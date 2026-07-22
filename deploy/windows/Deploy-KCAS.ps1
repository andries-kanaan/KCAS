#requires -Version 7.0
#requires -RunAsAdministrator

[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\Deploy\KCAS',
    [switch]$SkipProxyHealthCheck
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

function Invoke-CheckedCommand {
    param([string]$FilePath, [string[]]$Arguments, [string]$FailureMessage)
    $output = & $FilePath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $detail = ($output | Out-String).Trim()
        throw "$FailureMessage$(if ($detail) { ": $detail" })"
    }
    return $output
}

function Get-ConnectionValue {
    param([System.Data.Common.DbConnectionStringBuilder]$Builder, [string[]]$Keys, [object]$DefaultValue)
    foreach ($key in $Keys) {
        if ($Builder.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace([string]$Builder[$key])) {
            return $Builder[$key]
        }
    }
    return $DefaultValue
}

function Get-GitHubRepositoryName {
    param([string]$RemoteUrl)
    if ($RemoteUrl -match 'github\.com[/:](?<name>[^\s]+?)(?:\.git)?$') {
        return $matches.name.TrimEnd('/')
    }
    throw "Could not determine the GitHub repository from remote '$RemoteUrl'."
}

$installRootPath = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$settingsPath = Join-Path $installRootPath 'shared\deployment-operator.json'
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "One-click deployment is not installed. Run Install-KCAS-Deployment.ps1 first. Missing: $settingsPath"
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ($settings.schemaVersion -ne 1) { throw "Unsupported deployment settings in '$settingsPath'." }

$repositoryPath = [System.IO.Path]::GetFullPath([string]$settings.repositoryPath)
$workflow = if ([string]::IsNullOrWhiteSpace([string]$settings.workflow)) { 'windows-release.yml' } else { [string]$settings.workflow }
$scheduledTaskName = if ([string]::IsNullOrWhiteSpace([string]$settings.scheduledTaskName)) { 'KCAS' } else { [string]$settings.scheduledTaskName }
$mySqlBasePath = if ([string]::IsNullOrWhiteSpace([string]$settings.mySqlBasePath)) { 'D:\wamp64\bin\mysql\mysql9.1.0' } else { [string]$settings.mySqlBasePath }
$proxyHealthUrl = if ($SkipProxyHealthCheck) { '' } else { [string]$settings.proxyHealthUrl }
$sharedConfigurationPath = Join-Path $installRootPath 'shared\appsettings.Production.json'
$releaseEnginePath = Join-Path $installRootPath 'Deploy-KCAS-Release.ps1'

foreach ($requiredPath in @($repositoryPath, $sharedConfigurationPath, $releaseEnginePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) { throw "Required deployment path not found: $requiredPath" }
}
foreach ($command in @('git.exe','gh.exe')) {
    if (-not (Get-Command $command -ErrorAction SilentlyContinue)) { throw "Required command '$command' is not installed or is not on PATH." }
}

Write-Host 'Checking the reviewed main branch...'
$branch = (Invoke-CheckedCommand 'git.exe' @('-C',$repositoryPath,'branch','--show-current') 'Could not inspect the server repository branch' | Out-String).Trim()
if ($branch -ne 'main') { throw "Server repository is on '$branch', not 'main'." }
$workingTree = (Invoke-CheckedCommand 'git.exe' @('-C',$repositoryPath,'status','--porcelain') 'Could not inspect the server repository' | Out-String).Trim()
if ($workingTree) { throw "Server repository has local changes. Review them before deployment:`n$workingTree" }

$null = Invoke-CheckedCommand 'git.exe' @('-C',$repositoryPath,'pull','--ff-only','origin','main') 'Could not fast-forward the server repository'
$commit = (Invoke-CheckedCommand 'git.exe' @('-C',$repositoryPath,'rev-parse','HEAD') 'Could not identify the deployment commit' | Out-String).Trim().ToLowerInvariant()
if ($commit -notmatch '^[0-9a-f]{40}$') { throw "Git returned an invalid commit '$commit'." }

$remoteUrl = (Invoke-CheckedCommand 'git.exe' @('-C',$repositoryPath,'remote','get-url','origin') 'Could not inspect the GitHub remote' | Out-String).Trim()
$githubRepository = if ([string]::IsNullOrWhiteSpace([string]$settings.githubRepository)) { Get-GitHubRepositoryName $remoteUrl } else { [string]$settings.githubRepository }
$null = Invoke-CheckedCommand 'gh.exe' @('auth','status','--hostname','github.com') 'GitHub CLI is not authenticated'

# Refresh the non-running deployment engine and rollback helper from reviewed main.
foreach ($toolName in @('Deploy-KCAS-Release.ps1','Rollback-KCAS-Release.ps1')) {
    $sourceTool = Join-Path $repositoryPath "deploy\windows\$toolName"
    if (-not (Test-Path -LiteralPath $sourceTool -PathType Leaf)) { throw "Reviewed deployment tool not found: $sourceTool" }
    Copy-Item -LiteralPath $sourceTool -Destination (Join-Path $installRootPath $toolName) -Force
}

$currentManifestPath = Join-Path $installRootPath 'current\deployment-manifest.json'
if (Test-Path -LiteralPath $currentManifestPath -PathType Leaf) {
    $currentManifest = Get-Content -LiteralPath $currentManifestPath -Raw | ConvertFrom-Json
    if ([string]$currentManifest.gitCommit -eq $commit) {
        Write-Host "KCAS is already running the latest main commit $commit. Nothing to deploy."
        exit 0
    }
}

