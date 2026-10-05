@echo off
setlocal EnableExtensions
set "SCRIPT_DIR=%~dp0"
set "SHOULD_WAIT=1"

for %%A in (%*) do (
    if /I "%%~A"=="-NonInteractive" set "SHOULD_WAIT=0"
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%bootstrap.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"

echo.
if "%EXIT_CODE%"=="0" (
    echo Package Management bootstrap completed successfully.
) else (
    echo Package Management bootstrap failed with exit code %EXIT_CODE%.
)

if "%SHOULD_WAIT%"=="1" (
    echo.
    echo Press any key to close this window...
    pause >nul
)

exit /b %EXIT_CODE%
