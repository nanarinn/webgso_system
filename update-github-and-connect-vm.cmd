@echo off
setlocal
set "PROJECT_ROOT=%~dp0"
if not exist "%PROJECT_ROOT%update-github-and-connect-vm.ps1" (
    set "PROJECT_ROOT=%USERPROFILE%\Documents\ChatGPT\SBLSS SYSTEM\"
)
if not exist "%PROJECT_ROOT%update-github-and-connect-vm.ps1" (
    echo Cannot find the SBLSS project folder or update script.
    echo Expected: "%PROJECT_ROOT%update-github-and-connect-vm.ps1"
    pause
    exit /b 1
)
cd /d "%PROJECT_ROOT%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%PROJECT_ROOT%update-github-and-connect-vm.ps1"
if errorlevel 1 (
    echo.
    echo Update or VM connection check failed. Read the message above before retrying.
    pause
    exit /b 1
)
echo.
echo Done. Press any key to close this window.
pause >nul
