$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$mysql = 'C:\wamp64\bin\mysql\mysql9.1.0\bin\mysql.exe'
$pluginDir = 'C:\wamp64\bin\mysql\mysql9.1.0\lib\plugin'
$script = (Join-Path $root 'src\KCAS.Admin\Data\Migrations\kcas_blazor_initial.sql').Replace('\', '/')

& $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root -e 'CREATE DATABASE IF NOT EXISTS kcas_blazor CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;'

$migrationCount = & $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root --batch --skip-column-names kcas_blazor -e "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'kcas_blazor' AND table_name = '__efmigrationshistory';"
if ($migrationCount -eq '1') {
    $applied = & $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root --batch --skip-column-names kcas_blazor -e "SELECT COUNT(*) FROM __efmigrationshistory WHERE MigrationId = '20260529223052_InitialKcasSchema';"
    if ($applied -eq '1') {
        Write-Host 'KCAS database schema is already applied.'
        exit 0
    }
}

& $mysql --plugin-dir=$pluginDir --protocol=tcp --host=127.0.0.1 --port=3307 --user=root kcas_blazor -e "source $script"
