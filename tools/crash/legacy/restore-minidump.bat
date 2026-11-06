@echo off
chcp 936 >nul
setlocal
REM ============================================================
REM 6-恢复小内存转储.bat
REM 把 CrashDumpEnabled 还原为 3（本次改动前的原值）
REM 也可用 reg import crashcontrol_backup.reg 完整还原
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
echo   即将把转储类型恢复为「小内存转储」(CrashDumpEnabled=3)。
echo   这是本次改动前的原值。
pause
reg add "%KEY%" /v CrashDumpEnabled /t REG_DWORD /d 3 /f >nul
if errorlevel 1 (
    echo   [X] 写入失败。
    pause
    exit /b 1
)
echo.
echo   [OK] 已恢复，当前值：
reg query "%KEY%" /v CrashDumpEnabled
echo   请重启电脑使其生效。
echo.
pause
