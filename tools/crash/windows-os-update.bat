@echo off
chcp 936 >nul
setlocal
REM Windows 11 OS update only. Restores the 2045 pause when finished.
REM GB18030 + CRLF. No parentheses in this file name.
net session >nul 2>&1
if "%errorlevel%"=="0" goto :ADMIN
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b
:ADMIN
set "RUNLOG=%~dp0windows-os-update-run.log"
echo === %date% %time% ADMIN OK === > "%RUNLOG%"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0windows-os-update.ps1" -Install
echo EXIT %ERRORLEVEL% >> "%RUNLOG%"
echo.
pause
exit /b
