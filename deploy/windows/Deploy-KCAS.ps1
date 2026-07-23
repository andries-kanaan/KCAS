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

function Get-GitHubWorkflowRunForCommit {
    param(
        [string]$Repository,
        [string]$Commit,
        [hashtable]$Headers
    )

    $runsApiUrl = "https://api.github.com/repos/$Repository/actions/runs?head_sha=$Commit&event=push&per_page=20"
    $runs = Invoke-RestMethod -Uri $runsApiUrl -Headers $Headers -Method Get
    return @($runs.workflow_runs |
        Where-Object { $_.name -eq 'Windows release package' } |
        Sort-Object created_at -Descending |
        Select-Object -First 1)
}

function Get-DeploymentGitHubToken {
    param([object]$Settings, [string]$InstallRootPath)

    if (-not [string]::IsNullOrWhiteSpace($env:KCAS_GITHUB_TOKEN)) {
        return $env:KCAS_GITHUB_TOKEN.Trim()
    }

    $tokenPath = $null
    if ($Settings.PSObject.Properties.Name -contains 'githubTokenPath' -and -not [string]::IsNullOrWhiteSpace([string]$Settings.githubTokenPath)) {
        $tokenPath = [string]$Settings.githubTokenPath
    }
    else {
        $tokenPath = Join-Path $InstallRootPath 'shared\github-token.txt'
    }

    if (Test-Path -LiteralPath $tokenPath -PathType Leaf) {
        return (Get-Content -LiteralPath $tokenPath -Raw).Trim()
    }

    return $null
}

function New-GitHubRequestHeaders {
    param([string]$Token)

    $headers = @{
        'User-Agent' = 'KCAS-Immutable-Deployment'
        'Accept' = 'application/vnd.github+json'
        'X-GitHub-Api-Version' = '2022-11-28'
    }
    if (-not [string]::IsNullOrWhiteSpace($Token)) {
        $headers.Authorization = "Bearer $Token"
    }

    return $headers
}

$installRootPath = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$settingsPath = Join-Path $installRootPath 'shared\deployment-operator.json'
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "One-click deployment is not installed. Run Install-KCAS-Deployment.ps1 first. Missing: $settingsPath"
}
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if ($settings.schemaVersion -ne 1) { throw "Unsupported deployment settings in '$settingsPath'." }

$repositoryPath = [System.IO.Path]::GetFullPath([string]$settings.repositoryPath)
$deploymentReleaseTagPrefix = if ([string]::IsNullOrWhiteSpace([string]$settings.deploymentReleaseTagPrefix)) { 'deploy-' } else { [string]$settings.deploymentReleaseTagPrefix }
$scheduledTaskName = if ([string]::IsNullOrWhiteSpace([string]$settings.scheduledTaskName)) { 'KCAS' } else { [string]$settings.scheduledTaskName }
$mySqlBasePath = if ([string]::IsNullOrWhiteSpace([string]$settings.mySqlBasePath)) { 'D:\wamp64\bin\mysql\mysql9.1.0' } else { [string]$settings.mySqlBasePath }
$directHealthUrl = if ($settings.PSObject.Properties.Name -notcontains 'directHealthUrl' -or [string]::IsNullOrWhiteSpace([string]$settings.directHealthUrl)) { 'http://127.0.0.1:5000/health/ready' } else { [string]$settings.directHealthUrl }
$proxyHealthUrl = if ($SkipProxyHealthCheck) { '' } else { [string]$settings.proxyHealthUrl }
$sharedConfigurationPath = Join-Path $installRootPath 'shared\appsettings.Production.json'
$releaseEnginePath = Join-Path $installRootPath 'Deploy-KCAS-Release.ps1'

