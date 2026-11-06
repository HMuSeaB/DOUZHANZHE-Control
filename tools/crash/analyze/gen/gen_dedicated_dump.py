# -*- coding: utf-8 -*-
"""生成成对的 DedicatedDumpFile 修复/撤销脚本（GB18030 + CRLF，无 BOM）。

v2 修复（2026-11-05 20:30）：
  1) 主用文件名去掉半角括号 —— 旧名 `7-修复内核转储(专用转储文件).bat` 的 `)`
     在 `if (...)` 块内被 `%~f0` 展开后提前闭合括号块，cmd 解析崩、窗口一闪而过。
  2) 提权逻辑改成 goto 版，`%~f0` 不放进任何括号块。
  3) 写 tools/7_run.log 运行日志，即使窗口闪退也能确认跑到哪一步。
  4) 提权成功后顺手复制 Minidump 到 dumps\\，一次运行即可取到新转储。
  5) 旧文件也覆盖为同样的安全内容，避免误点旧入口。
"""
import os

BASE = r'C:\Users\36230\WorkBuddy\2026-11-04-22-45-13'

ON = r'''@echo off
chcp 936 >nul
setlocal
REM ============================================================
REM 7-修复内核转储.bat  (v2 安全版)
REM 背景：页面文件只在 D:\，C: 盘没有。
REM   2026-11-05 18:04 / 19:24 两次崩溃，volmgr 事件 161 均记录
REM   「转储文件创建失败」，只写出 minidump，CrashDumpEnabled 被回退为 3。
REM 方案：微软官方 DedicatedDumpFile —— 在 D: 预置专用转储文件，
REM   崩溃时直接写入它，不依赖启动卷上的页面文件。
REM 会改动：CrashControl 下 4 个值 + D: 预占约 8 GB
REM 顺带把 C:\Windows\Minidump 的转储复制到本目录 dumps\
REM 不改变驱动校验行为，不会造成开机蓝屏；必须重启生效。
REM 还原：8-撤销专用转储文件.bat
REM 编码：GB18030 + CRLF
REM ============================================================
net session >nul 2>&1
if "%errorlevel%"=="0" goto :ADMIN
echo 需要管理员权限，正在请求提权 ...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:ADMIN
set "KEY=HKLM\SYSTEM\CurrentControlSet\Control\CrashControl"
set "RUNLOG=%~dp0tools\7_run.log"
echo === %date% %time% ADMIN OK === > "%RUNLOG%"
if not exist "%~dp0dumps" mkdir "%~dp0dumps" >nul 2>&1
copy /y "C:\Windows\Minidump\*.dmp" "%~dp0dumps\" >nul 2>&1
echo copied minidumps >> "%RUNLOG%"
echo.
echo   当前转储类型：
reg query "%KEY%" /v CrashDumpEnabled
echo.
echo   即将设置：
echo     DedicatedDumpFile  = D:\dedicateddumpfile.sys  （预占约 8 GB）
echo     DumpFileSize       = 8192 (MB)
echo     IgnorePagefileSize = 1
echo     CrashDumpEnabled   = 2  （内核内存转储）
echo.
echo   - 会占用 D: 约 8 GB（当前剩余约 31 GB）
echo   - 不改驱动校验行为，不存在开机蓝屏风险
echo   - 会先备份原设置到 crashcontrol_backup_v2.reg
echo   - 必须重启才生效
echo.
echo   不想改请直接关闭本窗口，或按任意键继续。
pause

echo backup >> "%RUNLOG%"
reg export "%KEY%" "%~dp0crashcontrol_backup_v2.reg" /y >nul
if errorlevel 1 goto :FAIL_EXPORT

echo add1 >> "%RUNLOG%"
reg add "%KEY%" /v DedicatedDumpFile /t REG_SZ /d "D:\dedicateddumpfile.sys" /f >nul
if errorlevel 1 goto :ROLLBACK
echo add2 >> "%RUNLOG%"
reg add "%KEY%" /v DumpFileSize /t REG_DWORD /d 8192 /f >nul
if errorlevel 1 goto :ROLLBACK
echo add3 >> "%RUNLOG%"
reg add "%KEY%" /v IgnorePagefileSize /t REG_DWORD /d 1 /f >nul
if errorlevel 1 goto :ROLLBACK
echo add4 >> "%RUNLOG%"
reg add "%KEY%" /v CrashDumpEnabled /t REG_DWORD /d 2 /f >nul
if errorlevel 1 goto :ROLLBACK
echo DONE OK >> "%RUNLOG%"
goto :OK

:ROLLBACK
echo ROLLBACK >> "%RUNLOG%"
reg import "%~dp0crashcontrol_backup_v2.reg" >nul
reg delete "%KEY%" /v DedicatedDumpFile /f >nul 2>&1
reg delete "%KEY%" /v DumpFileSize /f >nul 2>&1
reg delete "%KEY%" /v IgnorePagefileSize /f >nul 2>&1
echo   [X] 写入失败，已回滚到原设置。
pause
exit /b 1

:FAIL_EXPORT
echo FAIL_EXPORT >> "%RUNLOG%"
echo   [X] 备份失败，已中止，未改动任何设置。
pause
exit /b 1

:OK
echo.
echo   [OK] 已设置，当前值：
reg query "%KEY%" /v CrashDumpEnabled
reg query "%KEY%" /v DedicatedDumpFile
reg query "%KEY%" /v DumpFileSize
echo.
echo   备份：%~dp0crashcontrol_backup_v2.reg
echo   已把 Minidump 复制到 dumps\ 供分析。
echo   请重启电脑使其生效（这次一定要重启）。
echo   之后再蓝屏，双击「复制蓝屏转储.bat」取 MEMORY.DMP 和新 Minidump。
echo   还原请用 8-撤销专用转储文件.bat。
echo.
pause
'''

