@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-local-admin.ps1"
if errorlevel 1 (
  echo.
  echo Local Super Admin setup failed.
  exit /b 1
)
endlocal
