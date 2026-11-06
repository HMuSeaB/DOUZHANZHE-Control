@echo off
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
