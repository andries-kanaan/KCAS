[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SnapshotManifestPath,
    [Parameter(Mandatory)]
    [ValidateSet('Scan','ApplyNew')]
    [string]$Mode,
    [long]$ApprovedScanRunId,
    [string]$InstallRoot = 'D:\Deploy\KCAS',
    [string]$ImporterPath = (Join-Path $InstallRoot 'current\tools\legacy-import\KCAS.LegacyImport.dll'),
    [string]$DotNetPath = (Join-Path $InstallRoot 'repo\.dotnet\dotnet.exe'),
    [string]$MySqlBasePath = $(if ($env:KCAS_MYSQL_BASE_PATH) { $env:KCAS_MYSQL_BASE_PATH } else { 'D:\wamp64\bin\mysql\mysql9.1.0' }),
    [string]$TargetHost = $(if ($env:KCAS_MYSQL_HOST) { $env:KCAS_MYSQL_HOST } else { '127.0.0.1' }),
    [int]$TargetPort = $(if ($env:KCAS_MYSQL_PORT) { [int]$env:KCAS_MYSQL_PORT } else { 3306 }),
    [string]$TargetDatabase = $(if ($env:KCAS_DATABASE) { $env:KCAS_DATABASE } else { 'kcas_blazor' }),
    [string]$TargetUser = $(if ($env:KCAS_MYSQL_USER) { $env:KCAS_MYSQL_USER } else { 'root' }),
    [string]$TargetPassword = $env:KCAS_MYSQL_PASSWORD,
    [string]$SourceUser = $(if ($env:KCAS_LEGACY_STAGE_USER) { $env:KCAS_LEGACY_STAGE_USER } else { 'root' }),
    [string]$SourcePassword = $env:KCAS_LEGACY_STAGE_PASSWORD
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

if ($Mode -eq 'ApplyNew' -and $ApprovedScanRunId -le 0) { throw 'ApplyNew requires -ApprovedScanRunId.' }
if ($Mode -eq 'Scan' -and $ApprovedScanRunId -gt 0) { throw 'ApprovedScanRunId is valid only for ApplyNew.' }
if ($TargetDatabase -notmatch '^[A-Za-z0-9_]+$') { throw "Unsupported target database name '$TargetDatabase'." }

$manifestPath = [System.IO.Path]::GetFullPath($SnapshotManifestPath)
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw "Snapshot manifest not found: $manifestPath" }
$snapshot = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($snapshot.schemaVersion -ne 1 -or $snapshot.sha256 -notmatch '^[0-9a-f]{64}$' -or $snapshot.stagedDatabase -notmatch '^kcas_legacy_stage_[0-9a-f]{12}$') {
    throw 'Snapshot manifest is invalid or unsupported.'
}
if (-not (Test-Path -LiteralPath $ImporterPath -PathType Leaf)) { throw "Immutable importer not found: $ImporterPath" }

$sharedRoot = Join-Path ([System.IO.Path]::GetFullPath($InstallRoot)) 'shared'
$logDirectory = Join-Path $sharedRoot 'legacy-import-logs'
$backupDirectory = Join-Path $sharedRoot 'database-backups'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null

$timestamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
if ($Mode -eq 'ApplyNew') {
    $dump = Join-Path $MySqlBasePath 'bin\mysqldump.exe'
    if (-not (Test-Path -LiteralPath $dump -PathType Leaf)) { throw "mysqldump.exe not found at '$dump'." }
    $backupPath = Join-Path $backupDirectory "$timestamp-before-legacy-apply.sql"
    $previousPassword = $env:MYSQL_PWD
    try {
        if (-not [string]::IsNullOrWhiteSpace($TargetPassword)) { $env:MYSQL_PWD = $TargetPassword }
        $dumpArgs = @("--plugin-dir=$(Join-Path $MySqlBasePath 'lib\plugin')", '--protocol=tcp', "--host=$TargetHost", "--port=$TargetPort", "--user=$TargetUser", '--single-transaction', '--routines', '--triggers', '--events', '--no-tablespaces', '--default-character-set=utf8mb4', "--result-file=$backupPath", $TargetDatabase)
        $backupProcess = Start-Process -FilePath $dump -ArgumentList $dumpArgs -Wait -PassThru -NoNewWindow
        if ($backupProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $backupPath) -or (Get-Item $backupPath).Length -eq 0) { throw 'Pre-import database backup failed.' }
    }
    finally {
        if ($null -eq $previousPassword) { Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue } else { $env:MYSQL_PWD = $previousPassword }
    }
}

