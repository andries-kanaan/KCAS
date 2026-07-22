param(
    [string]$MySqlBasePath = $env:KCAS_MYSQL_BASE_PATH,
    [int]$Port = $(if ($env:KCAS_MYSQL_PORT) { [int]$env:KCAS_MYSQL_PORT } else { 3306 }),
    [string]$HostName = $(if ($env:KCAS_MYSQL_HOST) { $env:KCAS_MYSQL_HOST } else { '127.0.0.1' }),
    [string]$User = $(if ($env:KCAS_MYSQL_USER) { $env:KCAS_MYSQL_USER } else { 'root' }),
    [string]$Password = $env:KCAS_MYSQL_PASSWORD,
    [string]$Database = $(if ($env:KCAS_DATABASE) { $env:KCAS_DATABASE } else { 'kcas_blazor' }),
    [string]$MigrationsPath,
    [string]$ExpectedLatestMigration
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($MigrationsPath)) {
    $MigrationsPath = Join-Path $root 'src\KCAS.Admin\Data\Migrations'
}

$migrationsPath = [System.IO.Path]::GetFullPath($MigrationsPath)
$script = (Join-Path $migrationsPath 'kcas_blazor_schema.sql').Replace('\', '/')
$migrationScriptsPath = Join-Path $migrationsPath 'Scripts'

if ([string]::IsNullOrWhiteSpace($MySqlBasePath)) {
    $MySqlBasePath = 'C:\wamp64\bin\mysql\mysql9.1.0'
}

$mysql = Join-Path $MySqlBasePath 'bin\mysql.exe'
$pluginDir = Join-Path $MySqlBasePath 'lib\plugin'

if (-not (Test-Path -LiteralPath $mysql)) {
    throw "MySQL client not found at '$mysql'. Pass -MySqlBasePath or set KCAS_MYSQL_BASE_PATH."
}

function Invoke-KcasMySql {
    param(
        [string[]]$Arguments
    )

    $baseArguments = @(
        "--plugin-dir=$pluginDir",
        '--protocol=tcp',
        "--host=$HostName",
        "--port=$Port",
        "--user=$User"
    )

    $previousMysqlPassword = $env:MYSQL_PWD
    if (-not [string]::IsNullOrWhiteSpace($Password)) {
        $env:MYSQL_PWD = $Password
    }

    $output = & $mysql @baseArguments @Arguments 2>&1
    if ($null -eq $previousMysqlPassword) {
        Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue
    }
    else {
        $env:MYSQL_PWD = $previousMysqlPassword
    }

    if ($LASTEXITCODE -ne 0) {
        $message = ($output | Out-String).Trim()
        if ([string]::IsNullOrWhiteSpace($message)) {
            $message = "mysql.exe exited with code $LASTEXITCODE."
        }

        throw $message
    }

    return $output
}

$latestMigration = Get-ChildItem -Path $migrationsPath -Filter '*.cs' |
    Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -match '^\d{14}_.+\.cs$' } |
    Sort-Object Name |
    Select-Object -Last 1 -ExpandProperty BaseName

if ([string]::IsNullOrWhiteSpace($latestMigration)) {
    throw "Could not determine the latest EF migration under '$migrationsPath'."
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedLatestMigration) -and $latestMigration -ne $ExpectedLatestMigration) {
    throw "Release manifest expects migration '$ExpectedLatestMigration', but the packaged migrations end at '$latestMigration'."
}

Write-Host "Applying KCAS database updates to '$Database' on ${HostName}:$Port..."
Write-Host "Latest repository migration: $latestMigration"

Invoke-KcasMySql -Arguments @('-e', "CREATE DATABASE IF NOT EXISTS $Database CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;")

$tableCount = Invoke-KcasMySql -Arguments @('--batch', '--skip-column-names', $Database, '-e', "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '$Database';")
$migrationCount = Invoke-KcasMySql -Arguments @('--batch', '--skip-column-names', $Database, '-e', "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = '$Database' AND table_name = '__EFMigrationsHistory';")
if ($migrationCount -eq '1') {
    $applied = Invoke-KcasMySql -Arguments @('--batch', '--skip-column-names', $Database, '-e', "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '$latestMigration';")
    if ($applied -eq '1') {
        Write-Host "KCAS database schema is already applied at '$latestMigration'."
        return
    }

    $currentMigration = Invoke-KcasMySql -Arguments @('--batch', '--skip-column-names', $Database, '-e', 'SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1;')
    if ([string]::IsNullOrWhiteSpace($currentMigration)) {
        throw "KCAS database has EF migration history table but no applied migrations. Back it up and review manually."
    }

    $targetedScriptName = "${currentMigration}_to_${latestMigration}.sql"
    $targetedScript = Join-Path $migrationScriptsPath $targetedScriptName
    if (-not (Test-Path -LiteralPath $targetedScript)) {
        throw "KCAS database is at '$currentMigration' but repository latest is '$latestMigration'. Missing reviewed migration script '$targetedScriptName'. Generate and review a targeted EF script; do not run the fresh schema script over an existing database."
    }

    $targetedScriptSource = $targetedScript.Replace('\', '/')
    Write-Host "Applying targeted migration script: $targetedScriptName"
    Invoke-KcasMySql -Arguments @($Database, '-e', "source $targetedScriptSource")
    Write-Host "KCAS database schema updated to '$latestMigration'."
    return
}

if ([int]$tableCount -gt 0) {
    throw "KCAS database is not empty but has no EF migration history. Back it up and review the schema manually before applying migrations."
}

Write-Host 'Applying full schema script to fresh database.'
Invoke-KcasMySql -Arguments @($Database, '-e', "source $script")
Write-Host "KCAS database schema created at '$latestMigration'."
