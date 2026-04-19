@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-mod.ps1"
set "EXIT_CODE=%ERRORLEVEL%"

echo.
pause
exit /b %EXIT_CODE%