OFF = r'''@echo off
chcp 936 >nul
setlocal
REM ============================================================
REM 8-撤销专用转储文件.bat
REM 删除 7 号新建的 3 个值，并把 CrashDumpEnabled 还原为 3
REM 注意：D:\dedicateddumpfile.sys 被系统进程持有句柄，
REM       需重启后才能删除；本脚本不会自动删文件。
REM 编码：GB18030 + CRLF
REM ============================================================
net session >nul 2>&1
if "%errorlevel%"=="0" goto :ADMIN
echo 需要管理员权限，正在请求提权 ...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:ADMIN
set "KEY=HKLM\SYSTEM\CurrentControlSet\Control\CrashControl"
echo.
echo   即将撤销专用转储文件设置，并把转储类型还原为「小内存转储」。
pause
reg delete "%KEY%" /v DedicatedDumpFile /f >nul 2>&1
reg delete "%KEY%" /v DumpFileSize /f >nul 2>&1
reg delete "%KEY%" /v IgnorePagefileSize /f >nul 2>&1
reg add "%KEY%" /v CrashDumpEnabled /t REG_DWORD /d 3 /f >nul
if errorlevel 1 (
    echo   [X] 写入失败。
    pause
    exit /b 1
)
echo.
echo   [OK] 已还原：
reg query "%KEY%" /v CrashDumpEnabled
echo.
echo   请重启。重启后如需释放磁盘，可手动删除
echo   D:\dedicateddumpfile.sys （约 8 GB）。脚本不会替你删。
echo   也可用 reg import crashcontrol_backup_v2.reg 恢复其他值。
echo.
pause
'''


def write(name, txt):
    data = txt.replace('\r\n', '\n').replace('\n', '\r\n').encode('gb18030')
    p = os.path.join(BASE, name)
    with open(p, 'wb') as f:
        f.write(data)
    raw = open(p, 'rb').read()
    print('%s: bytes=%d CRLF一致=%s GB18030可解码=%s BOM=%s' % (
        name, len(raw),
        raw.count(b'\r\n') == raw.count(b'\n'),
        bool(raw.decode('gb18030')),
        raw.startswith(b'\xef\xbb\xbf')))
    for line in raw.decode('gb18030').splitlines():
        s = line.strip()
        if s.startswith('reg add') or s.startswith('reg delete') or s.startswith('reg export'):
            print('   ', s)


write('7-修复内核转储.bat', ON)
write('7-修复内核转储(专用转储文件).bat', ON)
write('8-撤销专用转储文件.bat', OFF)
