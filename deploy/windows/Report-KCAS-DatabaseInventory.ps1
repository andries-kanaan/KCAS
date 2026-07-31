#requires -Version 7.0

[CmdletBinding()]
param(
    [string]$AppSettingsPath,
    [string]$MySqlBasePath = $env:KCAS_MYSQL_BASE_PATH,
    [string]$MySqlHost = $env:KCAS_MYSQL_HOST,
    [int]$MySqlPort = $(if ($env:KCAS_MYSQL_PORT) { [int]$env:KCAS_MYSQL_PORT } else { 0 }),
    [string]$MySqlUser = $env:KCAS_MYSQL_USER,
    [string]$MySqlPassword = $env:KCAS_MYSQL_PASSWORD,
    [string]$KcasDatabase = $(if ($env:KCAS_DATABASE) { $env:KCAS_DATABASE } else { 'kcas_blazor' }),
    [int]$RecoveryReviewDays = 14,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

function Get-ConnectionValue {
    param(
        [System.Data.Common.DbConnectionStringBuilder]$Builder,
        [string[]]$Keys,
        [object]$DefaultValue
    )

    foreach ($key in $Keys) {
        if ($Builder.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace([string]$Builder[$key])) {
            return $Builder[$key]
        }
    }
    return $DefaultValue
}

function Invoke-InventoryQuery {
    param([string]$Sql)

    $output = & $script:MySqlExecutable @script:MySqlArguments --batch --raw --skip-column-names --execute $Sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read the MySQL database inventory: $(($output | Out-String).Trim())"
    }
    return @($output)
}

if ($RecoveryReviewDays -lt 1) {
    throw 'RecoveryReviewDays must be at least 1.'
}
if ($KcasDatabase -notmatch '^[A-Za-z0-9_]+$') {
    throw "Unsupported KCAS database name '$KcasDatabase'."
}

if ([string]::IsNullOrWhiteSpace($AppSettingsPath)) {
    $repositorySettings = Join-Path $PSScriptRoot '..\..\src\KCAS.Admin\appsettings.Development.json'
    if (Test-Path -LiteralPath $repositorySettings -PathType Leaf) {
        $AppSettingsPath = $repositorySettings
    }
}

if (-not [string]::IsNullOrWhiteSpace($AppSettingsPath)) {
    $settingsFullPath = [System.IO.Path]::GetFullPath($AppSettingsPath)
    if (-not (Test-Path -LiteralPath $settingsFullPath -PathType Leaf)) {
        throw "App settings file not found: $settingsFullPath"
    }

    $settings = Get-Content -LiteralPath $settingsFullPath -Raw | ConvertFrom-Json
    $connectionString = [string]$settings.ConnectionStrings.DefaultConnection
    if (-not [string]::IsNullOrWhiteSpace($connectionString)) {
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $builder.set_ConnectionString($connectionString)
        if ([string]::IsNullOrWhiteSpace($MySqlHost)) {
            $MySqlHost = [string](Get-ConnectionValue $builder @('server', 'host', 'data source') '127.0.0.1')
        }
        if ($MySqlPort -eq 0) {
            $MySqlPort = [int](Get-ConnectionValue $builder @('port') 3306)
        }
        if ([string]::IsNullOrWhiteSpace($MySqlUser)) {
            $MySqlUser = [string](Get-ConnectionValue $builder @('user', 'user id', 'uid') 'root')
        }
        if ([string]::IsNullOrWhiteSpace($MySqlPassword)) {
            $MySqlPassword = [string](Get-ConnectionValue $builder @('password', 'pwd') '')
        }
        if (-not $PSBoundParameters.ContainsKey('KcasDatabase')) {
            $KcasDatabase = [string](Get-ConnectionValue $builder @('database', 'initial catalog') $KcasDatabase)
        }
    }
}

if ([string]::IsNullOrWhiteSpace($MySqlHost)) { $MySqlHost = '127.0.0.1' }
if ($MySqlPort -eq 0) { $MySqlPort = 3306 }
if ([string]::IsNullOrWhiteSpace($MySqlUser)) { $MySqlUser = 'root' }

if ([string]::IsNullOrWhiteSpace($MySqlBasePath)) {
    $baseCandidates = @(
        'C:\wamp64\bin\mysql\mysql9.1.0',
        'D:\wamp64\bin\mysql\mysql9.1.0'
    )
    $MySqlBasePath = $baseCandidates |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'bin\mysql.exe') -PathType Leaf } |
        Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($MySqlBasePath)) {
    throw 'Could not locate MySQL. Supply -MySqlBasePath or KCAS_MYSQL_BASE_PATH.'
}

$script:MySqlExecutable = Join-Path ([System.IO.Path]::GetFullPath($MySqlBasePath)) 'bin\mysql.exe'
if (-not (Test-Path -LiteralPath $script:MySqlExecutable -PathType Leaf)) {
    throw "mysql.exe not found at '$script:MySqlExecutable'."
}
$script:MySqlArguments = @(
    '--protocol=tcp',
    "--host=$MySqlHost",
    "--port=$MySqlPort",
    "--user=$MySqlUser"
)

