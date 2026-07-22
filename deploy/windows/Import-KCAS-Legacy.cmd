@echo off
setlocal

where pwsh.exe >nul 2>&1
if errorlevel 1 (
    echo PowerShell 7 ^(pwsh.exe^) is required but was not found on PATH.
    exit /b 1
)

pwsh.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Import-KCAS-Legacy.ps1" %*
exit /b %ERRORLEVEL%
