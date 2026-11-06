@echo off
chcp 936 >nul
setlocal
REM ============================================================
REM 5-开启内核内存转储.bat
REM 把「小内存转储」(CrashDumpEnabled=3) 改为「内核内存转储」(=2)
REM 目的：下次蓝屏保留完整内核地址空间，才能用 !pool / !poolfind
REM       追溯被写坏的池块的 PoolTag 与分配者。
REM 不改驱动校验行为，不会造成开机蓝屏；只是下次崩溃多写几 GB。
REM 需要管理员 + 重启生效。配对脚本：6-恢复小内存转储.bat
REM 编码：GB18030 + CRLF
REM ============================================================
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    echo 需要管理员权限，正在请求提权 ...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
set "KEY=HKLM\SYSTEM\CurrentControlSet\Control\CrashControl"
echo.
echo   当前设置：
reg query "%KEY%" /v CrashDumpEnabled
echo.
echo   即将改为：内核内存转储 (CrashDumpEnabled=2)
echo   - 不改变开机时的驱动校验，不存在开机蓝屏风险
echo   - 下次蓝屏会把内核内存写到 C:\Windows\MEMORY.DMP（约 1-4 GB）
echo   - 页面文件 D:\pagefile.sys 约 7.4 GB，C: 剩 40 GB，空间足够
echo   - 会先把原设置备份到 crashcontrol_backup.reg
echo.
echo   不想改请直接关闭本窗口。
pause

reg export "%KEY%" "%~dp0crashcontrol_backup.reg" /y >nul
if errorlevel 1 (
    echo   [X] 备份失败，已中止，未改动任何设置。
    pause
    exit /b 1
)
reg add "%KEY%" /v CrashDumpEnabled /t REG_DWORD /d 2 /f >nul
if errorlevel 1 (
    echo   [X] 写入失败，正在用备份还原 ...
    reg import "%~dp0crashcontrol_backup.reg" >nul
    pause
    exit /b 1
)
echo.
echo   [OK] 已设置，当前值：
reg query "%KEY%" /v CrashDumpEnabled
echo.
echo   备份文件：%~dp0crashcontrol_backup.reg
echo   请重启电脑使其生效。
echo   之后再蓝屏，双击「复制蓝屏转储.bat」取 MEMORY.DMP 和新 Minidump。
echo   恢复请用 6-恢复小内存转储.bat。
echo.
pause
