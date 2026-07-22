[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$GitCommit,
    [string]$InstallRoot = 'D:\Deploy\KCAS',
    [string]$ScheduledTaskName = 'KCAS',
    [string]$DirectHealthUrl = 'http://127.0.0.1:5143/health/ready',
    [int]$HealthTimeoutSeconds = 60,
    [Parameter(Mandatory)]
    [switch]$DatabaseIsCompatible
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if (-not $DatabaseIsCompatible) {
    throw 'Application rollback requires explicit confirmation that the current database schema is compatible. Pass -DatabaseIsCompatible only after checking the migration impact.'
}

$installRootPath = [System.IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
$driveRoot = [System.IO.Path]::GetPathRoot($installRootPath).TrimEnd('\')
if ($installRootPath -eq $driveRoot -or $installRootPath.Length -lt ($driveRoot.Length + 8)) {
    throw "InstallRoot '$installRootPath' is too broad."
}

$normalizedCommit = $GitCommit.ToLowerInvariant()
$releasePath = Join-Path (Join-Path $installRootPath 'releases') $normalizedCommit
$manifestPath = Join-Path $releasePath 'deployment-manifest.json'
$entryPoint = Join-Path $releasePath 'app\KCAS.Admin.exe'
foreach ($requiredPath in @($releasePath, $manifestPath, $entryPoint)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Rollback release is incomplete or absent: '$requiredPath'."
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.gitCommit -ne $normalizedCommit) {
    throw "Rollback manifest commit '$($manifest.gitCommit)' does not match requested commit '$GitCommit'."
}

$currentPath = Join-Path $installRootPath 'current'
$temporaryJunction = "$currentPath.rollback"

function Remove-VerifiedJunction {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.LinkType -ne 'Junction') {
        throw "Refusing to remove '$Path' because it is not a directory junction."
    }
    [System.IO.Directory]::Delete($item.FullName)
}

function Wait-Healthy {
    $deadline = [DateTime]::UtcNow.AddSeconds($HealthTimeoutSeconds)
    $lastError = $null
    do {
        try {
            $response = Invoke-WebRequest -Uri $DirectHealthUrl -UseBasicParsing -TimeoutSec 10
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
    throw "Rolled-back KCAS release did not become healthy. Last result: $lastError"
}

function Stop-KcasWorkload {
    $task = Get-ScheduledTask -TaskName $ScheduledTaskName -ErrorAction Stop
    if ($task.State -eq 'Running') {
        Stop-ScheduledTask -TaskName $ScheduledTaskName
    }

    $healthUri = [System.Uri]$DirectHealthUrl
    $normalizedApplicationRoot = $installRootPath
    $escapedApplicationRoot = [System.Text.RegularExpressions.Regex]::Escape($normalizedApplicationRoot)
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $healthUri.Port -ErrorAction SilentlyContinue)
        $listenerProcessIds = @($listeners | Select-Object -ExpandProperty OwningProcess -Unique)
        $kcasProcesses = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
            $commandLine = [string]$_.CommandLine
            $executablePath = [string]$_.ExecutablePath
            ($_.Name -eq 'KCAS.Admin.exe' -or $commandLine -match 'KCAS\.Admin(?:\.dll|\.exe)') -and
                ($listenerProcessIds -contains $_.ProcessId -or
                    $executablePath.StartsWith($normalizedApplicationRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
                    $commandLine -match $escapedApplicationRoot)
        })
        foreach ($process in $kcasProcesses) {
            Stop-Process -Id ([int]$process.ProcessId) -Force -ErrorAction SilentlyContinue
        }
        foreach ($processId in $listenerProcessIds) {
            if ($kcasProcesses.ProcessId -contains $processId) { continue }
            $process = Get-CimInstance Win32_Process -Filter "ProcessId = $processId" -ErrorAction SilentlyContinue
            if ($null -eq $process) { continue }
            throw "Port $($healthUri.Port) is owned by unexpected process '$($process.Name)' (PID $processId)."
        }
        Start-Sleep -Milliseconds 500
    } while (($listeners.Count -gt 0 -or $kcasProcesses.Count -gt 0) -and [DateTime]::UtcNow -lt $deadline)

    $remainingListeners = @(Get-NetTCPConnection -State Listen -LocalPort $healthUri.Port -ErrorAction SilentlyContinue)
    $remainingKcasProcesses = @(Get-CimInstance Win32_Process -ErrorAction Stop | Where-Object {
        $commandLine = [string]$_.CommandLine
        $executablePath = [string]$_.ExecutablePath
        ($_.Name -eq 'KCAS.Admin.exe' -or $commandLine -match 'KCAS\.Admin(?:\.dll|\.exe)') -and
            ($executablePath.StartsWith($normalizedApplicationRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
                $commandLine -match $escapedApplicationRoot)
    })
    if ($remainingListeners.Count -gt 0 -or $remainingKcasProcesses.Count -gt 0) {
        throw 'KCAS did not fully exit within 30 seconds after its Scheduled Task was stopped.'
    }
}

Stop-KcasWorkload

Remove-VerifiedJunction -Path $temporaryJunction
New-Item -ItemType Junction -Path $temporaryJunction -Target $releasePath | Out-Null
Remove-VerifiedJunction -Path $currentPath
[System.IO.Directory]::Move($temporaryJunction, $currentPath)

Start-ScheduledTask -TaskName $ScheduledTaskName
Wait-Healthy

$logDirectory = Join-Path $installRootPath 'shared\deployment-logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$eventRecord = [ordered]@{
    timestampUtc = [DateTime]::UtcNow.ToString('O')
    status = 'ManualRollback'
    version = $manifest.version
    gitCommit = $manifest.gitCommit
    migration = $manifest.latestMigration
    message = 'Application release rolled back after database compatibility was confirmed manually.'
}
($eventRecord | ConvertTo-Json -Compress) | Add-Content -LiteralPath (Join-Path $logDirectory 'deployments.jsonl') -Encoding utf8

Write-Host "KCAS application rollback succeeded: $($manifest.version) ($($manifest.gitCommit))."
Write-Warning 'No database migration was reversed.'
