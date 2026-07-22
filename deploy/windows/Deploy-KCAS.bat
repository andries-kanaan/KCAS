@echo off
setlocal

where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh.exe^) is required but was not found on PATH.
    echo Install PowerShell 7, then run this deployment again.
    pause
    exit /b 1
)

set "KCAS_DEPLOY_SCRIPT=%~dp0Deploy-KCAS.ps1"
pwsh.exe -NoLogo -NoProfile -Command "$process = Start-Process -FilePath 'pwsh.exe' -Verb RunAs -Wait -PassThru -ArgumentList @('-NoLogo','-NoProfile','-ExecutionPolicy','Bypass','-File',$env:KCAS_DEPLOY_SCRIPT); exit $process.ExitCode"
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
