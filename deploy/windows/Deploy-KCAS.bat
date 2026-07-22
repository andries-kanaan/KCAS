@echo off
setlocal

net session >nul 2>&1
if errorlevel 1 (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh.exe^) is required but was not found on PATH.
    echo Install PowerShell 7, then run this deployment again.
    pause
    exit /b 1
)

pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Deploy-KCAS.ps1"
set "KCAS_EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%KCAS_EXIT_CODE%"=="0" (
    echo KCAS deployment failed. Review the error above.
) else (
    echo KCAS deployment completed successfully.
)
echo.
pause
exit /b %KCAS_EXIT_CODE%