$previousPassword = $env:MYSQL_PWD
try {
    if (-not [string]::IsNullOrWhiteSpace($MySqlPassword)) {
        $env:MYSQL_PWD = $MySqlPassword
    }

    $activeStageQuery = @"
        SELECT SourceLabel
        FROM ``$KcasDatabase``.LegacyImportRuns
        WHERE Mode = 'Scan'
          AND Status IN ('Completed', 'AwaitingReview')
          AND SourceLabel REGEXP '^kcas_legacy_stage_[0-9a-fA-F]{12}$'
        ORDER BY Id DESC
        LIMIT 1;
        
"@
    $activeStage = (Invoke-InventoryQuery $activeStageQuery | Select-Object -First 1)

    $inventoryQuery = @"
        SELECT s.schema_name,
               COUNT(t.table_name),
               COALESCE(SUM(t.data_length + t.index_length), 0),
               COALESCE(DATE_FORMAT(MIN(t.create_time), '%Y-%m-%d %H:%i:%s'), ''),
               COALESCE(DATE_FORMAT(MAX(t.update_time), '%Y-%m-%d %H:%i:%s'), '')
        FROM information_schema.schemata s
        LEFT JOIN information_schema.tables t ON t.table_schema = s.schema_name
        WHERE s.schema_name LIKE 'kcas%'
        GROUP BY s.schema_name
        ORDER BY s.schema_name;
        
"@
    $rows = Invoke-InventoryQuery $inventoryQuery
}
finally {
    if ($null -eq $previousPassword) {
        Remove-Item Env:\MYSQL_PWD -ErrorAction SilentlyContinue
    }
    else {
        $env:MYSQL_PWD = $previousPassword
    }
}

$now = Get-Date
$inventory = foreach ($line in $rows) {
    $fields = $line -split "`t", 5
    if ($fields.Count -ne 5) {
        throw "Unexpected inventory row returned by MySQL: $line"
    }

    $name = $fields[0]
    $created = if ($fields[3]) { [DateTime]::Parse($fields[3], [Globalization.CultureInfo]::InvariantCulture) } else { $null }
    $ageDays = if ($created) { [Math]::Max(0, [Math]::Floor(($now - $created).TotalDays)) } else { $null }
    $category = 'Other KCAS'
    $disposition = 'Review'
    $reason = 'Unclassified KCAS database.'

    if ($name -eq $KcasDatabase) {
        $category = 'Live'
        $disposition = 'Retain'
        $reason = 'Configured live KCAS database.'
    }
    elseif ($name -eq $activeStage) {
        $category = 'Legacy staging'
        $disposition = 'Retain'
        $reason = 'Active checksum-bound legacy snapshot.'
    }
    elseif ($name -match '^kcas_legacy_stage_[0-9a-fA-F]{12}$') {
        $category = 'Legacy staging'
        $disposition = 'Review'
        $reason = 'Inactive checksum staging database.'
    }
    elseif ($name -match '^kcas_deploy_') {
        $category = 'Deployment test'
        $disposition = 'Review'
        $reason = 'Temporary deployment or migration verification database.'
    }
    elseif ($name -match '^kcas_import_rehearsal') {
        $category = 'Import rehearsal'
        $disposition = 'Review'
        $reason = 'Temporary import rehearsal database.'
    }
    elseif ($name -match '^kcas_recovery_') {
        $category = 'Recovery'
        $disposition = if ($null -ne $ageDays -and $ageDays -ge $RecoveryReviewDays) { 'Review' } else { 'Monitor' }
        $reason = "Recovery snapshot; review after $RecoveryReviewDays days and after verifying a newer backup."
    }
    elseif ($name -match '(_test|_tests)$') {
        $category = 'Automated test'
        $disposition = 'Monitor'
        $reason = 'Expected test database; investigate if multiple copies accumulate.'
    }

    [pscustomobject]@{
        Database = $name
        Category = $category
        Disposition = $disposition
        Tables = [int]$fields[1]
        SizeMB = [Math]::Round(([double]$fields[2] / 1MB), 2)
        AgeDays = $ageDays
        LatestUpdate = if ($fields[4]) { $fields[4] } else { '—' }
        Reason = $reason
    }
}

$orderedInventory = @($inventory | Sort-Object Database)
$reviewItems = @($orderedInventory | Where-Object { $_.Disposition -eq 'Review' })
$totalSize = if ($orderedInventory.Count -gt 0) {
    [Math]::Round((($orderedInventory | Measure-Object -Property SizeMB -Sum).Sum), 2)
}
else { 0 }
$reviewSize = if ($reviewItems.Count -gt 0) {
    [Math]::Round((($reviewItems | Measure-Object -Property SizeMB -Sum).Sum), 2)
}
else { 0 }

Write-Host ''
Write-Host 'KCAS database inventory (read-only)'
Write-Host "Generated: $($now.ToString('yyyy-MM-dd HH:mm:ss zzz'))"
Write-Host "Active legacy staging database: $(if ($activeStage) { $activeStage } else { 'None recorded' })"
Write-Host ''
$orderedInventory |
    Select-Object Database, Category, Disposition, Tables, SizeMB, AgeDays, LatestUpdate |
    Format-Table -AutoSize |
    Out-String |
    Write-Host
Write-Host "Total: $($orderedInventory.Count) database(s), $totalSize MB."
Write-Host "Review recommended: $($reviewItems.Count) database(s), $reviewSize MB."
Write-Host 'No databases were modified or deleted.'

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $outputFullPath
    if ($outputDirectory -and -not (Test-Path -LiteralPath $outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }
    [ordered]@{
        generatedAt = $now.ToString('O')
        activeLegacyStage = $activeStage
        databaseCount = $orderedInventory.Count
        totalSizeMB = $totalSize
        reviewCount = $reviewItems.Count
        reviewSizeMB = $reviewSize
        databases = $orderedInventory
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outputFullPath -Encoding utf8NoBOM
    Write-Host "JSON report: $outputFullPath"
}

$orderedInventory
