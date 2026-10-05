@echo off
setlocal EnableExtensions
set "SCRIPT_DIR=%~dp0"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%update.ps1"
exit /b %ERRORLEVEL%
