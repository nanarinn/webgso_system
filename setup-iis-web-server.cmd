@echo off
setlocal

fltmc >nul 2>&1
if errorlevel 1 (
    echo Requesting Administrator access to set up the IIS web server...
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b %errorlevel%
)

set "PROJECT_ROOT=%~dp0"
if not exist "%PROJECT_ROOT%setup-iis-web-server.ps1" (
    set "PROJECT_ROOT=%USERPROFILE%\Documents\ChatGPT\SBLSS SYSTEM\"
)
if not exist "%PROJECT_ROOT%setup-iis-web-server.ps1" (
    echo Cannot find setup-iis-web-server.ps1.
    echo Expected: "%PROJECT_ROOT%setup-iis-web-server.ps1"
    pause
    exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%setup-iis-web-server.ps1"
if errorlevel 1 (
    echo.
    echo IIS setup stopped. Read the error above and fix the reported prerequisite before retrying.
    pause
    exit /b 1
)

echo.
echo SBLSS IIS web server is ready at https://sblss-lan.local:8088/login
pause
