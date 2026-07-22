[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,
    [string]$ChecksumPath,
    [string]$InstallRoot = 'D:\Deploy\KCAS',
    [string]$ScheduledTaskName = 'KCAS',
    # Retained as an ignored compatibility switch for the pre-fix one-click launcher.
    [switch]$UpdateScheduledTaskAction,
    [string]$DirectHealthUrl = 'http://127.0.0.1:5143/health/ready',
    [string]$ProxyHealthUrl,
    [int]$HealthTimeoutSeconds = 60,
    [string]$SharedConfigurationPath,
    [string]$MySqlBasePath = $(if ($env:KCAS_MYSQL_BASE_PATH) { $env:KCAS_MYSQL_BASE_PATH } else { 'D:\wamp64\bin\mysql\mysql9.1.0' }),
    [int]$MySqlPort = $(if ($env:KCAS_MYSQL_PORT) { [int]$env:KCAS_MYSQL_PORT } else { 3306 }),
    [string]$MySqlHost = $(if ($env:KCAS_MYSQL_HOST) { $env:KCAS_MYSQL_HOST } else { '127.0.0.1' }),
    [string]$MySqlUser = $(if ($env:KCAS_MYSQL_USER) { $env:KCAS_MYSQL_USER } else { 'root' }),
    [string]$MySqlPassword = $env:KCAS_MYSQL_PASSWORD,
    [string]$Database = $(if ($env:KCAS_DATABASE) { $env:KCAS_DATABASE } else { 'kcas_blazor' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

function Assert-SafeInstallRoot {
    param([string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
    $driveRoot = [System.IO.Path]::GetPathRoot($resolved).TrimEnd('\')
    if ($resolved -eq $driveRoot -or $resolved.Length -lt ($driveRoot.Length + 8)) {
        throw "InstallRoot '$resolved' is too broad. Use a dedicated KCAS deployment directory."
    }
    return $resolved
}

function Remove-KcasJunction {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.LinkType -ne 'Junction') {
        throw "Refusing to replace '$Path' because it is not a directory junction."
    }
    [System.IO.Directory]::Delete($item.FullName)
}

function Set-CurrentRelease {
    param(
        [string]$CurrentPath,
        [string]$ReleasePath
    )

    $temporaryJunction = "$CurrentPath.next"
    Remove-KcasJunction -Path $temporaryJunction
    New-Item -ItemType Junction -Path $temporaryJunction -Target $ReleasePath | Out-Null
    Remove-KcasJunction -Path $CurrentPath
    [System.IO.Directory]::Move($temporaryJunction, $CurrentPath)
}

function Stop-KcasTask {
    param(
        [string]$TaskName,
        [string]$HealthUrl
    )

    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    if ($task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $TaskName
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        do {
            Start-Sleep -Milliseconds 500
            $task = Get-ScheduledTask -TaskName $TaskName
        } while ($task.State -eq 'Running' -and [DateTime]::UtcNow -lt $deadline)

        if ($task.State -eq 'Running') {
            throw "Scheduled Task '$TaskName' did not stop within 30 seconds."
        }
    }

    $healthUri = [System.Uri]$HealthUrl
    $listenerDeadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $healthUri.Port -ErrorAction SilentlyContinue)
        if ($listeners.Count -eq 0) { return }

        foreach ($processId in @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)) {
            $process = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
            if ($null -eq $process) { continue }
            $commandLine = [string]$process.CommandLine
            if ($process.Name -ne 'KCAS.Admin.exe' -and $commandLine -notmatch 'KCAS\.Admin(?:\.dll|\.exe)') {
                throw "Port $($healthUri.Port) remains owned by unexpected process '$($process.Name)' (PID $processId) after stopping Scheduled Task '$TaskName'."
            }
            Write-Host "Stopping lingering KCAS process '$($process.Name)' (PID $processId)..."
            Stop-Process -Id $processId -Force -ErrorAction Stop
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $listenerDeadline)

    throw "KCAS continued listening on port $($healthUri.Port) after Scheduled Task '$TaskName' was stopped."
}

function Wait-KcasHealth {
    param(
        [string]$Url,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10
            if ([int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 300) {
                return
            }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = $_.Exception.Message
        }
        Start-Sleep -Seconds 2
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "KCAS did not become healthy at '$Url' within $TimeoutSeconds seconds. Last result: $lastError"
}

function Backup-KcasDatabase {
    param(
        [string]$BackupPath
    )

    if ($Database -notmatch '^[A-Za-z0-9_]+$') {
        throw "Database name '$Database' contains unsupported characters."
    }

    $mysqlDump = Join-Path $MySqlBasePath 'bin\mysqldump.exe'
    $pluginDirectory = Join-Path $MySqlBasePath 'lib\plugin'
    if (-not (Test-Path -LiteralPath $mysqlDump)) {
        throw "MySQL dump tool not found at '$mysqlDump'."
    }

    $arguments = @(
        "--plugin-dir=$pluginDirectory",
        '--protocol=tcp',
        "--host=$MySqlHost",
        "--port=$MySqlPort",
        "--user=$MySqlUser",
        '--single-transaction',
        '--routines',
        '--triggers',
        '--events',
        '--no-tablespaces',
        '--default-character-set=utf8mb4',
        "--result-file=$BackupPath",
        $Database
    )

    $previousMysqlPassword = $env:MYSQL_PWD
    try {
        if (-not [string]::IsNullOrWhiteSpace($MySqlPassword)) {
            $env:MYSQL_PWD = $MySqlPassword
        }
        $process = Start-Process -FilePath $mysqlDump -ArgumentList $arguments -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -ne 0) {
            throw "mysqldump.exe exited with code $($process.ExitCode)."
        }
    }
    finally {
        if ($null -eq $previousMysqlPassword) {
            Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue
        }
        else {
            $env:MYSQL_PWD = $previousMysqlPassword
        }
    }

    if (-not (Test-Path -LiteralPath $BackupPath) -or (Get-Item -LiteralPath $BackupPath).Length -eq 0) {
        throw "Database backup was not created at '$BackupPath'."
    }
}

function Write-DeploymentEvent {
    param(
        [string]$Status,
        [string]$Message
    )

    $eventRecord = [ordered]@{
        timestampUtc = [DateTime]::UtcNow.ToString('O')
        status = $Status
        version = $manifest.version
        gitCommit = $manifest.gitCommit
        migration = $manifest.latestMigration
        packageSha256 = $actualChecksum
        message = $Message
    }
    ($eventRecord | ConvertTo-Json -Compress) | Add-Content -LiteralPath $deploymentLogPath -Encoding utf8
}

$installRootPath = Assert-SafeInstallRoot -Path $InstallRoot
$packageFullPath = [System.IO.Path]::GetFullPath($PackagePath)
if (-not (Test-Path -LiteralPath $packageFullPath)) {
    throw "Release package not found at '$packageFullPath'."
}

if ([string]::IsNullOrWhiteSpace($ChecksumPath)) {
    $ChecksumPath = "$packageFullPath.sha256"
}
$checksumFullPath = [System.IO.Path]::GetFullPath($ChecksumPath)
if (-not (Test-Path -LiteralPath $checksumFullPath)) {
    throw "Release checksum not found at '$checksumFullPath'."
}

$expectedChecksum = ((Get-Content -LiteralPath $checksumFullPath -TotalCount 1).Trim() -split '\s+')[0].ToLowerInvariant()
if ($expectedChecksum -notmatch '^[0-9a-f]{64}$') {
    throw "Checksum file '$checksumFullPath' does not start with a valid SHA-256 value."
}
$actualChecksum = (Get-FileHash -LiteralPath $packageFullPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualChecksum -ne $expectedChecksum) {
    throw "Release checksum mismatch. Expected '$expectedChecksum', received '$actualChecksum'."
}

$releasesPath = Join-Path $installRootPath 'releases'
$stagingPath = Join-Path $installRootPath 'staging'
$sharedPath = Join-Path $installRootPath 'shared'
$backupDirectory = Join-Path $sharedPath 'database-backups'
$logsDirectory = Join-Path $sharedPath 'deployment-logs'
$keysDirectory = Join-Path $sharedPath 'DataProtectionKeys'
$currentPath = Join-Path $installRootPath 'current'
$publishPath = Join-Path $installRootPath 'publish'
$lockPath = Join-Path $installRootPath 'deployment.lock'

foreach ($directory in @($installRootPath, $releasesPath, $stagingPath, $sharedPath, $backupDirectory, $logsDirectory, $keysDirectory)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

if ([string]::IsNullOrWhiteSpace($SharedConfigurationPath)) {
    $SharedConfigurationPath = Join-Path $sharedPath 'appsettings.Production.json'
}

$lockStream = $null
$extractPath = Join-Path $stagingPath ([Guid]::NewGuid().ToString('N'))
$previousReleasePath = $null
$taskWasStopped = $false
$releaseWasSwitched = $false
$legacyPublishBackupPath = $null
$publishWasTransitioned = $false
$manifest = $null
$deploymentLogPath = Join-Path $logsDirectory 'deployments.jsonl'

try {
    try {
        $lockStream = [System.IO.File]::Open($lockPath, 'OpenOrCreate', 'ReadWrite', 'None')
    }
    catch {
        throw "Another KCAS deployment appears to be running because '$lockPath' is locked."
    }

    New-Item -ItemType Directory -Path $extractPath -Force | Out-Null
    Expand-Archive -LiteralPath $packageFullPath -DestinationPath $extractPath

    $manifestPath = Join-Path $extractPath 'deployment-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw 'Release package does not contain deployment-manifest.json.'
    }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 1 -or $manifest.application -ne 'KCAS.Admin') {
        throw 'Release manifest is not a supported KCAS deployment manifest.'
    }
    if ($manifest.gitCommit -notmatch '^[0-9a-f]{40}$' -or $manifest.latestMigration -notmatch '^\d{14}_.+$') {
        throw 'Release manifest contains an invalid Git commit or migration identifier.'
    }
    if ($manifest.selfContained -ne $false -or $manifest.targetFramework -notmatch '^net(?<major>\d+)\.\d+$') {
        throw 'Release manifest must describe the supported framework-dependent KCAS package.'
    }

    $dotNetHostPath = Join-Path $installRootPath 'repo\.dotnet\dotnet.exe'
    if (-not (Test-Path -LiteralPath $dotNetHostPath -PathType Leaf)) {
        throw "The server .NET host required by this framework-dependent release was not found at '$dotNetHostPath'."
    }
    $requiredDotNetMajor = $matches.major
    $installedRuntimes = & $dotNetHostPath --list-runtimes 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Could not inspect runtimes through '$dotNetHostPath'." }
    if (-not ($installedRuntimes | Where-Object { $_ -match "^Microsoft\.NETCore\.App $requiredDotNetMajor\." }) -or
        -not ($installedRuntimes | Where-Object { $_ -match "^Microsoft\.AspNetCore\.App $requiredDotNetMajor\." })) {
        throw "The server .NET host does not contain both .NET and ASP.NET Core $requiredDotNetMajor runtimes required by '$($manifest.targetFramework)'."
    }

    $releasePath = Join-Path $releasesPath $manifest.gitCommit
    if (Test-Path -LiteralPath $releasePath) {
        throw "Release '$($manifest.gitCommit)' is already installed at '$releasePath'."
    }

    $entryPointInExtract = Join-Path $extractPath 'app\KCAS.Admin.exe'
    $legacyImporterInExtract = Join-Path $extractPath 'tools\legacy-import\KCAS.LegacyImport.dll'
    $legacySnapshotStagerInExtract = Join-Path $extractPath 'tools\legacy-import\Stage-KCAS-LegacySnapshot.ps1'
    $legacyImportRunnerInExtract = Join-Path $extractPath 'tools\legacy-import\Run-KCAS-LegacyImport.ps1'
    $databaseDeploymentInExtract = Join-Path $extractPath 'database\Apply-KCAS-Database.ps1'
    $migrationsInExtract = Join-Path $extractPath 'database\Migrations'
    foreach ($requiredReleasePath in @($entryPointInExtract, $legacyImporterInExtract, $legacySnapshotStagerInExtract, $legacyImportRunnerInExtract, $databaseDeploymentInExtract, $migrationsInExtract)) {
        if (-not (Test-Path -LiteralPath $requiredReleasePath)) {
            throw "Release package is missing '$requiredReleasePath'."
        }
    }

    $appDataDirectory = Join-Path $extractPath 'app\App_Data'
    New-Item -ItemType Directory -Path $appDataDirectory -Force | Out-Null
    $releaseKeysPath = Join-Path $appDataDirectory 'DataProtectionKeys'
    if (Test-Path -LiteralPath $releaseKeysPath) {
        throw "Release unexpectedly contains Data Protection keys at '$releaseKeysPath'."
    }
    New-Item -ItemType Junction -Path $releaseKeysPath -Target $keysDirectory | Out-Null

    if (Test-Path -LiteralPath $SharedConfigurationPath) {
        Copy-Item -LiteralPath $SharedConfigurationPath -Destination (Join-Path $extractPath 'app\appsettings.Production.json')
    }
    else {
        Write-Warning "Shared configuration '$SharedConfigurationPath' was not found. KCAS must receive production settings through environment variables."
    }

    Move-Item -LiteralPath $extractPath -Destination $releasePath
    $extractPath = $null
    $manifestPath = Join-Path $releasePath 'deployment-manifest.json'
    $null = Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction Stop

    if (Test-Path -LiteralPath $currentPath) {
        $currentItem = Get-Item -LiteralPath $currentPath -Force
        if ($currentItem.LinkType -ne 'Junction') {
            throw "Current release path '$currentPath' exists but is not a directory junction."
        }
        $previousReleasePath = [string]$currentItem.Target
    }

    if (Test-Path -LiteralPath $publishPath) {
        $publishItem = Get-Item -LiteralPath $publishPath -Force
        if ($null -ne $previousReleasePath -and $publishItem.LinkType -ne 'Junction') {
            throw "Publish path '$publishPath' must be a directory junction after the immutable deployment transition."
        }
        if ($null -ne $previousReleasePath -and
            [System.IO.Path]::GetFullPath([string]$publishItem.Target).TrimEnd('\') -ne [System.IO.Path]::GetFullPath((Join-Path $currentPath 'app')).TrimEnd('\')) {
            throw "Publish junction '$publishPath' does not target the stable current application path."
        }
    }
    elseif ($null -ne $previousReleasePath) {
        throw "Publish compatibility path '$publishPath' is missing."
    }

    $backupPath = Join-Path $backupDirectory ("{0}-before-{1}.sql" -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'), $manifest.gitCommit.Substring(0, 12))
    Write-Host "Backing up '$Database' to '$backupPath'..."
    Backup-KcasDatabase -BackupPath $backupPath

    Write-Host "Stopping Scheduled Task '$ScheduledTaskName'..."
    Stop-KcasTask -TaskName $ScheduledTaskName -HealthUrl $DirectHealthUrl
    $taskWasStopped = $true

    Write-Host "Applying reviewed database migrations through '$($manifest.latestMigration)'..."
    & (Join-Path $releasePath 'database\Apply-KCAS-Database.ps1') `
        -MySqlBasePath $MySqlBasePath `
        -Port $MySqlPort `
        -HostName $MySqlHost `
        -User $MySqlUser `
        -Password $MySqlPassword `
        -Database $Database `
        -MigrationsPath (Join-Path $releasePath 'database\Migrations') `
        -ExpectedLatestMigration $manifest.latestMigration

    Set-CurrentRelease -CurrentPath $currentPath -ReleasePath $releasePath
    $releaseWasSwitched = $true

    if ($null -eq $previousReleasePath) {
        if (-not (Test-Path -LiteralPath $publishPath -PathType Container)) {
            throw "Existing publish directory '$publishPath' was not found for the first immutable deployment."
        }
        $publishItem = Get-Item -LiteralPath $publishPath -Force
        if ($publishItem.LinkType -eq 'Junction') {
            throw "Publish path '$publishPath' is already a junction but no current release exists."
        }
        $legacyPublishBackupDirectory = Join-Path $sharedPath 'legacy-deployment-backup'
        New-Item -ItemType Directory -Path $legacyPublishBackupDirectory -Force | Out-Null
        $legacyPublishBackupPath = Join-Path $legacyPublishBackupDirectory ("publish-before-immutable-{0}" -f [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
        Move-Item -LiteralPath $publishPath -Destination $legacyPublishBackupPath
        $publishWasTransitioned = $true
        New-Item -ItemType Junction -Path $publishPath -Target (Join-Path $currentPath 'app') | Out-Null
        Write-Host "Preserved the previous publish directory at '$legacyPublishBackupPath'."
    }

    Write-Host "Starting KCAS release '$($manifest.version)'..."
    Start-ScheduledTask -TaskName $ScheduledTaskName
    $taskWasStopped = $false
    Wait-KcasHealth -Url $DirectHealthUrl -TimeoutSeconds $HealthTimeoutSeconds
    if (-not [string]::IsNullOrWhiteSpace($ProxyHealthUrl)) {
        Wait-KcasHealth -Url $ProxyHealthUrl -TimeoutSeconds $HealthTimeoutSeconds
    }

    Write-DeploymentEvent -Status 'Succeeded' -Message "Release activated. Database backup: $backupPath"
    Write-Host "KCAS deployment succeeded: $($manifest.version) ($($manifest.gitCommit))."
}
catch {
    $failure = $_.Exception.Message
    Write-Host "KCAS deployment failed: $failure" -ForegroundColor Red

    if ($releaseWasSwitched) {
        try {
            Stop-KcasTask -TaskName $ScheduledTaskName -HealthUrl $DirectHealthUrl
            if ($publishWasTransitioned) {
                Remove-KcasJunction -Path $publishPath
                if (-not [string]::IsNullOrWhiteSpace($legacyPublishBackupPath) -and (Test-Path -LiteralPath $legacyPublishBackupPath)) {
                    Move-Item -LiteralPath $legacyPublishBackupPath -Destination $publishPath
                }
            }
            if (-not [string]::IsNullOrWhiteSpace($previousReleasePath) -and (Test-Path -LiteralPath $previousReleasePath)) {
                Set-CurrentRelease -CurrentPath $currentPath -ReleasePath $previousReleasePath
            }
            elseif (Test-Path -LiteralPath $currentPath) {
                Remove-KcasJunction -Path $currentPath
            }
            Start-ScheduledTask -TaskName $ScheduledTaskName
            if (-not [string]::IsNullOrWhiteSpace($previousReleasePath)) {
                Wait-KcasHealth -Url $DirectHealthUrl -TimeoutSeconds $HealthTimeoutSeconds
            }
        }
        catch {
            $failure = "$failure Application rollback also failed: $($_.Exception.Message)"
        }
    }
    elseif ($taskWasStopped) {
        try {
            Start-ScheduledTask -TaskName $ScheduledTaskName
        }
        catch {
            $failure = "$failure The previous Scheduled Task also failed to restart: $($_.Exception.Message)"
        }
    }

    if ($null -ne $manifest) {
        Write-DeploymentEvent -Status 'Failed' -Message $failure
    }
    throw $failure
}
finally {
    if ($null -ne $lockStream) {
        $lockStream.Dispose()
    }
    if (-not [string]::IsNullOrWhiteSpace($extractPath) -and (Test-Path -LiteralPath $extractPath)) {
        Remove-Item -LiteralPath $extractPath -Recurse -Force
    }
}