foreach ($requiredPath in @($repositoryPath, $sharedConfigurationPath, $releaseEnginePath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) { throw "Required deployment path not found: $requiredPath" }
}
foreach ($command in @('git.exe')) {
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

Write-Host "Waiting for the tested Windows package for $commit..."
$releaseTag = "$deploymentReleaseTagPrefix$commit"
$releaseApiUrl = "https://api.github.com/repos/$githubRepository/releases/tags/$releaseTag"
$githubToken = Get-DeploymentGitHubToken -Settings $settings -InstallRootPath $installRootPath
$requestHeaders = New-GitHubRequestHeaders -Token $githubToken
$release = $null
$releaseError = $null
$workflowError = $null
$lastWorkflowCheckUtc = [DateTime]::MinValue
$deadline = [DateTime]::UtcNow.AddMinutes(20)
do {
    try {
        $release = Invoke-RestMethod -Uri $releaseApiUrl -Headers $requestHeaders -Method Get
        $releaseError = $null
    }
    catch {
        $releaseError = $_.Exception.Message
        if ([string]::IsNullOrWhiteSpace($githubToken) -and $releaseError -like '*API rate limit exceeded*') {
            throw "GitHub API rate limit was exceeded while waiting for '$releaseTag'. Create a fine-grained GitHub token with public repository read access and save it outside Git at '$installRootPath\shared\github-token.txt', or set KCAS_GITHUB_TOKEN for the deployment process, then retry."
        }

        if ([DateTime]::UtcNow -ge $lastWorkflowCheckUtc.AddSeconds(60)) {
            $lastWorkflowCheckUtc = [DateTime]::UtcNow
            try {
                $workflowRun = Get-GitHubWorkflowRunForCommit -Repository $githubRepository -Commit $commit -Headers $requestHeaders
                if ($workflowRun.Count -gt 0) {
                    $run = $workflowRun[0]
                    if ($run.status -eq 'completed' -and $run.conclusion -ne 'success') {
                        throw "The GitHub 'Windows release package' workflow for '$commit' completed with conclusion '$($run.conclusion)' before publishing '$releaseTag'. Review $($run.html_url)"
                    }

                    if ($run.status -in @('queued','in_progress','waiting','requested')) {
                        Write-Host "GitHub release workflow is $($run.status); waiting for '$releaseTag'. Run: $($run.html_url)"
                    }
                }
            }
            catch {
                $workflowError = $_.Exception.Message
                if ($workflowError -like "The GitHub 'Windows release package' workflow*") {
                    throw $workflowError
                }
            }
        }

        Start-Sleep -Seconds 10
    }
} while ($null -eq $release -and [DateTime]::UtcNow -lt $deadline)
if ($null -eq $release) {
    throw "The tested deployment release '$releaseTag' did not become available within 20 minutes. Last GitHub release result: $releaseError$(if ($workflowError) { " Last workflow result: $workflowError" })"
}
if ([string]$release.target_commitish -ne $commit) {
    throw "Deployment release '$releaseTag' targets '$($release.target_commitish)', not '$commit'."
}

$inboxPath = Join-Path $installRootPath "inbox\$commit"
if (Test-Path -LiteralPath $inboxPath) { Remove-Item -LiteralPath $inboxPath -Recurse -Force }
New-Item -ItemType Directory -Path $inboxPath -Force | Out-Null
Write-Host 'Downloading the verified release from GitHub...'
$packageAssets = @($release.assets | Where-Object { $_.name -like 'KCAS-*-win-x64.zip' })
if ($packageAssets.Count -ne 1) { throw "Expected one release ZIP on '$releaseTag'; found $($packageAssets.Count)." }
$checksumAssets = @($release.assets | Where-Object { $_.name -eq "$($packageAssets[0].name).sha256" })
if ($checksumAssets.Count -ne 1) { throw "Expected one checksum for '$($packageAssets[0].name)' on '$releaseTag'; found $($checksumAssets.Count)." }
$packagePath = Join-Path $inboxPath $packageAssets[0].name
$checksumPath = Join-Path $inboxPath $checksumAssets[0].name
Invoke-WebRequest -Uri $packageAssets[0].browser_download_url -Headers $requestHeaders -OutFile $packagePath
Invoke-WebRequest -Uri $checksumAssets[0].browser_download_url -Headers $requestHeaders -OutFile $checksumPath

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
    DirectHealthUrl = $directHealthUrl
}
if (-not [string]::IsNullOrWhiteSpace($proxyHealthUrl)) { $deploymentArguments.ProxyHealthUrl = $proxyHealthUrl }
if (-not (Test-Path -LiteralPath (Join-Path $installRootPath 'current'))) {
    Write-Host 'First immutable deployment detected; the existing publish path will be transitioned without changing the Scheduled Task.'
}

$inactiveReleasePath = Join-Path $installRootPath "releases\$commit"
if (Test-Path -LiteralPath $inactiveReleasePath -PathType Container) {
    $failedReleaseDirectory = Join-Path $installRootPath 'shared\failed-releases'
    New-Item -ItemType Directory -Path $failedReleaseDirectory -Force | Out-Null
    $failedReleasePath = Join-Path $failedReleaseDirectory ("{0}-{1}" -f $commit, [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
    Move-Item -LiteralPath $inactiveReleasePath -Destination $failedReleasePath
    Write-Host "Preserved the inactive release from an earlier failed attempt at '$failedReleasePath'."
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
