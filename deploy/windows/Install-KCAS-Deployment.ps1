#requires -Version 7.0
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\Deploy\KCAS',
    [string]$RepositoryPath = 'D:\Deploy\KCAS\repo',
    [string]$MySqlBasePath = 'D:\wamp64\bin\mysql\mysql9.1.0',
    [string]$ScheduledTaskName = 'KCAS',
    [string]$DirectHealthUrl = 'http://127.0.0.1:5000/health/ready',
    [string]$ProxyHealthUrl = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$installRootPath = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$repositoryFullPath = [System.IO.Path]::GetFullPath($RepositoryPath).TrimEnd('\')
if (-not (Test-Path -LiteralPath (Join-Path $repositoryFullPath '.git') -PathType Container)) {
    throw "KCAS Git repository not found: $repositoryFullPath"
}
if (-not (Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction SilentlyContinue)) {
    throw "Scheduled Task '$ScheduledTaskName' was not found."
}
foreach ($command in @('git.exe')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' is not installed or is not on PATH."
    }
}

$sharedPath = Join-Path $installRootPath 'shared'
$keysPath = Join-Path $sharedPath 'DataProtectionKeys'
$inboxPath = Join-Path $installRootPath 'inbox'
$backupPath = Join-Path $sharedPath ("legacy-deployment-backup\{0}" -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
foreach ($directory in @($installRootPath,$sharedPath,$keysPath,$inboxPath,$backupPath)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

foreach ($legacyName in @('Deploy-KCAS.bat','Deploy-KCAS.ps1')) {
    $legacyPath = Join-Path $installRootPath $legacyName
    if (Test-Path -LiteralPath $legacyPath -PathType Leaf) {
        Copy-Item -LiteralPath $legacyPath -Destination (Join-Path $backupPath $legacyName)
    }
}

$sharedConfigurationPath = Join-Path $sharedPath 'appsettings.Production.json'
if (-not (Test-Path -LiteralPath $sharedConfigurationPath -PathType Leaf)) {
    $configurationCandidates = @(
        (Join-Path $installRootPath 'publish\appsettings.Production.json'),
        (Join-Path $repositoryFullPath 'src\KCAS.Admin\appsettings.Production.json')
    )
    $configurationSource = $configurationCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($configurationSource)) {
        throw "Production appsettings could not be found. Place it at '$sharedConfigurationPath' and rerun the installer."
    }
    Copy-Item -LiteralPath $configurationSource -Destination $sharedConfigurationPath
    Write-Host "Preserved production configuration from '$configurationSource'."
}

$oldKeysPath = Join-Path $installRootPath 'publish\App_Data\DataProtectionKeys'
if ((Test-Path -LiteralPath $oldKeysPath -PathType Container) -and -not (Get-ChildItem -LiteralPath $keysPath -File -ErrorAction SilentlyContinue)) {
    Copy-Item -Path (Join-Path $oldKeysPath '*') -Destination $keysPath -Recurse
    Write-Host 'Preserved existing Data Protection keys.'
}

$remoteUrl = (& git.exe -C $repositoryFullPath remote get-url origin).Trim()
if ($LASTEXITCODE -ne 0 -or $remoteUrl -notmatch 'github\.com[/:](?<name>[^\s]+?)(?:\.git)?$') {
    throw "Could not determine the GitHub repository from '$remoteUrl'."
}
$githubRepository = $matches.name.TrimEnd('/')

$settings = [ordered]@{
    schemaVersion = 1
    repositoryPath = $repositoryFullPath
    githubRepository = $githubRepository
    deploymentReleaseTagPrefix = 'deploy-'
    scheduledTaskName = $ScheduledTaskName
    mySqlBasePath = $MySqlBasePath
    directHealthUrl = $DirectHealthUrl
    proxyHealthUrl = $ProxyHealthUrl
    installedAtUtc = [DateTime]::UtcNow.ToString('O')
}
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sharedPath 'deployment-operator.json') -Encoding utf8NoBOM

$toolNames = @('Deploy-KCAS.bat','Deploy-KCAS.ps1','Deploy-KCAS-Release.ps1','Rollback-KCAS-Release.ps1')
foreach ($toolName in $toolNames) {
    $sourcePath = Join-Path $PSScriptRoot $toolName
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { throw "Installer input missing: $sourcePath" }
    Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $installRootPath $toolName) -Force
}

Write-Host ''
Write-Host 'One-click immutable deployment is installed.' -ForegroundColor Green
Write-Host "The previous deployment files are backed up at '$backupPath'."
Write-Host "Future deployments: double-click '$installRootPath\Deploy-KCAS.bat'."