$sourceSsl = if ($snapshot.mysqlHost -in @('localhost','127.0.0.1','::1')) { ';SslMode=Disabled;AllowPublicKeyRetrieval=True' } else { '' }
$targetSsl = if ($TargetHost -in @('localhost','127.0.0.1','::1')) { ';SslMode=Disabled;AllowPublicKeyRetrieval=True' } else { '' }
$sourceConnection = "server=$($snapshot.mysqlHost);port=$($snapshot.mysqlPort);database=$($snapshot.stagedDatabase);user=$SourceUser;password=$SourcePassword;TreatTinyAsBoolean=false$sourceSsl"
$targetConnection = "server=$TargetHost;port=$TargetPort;database=$TargetDatabase;user=$TargetUser;password=$TargetPassword;TreatTinyAsBoolean=true$targetSsl"
$arguments = [System.Collections.Generic.List[string]]::new()
$arguments.Add($(if ($Mode -eq 'Scan') { '--scan' } else { '--apply-new' }))
$arguments.Add('--source-snapshot-sha256'); $arguments.Add([string]$snapshot.sha256)
$arguments.Add('--source-snapshot-file-name'); $arguments.Add([string]$snapshot.sourceFileName)
if ($Mode -eq 'ApplyNew') { $arguments.Add('--approved-scan-run'); $arguments.Add([string]$ApprovedScanRunId) }

$stdoutPath = Join-Path $logDirectory "$timestamp-$($Mode.ToLowerInvariant()).out.log"
$stderrPath = Join-Path $logDirectory "$timestamp-$($Mode.ToLowerInvariant()).err.log"
$processInfo = [System.Diagnostics.ProcessStartInfo]::new()
$resolvedImporterPath = [System.IO.Path]::GetFullPath($ImporterPath)
$isManagedDll = [System.IO.Path]::GetExtension($resolvedImporterPath) -eq '.dll'
$processInfo.FileName = if ($isManagedDll) { $DotNetPath } else { $resolvedImporterPath }
$processInfo.WorkingDirectory = [System.IO.Path]::GetDirectoryName($resolvedImporterPath)
$processInfo.UseShellExecute = $false
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.Environment['KCAS_LEGACY_CONNECTION'] = $sourceConnection
$processInfo.Environment['KCAS_TARGET_CONNECTION'] = $targetConnection
if ($isManagedDll) { $processInfo.ArgumentList.Add($resolvedImporterPath) }
foreach ($argument in $arguments) { $processInfo.ArgumentList.Add($argument) }
$process = [System.Diagnostics.Process]::Start($processInfo)
$stdout = $process.StandardOutput.ReadToEnd()
$stderr = $process.StandardError.ReadToEnd()
$process.WaitForExit()
$stdout | Set-Content -LiteralPath $stdoutPath -Encoding utf8NoBOM
$stderr | Set-Content -LiteralPath $stderrPath -Encoding utf8NoBOM
if ($stdout) { Write-Host $stdout.TrimEnd() }
if ($stderr) { Write-Error $stderr.TrimEnd() -ErrorAction Continue }

$runId = if ($stdout -match 'run\s+(\d+)') { [long]$matches[1] } else { $null }
$event = [ordered]@{ timestampUtc=[DateTime]::UtcNow.ToString('O'); mode=$Mode; snapshotSha256=$snapshot.sha256; sourceDatabase=$snapshot.stagedDatabase; targetDatabase=$TargetDatabase; approvedScanRunId=$(if($ApprovedScanRunId -gt 0){$ApprovedScanRunId}else{$null}); resultingRunId=$runId; exitCode=$process.ExitCode }
($event | ConvertTo-Json -Compress) | Add-Content -LiteralPath (Join-Path $logDirectory 'imports.jsonl') -Encoding utf8
if ($process.ExitCode -ne 0) { throw "Legacy importer failed with exit code $($process.ExitCode). See '$stderrPath'." }
Write-Output $runId
