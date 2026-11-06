@echo off
setlocal
net session >nul 2>&1
if not "%errorlevel%"=="0" (
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
if not exist "C:\Users\36230\WorkBuddy\2026-11-04-22-45-13\dumps" mkdir "C:\Users\36230\WorkBuddy\2026-11-04-22-45-13\dumps"
copy /y "C:\Windows\Minidump\*.dmp" "C:\Users\36230\WorkBuddy\2026-11-04-22-45-13\dumps\" >nul
if exist "C:\Windows\MEMORY.DMP" copy /y "C:\Windows\MEMORY.DMP" "C:\Users\36230\WorkBuddy\2026-11-04-22-45-13\dumps\" >nul
echo OK
exit /b
