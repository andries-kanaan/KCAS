@echo off
setlocal

where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh.exe^) is required but was not found on PATH.
    echo Install PowerShell 7, then run this installer again.
    pause
    exit /b 1
)

set "KCAS_INSTALL_SCRIPT=%~dp0Install-KCAS-Deployment.ps1"
pwsh.exe -NoLogo -NoProfile -Command "$process = Start-Process -FilePath 'pwsh.exe' -Verb RunAs -Wait -PassThru -ArgumentList @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$env:KCAS_INSTALL_SCRIPT); exit $process.ExitCode"
set "KCAS_EXIT_CODE=%ERRORLEVEL%"

echo.
if not "%KCAS_EXIT_CODE%"=="0" (
    echo KCAS deployment installation failed. Review the error above.
) else (
    echo KCAS one-click deployment was installed successfully.
)
echo.
pause
exit /b %KCAS_EXIT_CODE%
