@echo off
setlocal

fltmc >nul 2>&1
if errorlevel 1 (
    echo Requesting Administrator access for LAN sharing...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b %errorlevel%
)

set "PROJECT_ROOT=%~dp0"
if not exist "%PROJECT_ROOT%run-local-sqlserver.ps1" (
    set "PROJECT_ROOT=%USERPROFILE%\Documents\ChatGPT\SBLSS SYSTEM\"
)
if not exist "%PROJECT_ROOT%run-local-sqlserver.ps1" (
    echo Cannot find the SBLSS project folder or run-local-sqlserver.ps1.
    echo Expected: "%PROJECT_ROOT%run-local-sqlserver.ps1"
    pause
    exit /b 1
)
cd /d "%PROJECT_ROOT%"
echo Starting SBLSS with the SQL Server database in VMware...
echo Team URL: https://sblss-lan.local:8088/login
echo Keep this window open while the team uses the system.
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%run-local-sqlserver.ps1" -VmSqlServer -Share -ShareAddress 192.168.0.173 -ShareEntra -ShareHostName sblss-lan.local

if errorlevel 1 (
    echo.
    echo SBLSS could not start. Read the error above, then press any key to close.
    pause >nul
    exit /b 1
)

echo.
echo SBLSS has stopped. Press any key to close.
pause >nul
