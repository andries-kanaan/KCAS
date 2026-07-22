[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SqlExportPath,
    [string]$SharedRoot = 'D:\Deploy\KCAS\shared',
    [string]$MySqlBasePath = $(if ($env:KCAS_MYSQL_BASE_PATH) { $env:KCAS_MYSQL_BASE_PATH } else { 'D:\wamp64\bin\mysql\mysql9.1.0' }),
    [string]$MySqlHost = $(if ($env:KCAS_LEGACY_STAGE_HOST) { $env:KCAS_LEGACY_STAGE_HOST } else { '127.0.0.1' }),
    [int]$MySqlPort = $(if ($env:KCAS_LEGACY_STAGE_PORT) { [int]$env:KCAS_LEGACY_STAGE_PORT } else { 3306 }),
    [string]$MySqlUser = $(if ($env:KCAS_LEGACY_STAGE_USER) { $env:KCAS_LEGACY_STAGE_USER } else { 'root' }),
    [string]$MySqlPassword = $env:KCAS_LEGACY_STAGE_PASSWORD
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

$exportPath = [System.IO.Path]::GetFullPath($SqlExportPath)
if (-not (Test-Path -LiteralPath $exportPath -PathType Leaf)) {
    throw "SQL export not found: $exportPath"
}
if ([System.IO.Path]::GetExtension($exportPath) -ne '.sql') {
    throw 'Legacy snapshot must be a .sql export.'
}
if (Select-String -LiteralPath $exportPath -Pattern '^\s*(CREATE\s+DATABASE|DROP\s+DATABASE|USE\s+`?)' -Quiet) {
    throw 'SQL export contains database-selection statements. Export kanaanclients without CREATE DATABASE, DROP DATABASE, or USE so it cannot escape the checksum-named staging database.'
}

$mysql = Join-Path $MySqlBasePath 'bin\mysql.exe'
if (-not (Test-Path -LiteralPath $mysql -PathType Leaf)) {
    throw "mysql.exe not found at '$mysql'."
}

$sha256 = (Get-FileHash -LiteralPath $exportPath -Algorithm SHA256).Hash.ToLowerInvariant()
$database = "kcas_legacy_stage_$($sha256.Substring(0, 12))"
$snapshotDirectory = Join-Path ([System.IO.Path]::GetFullPath($SharedRoot)) "legacy-snapshots\$sha256"
$manifestPath = Join-Path $snapshotDirectory 'snapshot.json'
New-Item -ItemType Directory -Path $snapshotDirectory -Force | Out-Null

$previousPassword = $env:MYSQL_PWD
try {
    if (-not [string]::IsNullOrWhiteSpace($MySqlPassword)) { $env:MYSQL_PWD = $MySqlPassword }
    $baseArguments = @('--batch', '--raw', '--skip-column-names', "--plugin-dir=$(Join-Path $MySqlBasePath 'lib\plugin')", '--protocol=tcp', "--host=$MySqlHost", "--port=$MySqlPort", "--user=$MySqlUser")
    $databaseExists = & $mysql @baseArguments --execute "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name='$database';"
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect MySQL staging databases.' }

    if ([int]$databaseExists -eq 0) {
        & $mysql @baseArguments --execute "CREATE DATABASE ``$database`` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;"
        if ($LASTEXITCODE -ne 0) { throw "Could not create staging database '$database'." }
        $process = Start-Process -FilePath $mysql -ArgumentList (@($baseArguments) + @("--database=$database")) -RedirectStandardInput $exportPath -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -ne 0) { throw "Restoring the legacy SQL export failed with exit code $($process.ExitCode)." }
    }
    elseif (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Staging database '$database' already exists without its matching snapshot manifest. Review it manually."
    }

    $expectedTables = @('tbl_client','tbl_clientnote','tbl_kyc','tbl_investmentaccount','tbl_investmenthistory','tbl_fund','tbl_companyproduct','tbl_lispname','tbl_mainclass','tbl_subclass','tbl_fundname','tbl_miscinfo')
    $tableList = & $mysql @baseArguments "--database=$database" --execute 'SHOW TABLES;'
    if ($LASTEXITCODE -ne 0) { throw "Could not validate staging database '$database'." }
    $missing = $expectedTables | Where-Object { $_ -notin $tableList }
    if ($missing) { throw "Staged export is missing required table(s): $($missing -join ', ')." }
}
finally {
    if ($null -eq $previousPassword) { Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue } else { $env:MYSQL_PWD = $previousPassword }
}

$manifest = [ordered]@{
    schemaVersion = 1
    sha256 = $sha256
    sourceFileName = [System.IO.Path]::GetFileName($exportPath)
    sourceFileLength = (Get-Item -LiteralPath $exportPath).Length
    stagedDatabase = $database
    mysqlHost = $MySqlHost
    mysqlPort = $MySqlPort
    stagedAtUtc = [DateTime]::UtcNow.ToString('O')
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM
Write-Host "Staged legacy snapshot '$($manifest.sourceFileName)' as '$database'."
Write-Host "SHA-256: $sha256"
Write-Output $manifestPath
