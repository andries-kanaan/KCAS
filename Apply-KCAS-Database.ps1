$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$mysql = 'C:\wamp64\bin\mysql\mysql9.1.0\bin\mysql.exe'
$pluginDir = 'C:\wamp64\bin\mysql\mysql9.1.0\lib\plugin'
$script = (Join-Path $root 'src\KCAS.Admin\Data\Migrations\kcas_blazor_schema.sql').Replace('\', '/')
$latestMigration = '20260602211709_CompleteOutstandingWorkflows'

& $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root -e 'CREATE DATABASE IF NOT EXISTS kcas_blazor CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;'

$tableCount = & $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root --batch --skip-column-names kcas_blazor -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'kcas_blazor';"
$migrationCount = & $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root --batch --skip-column-names kcas_blazor -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'kcas_blazor' AND table_name = '__EFMigrationsHistory';"
if ($migrationCount -eq '1') {
    $applied = & $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root --batch --skip-column-names kcas_blazor -e "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId = '$latestMigration';"
    if ($applied -eq '1') {
        Write-Host 'KCAS database schema is already applied.'
        exit 0
    }

    throw "KCAS database has migration history but is missing latest migration '$latestMigration'. Generate and review a targeted migration script from the applied migration to the latest migration; do not run the fresh schema script over an existing database."
}

if ([int]$tableCount -gt 0) {
    throw "KCAS database is not empty but has no EF migration history. Back it up and review the schema manually before applying migrations."
}

& $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root kcas_blazor -e "source $script"
