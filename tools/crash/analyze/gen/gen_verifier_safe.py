import os

BASE = r'C:\Users\36230\WorkBuddy\2026-11-04-22-45-13'

# 首轮只放 3 个最可疑的；原版一次放进 13 个（其中 7 个开机加载），开机风险明显偏大
drivers = 'Ld9BoxNetLwf.sys Ld9BoxSup.sys BAPIDRV64.sys'

txt = '''@echo off
chcp 936 >nul
REM ============================================================
REM 驱动验证器（安全版 v2）: 特殊池 0x1 + 池跟踪 0x8
REM 与 v1 的区别:
REM   1) /bootmode resetonbootfail —— 系统若启动失败，自动禁用验证器后重启
REM      （v1 用的 oneboot 在"开机就崩"时不一定能顺利走完一次开机）
REM   2) 首轮只验证 3 个驱动，不再一次放进 13 个开机加载型驱动
REM 风险仍在: 这 3 个里有 2 个是开机加载型，特殊池会在开机早期校验，
REM           小概率导致开机阶段蓝屏。不想冒这个险就别运行本脚本。
REM 回滚: 双击 4-关闭驱动验证器.bat，或恢复环境命令行执行 verifier /reset
REM 编码: GB18030 + CRLF
REM ============================================================
setlocal
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

echo.
echo  注意: 本操作会改变开机时的驱动校验方式，有小概率开机蓝屏。
echo  不想继续请直接关闭本窗口。
echo.
pause

echo [1/3] 清除已有设置 ...
verifier /reset >nul 2>&1

echo [2/3] 设置"启动失败自动回滚" ...
verifier /bootmode resetonbootfail
if not "%errorlevel%"=="0" (
    echo     设置失败，已中止，未开启验证
    goto :done
)

echo [3/3] 只对 3 个驱动启用特殊池 + 池跟踪 ...
verifier /flags 0x9 /driver ''' + drivers + '''
if not "%errorlevel%"=="0" (
    echo     启用失败，正在回滚 ...
    verifier /reset >nul 2>&1
    goto :done
)

echo.
echo === 当前设置（重启后生效） ===
verifier /querysettings
echo.
echo 请重启电脑，然后正常用 + 待机，等下一次蓝屏。
echo 新蓝屏若是 0xC1 / 0xC4，会直接写出驱动 .sys 名字，发我即可。
echo.
echo 万一开不了机:
echo   - resetonbootfail 会在启动失败时自动禁用验证器并重启；
echo   - 若仍不行，进恢复环境/安全模式的命令行执行 verifier /reset 后重启；
echo   - 或开机成功后双击 4-关闭驱动验证器.bat。
echo 测试结束请运行 4-关闭驱动验证器.bat（此模式不会自己过期）。
echo.

:done
echo.
pause
exit /b
'''

path = os.path.join(BASE, '3-开启驱动验证器(特殊池).bat')
data = txt.replace('\r\n', '\n').replace('\n', '\r\n').encode('gb18030')
with open(path, 'wb') as f:
    f.write(data)
raw = open(path, 'rb').read()
print('bytes=%d CRLF一致=%s GB18030可解码=%s BOM=%s' % (
    len(raw), raw.count(b'\n') == raw.count(b'\r\n'),
    bool(raw.decode('gb18030')), raw[:3] == b'\xef\xbb\xbf'))