Write-Host "Finding the tested Windows package for $commit..."
$runListJson = Invoke-CheckedCommand 'gh.exe' @('run','list','--repo',$githubRepository,'--workflow',$workflow,'--commit',$commit,'--limit','10','--json','databaseId,status,conclusion,headSha,createdAt') 'Could not inspect GitHub release runs'
$runs = @((($runListJson | Out-String) | ConvertFrom-Json) | Where-Object { $_.headSha -eq $commit })
if ($runs.Count -eq 0) {
    Write-Host 'No packaging run exists yet; starting one in GitHub Actions...'
    $null = Invoke-CheckedCommand 'gh.exe' @('workflow','run',$workflow,'--repo',$githubRepository,'--ref','main','-f',"version_label=deploy-$($commit.Substring(0,12))") 'Could not start the Windows release workflow'
    $deadline = [DateTime]::UtcNow.AddMinutes(2)
    do {
        Start-Sleep -Seconds 3
        $runListJson = Invoke-CheckedCommand 'gh.exe' @('run','list','--repo',$githubRepository,'--workflow',$workflow,'--commit',$commit,'--limit','10','--json','databaseId,status,conclusion,headSha,createdAt') 'Could not find the started Windows release workflow'
        $runs = @((($runListJson | Out-String) | ConvertFrom-Json) | Where-Object { $_.headSha -eq $commit })
    } while ($runs.Count -eq 0 -and [DateTime]::UtcNow -lt $deadline)
    if ($runs.Count -eq 0) { throw 'The Windows release workflow did not appear within two minutes.' }
}

$run = $runs | Sort-Object @{ Expression = { if ($_.status -eq 'completed' -and $_.conclusion -eq 'success') { 0 } else { 1 } } }, @{ Expression = 'createdAt'; Descending = $true } | Select-Object -First 1
if ($run.status -ne 'completed') {
    Write-Host "Waiting for GitHub Actions run $($run.databaseId) to finish..."
    $null = Invoke-CheckedCommand 'gh.exe' @('run','watch',[string]$run.databaseId,'--repo',$githubRepository,'--exit-status') 'The Windows release workflow failed'
}
elseIf ($run.conclusion -ne 'success') {
    throw "GitHub Actions run $($run.databaseId) completed with '$($run.conclusion)'."
}

$inboxPath = Join-Path $installRootPath "inbox\$commit"
if (Test-Path -LiteralPath $inboxPath) { Remove-Item -LiteralPath $inboxPath -Recurse -Force }
New-Item -ItemType Directory -Path $inboxPath -Force | Out-Null
Write-Host 'Downloading the verified release from GitHub...'
$null = Invoke-CheckedCommand 'gh.exe' @('run','download',[string]$run.databaseId,'--repo',$githubRepository,'--name',"kcas-windows-$commit",'--dir',$inboxPath) 'Could not download the Windows release artifact'

$packages = @(Get-ChildItem -LiteralPath $inboxPath -File -Filter 'KCAS-*-win-x64.zip')
if ($packages.Count -ne 1) { throw "Expected exactly one KCAS release ZIP in '$inboxPath'; found $($packages.Count)." }
$packagePath = $packages[0].FullName
$checksumPath = "$packagePath.sha256"
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) { throw "Release checksum not found: $checksumPath" }

$configuration = Get-Content -LiteralPath $sharedConfigurationPath -Raw | ConvertFrom-Json
$connectionString = [string]$configuration.ConnectionStrings.DefaultConnection
if ([string]::IsNullOrWhiteSpace($connectionString)) { throw "Production configuration has no 'ConnectionStrings:DefaultConnection'." }
$connectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
$connectionBuilder.set_ConnectionString($connectionString)
$mySqlHost = [string](Get-ConnectionValue $connectionBuilder @('server','host','data source') '127.0.0.1')
$mySqlPort = [int](Get-ConnectionValue $connectionBuilder @('port') 3306)
$database = [string](Get-ConnectionValue $connectionBuilder @('database','initial catalog') 'kcas_blazor')
$mySqlUser = [string](Get-ConnectionValue $connectionBuilder @('user','user id','uid','username') '')
$mySqlPassword = [string](Get-ConnectionValue $connectionBuilder @('password','pwd') '')
if ([string]::IsNullOrWhiteSpace($mySqlUser)) { throw 'Production connection string has no database user.' }

$deploymentArguments = @{
    PackagePath = $packagePath
    ChecksumPath = $checksumPath
    InstallRoot = $installRootPath
    ScheduledTaskName = $scheduledTaskName
    SharedConfigurationPath = $sharedConfigurationPath
    MySqlBasePath = $mySqlBasePath
    MySqlHost = $mySqlHost
    MySqlPort = $mySqlPort
    MySqlUser = $mySqlUser
    MySqlPassword = $mySqlPassword
    Database = $database
}
if (-not [string]::IsNullOrWhiteSpace($proxyHealthUrl)) { $deploymentArguments.ProxyHealthUrl = $proxyHealthUrl }
if (-not (Test-Path -LiteralPath (Join-Path $installRootPath 'current'))) {
    $deploymentArguments.UpdateScheduledTaskAction = $true
    Write-Host 'First immutable deployment detected; the existing KCAS Scheduled Task action will be transitioned automatically.'
}

Write-Host "Deploying tested commit $commit..."
& $releaseEnginePath @deploymentArguments

# Safely refresh the launchers for the next deployment after this run has completed parsing them.
foreach ($launcherName in @('Deploy-KCAS.bat','Deploy-KCAS.ps1')) {
    $sourceLauncher = Join-Path $repositoryPath "deploy\windows\$launcherName"
    if (Test-Path -LiteralPath $sourceLauncher -PathType Leaf) {
        Copy-Item -LiteralPath $sourceLauncher -Destination (Join-Path $installRootPath $launcherName) -Force
    }
}

Write-Host "KCAS now runs tested main commit $commit." -ForegroundColor Green
