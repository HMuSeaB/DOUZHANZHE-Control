@echo off
chcp 936 >nul
REM ============================================================
REM 关闭驱动验证器（随时可用；改完请重启）
REM 编码: GB18030 + CRLF
REM ============================================================
setlocal
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

verifier /reset
echo.
echo 已清除。请重启电脑完成生效。
echo.
pause
exit /b
