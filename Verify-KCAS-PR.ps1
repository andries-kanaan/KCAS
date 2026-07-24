param(
    [string]$Configuration = 'Release',
    [switch]$SkipRestore,
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)
    Write-Host ""
    Write-Host "==> $Message"
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Set-LocalTestConnectionString {
    if ($env:KCAS_TEST_CONNECTION_STRING) {
        return
    }

    $settingsPath = Join-Path $repoRoot 'src\KCAS.Admin\appsettings.Development.json'
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        return
    }

    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $connectionString = [string]$settings.ConnectionStrings.DefaultConnection
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        return
    }

    $parts = [ordered]@{}
    foreach ($part in $connectionString.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)) {
        $keyValue = $part.Split('=', 2)
        if ($keyValue.Count -eq 2) {
            $parts[$keyValue[0].Trim()] = $keyValue[1].Trim()
        }
    }

    $parts['database'] = 'kcas_blazor_test'
    $parts['SslMode'] = 'Disabled'
    $env:KCAS_TEST_CONNECTION_STRING = ($parts.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ';'
}

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoRoot

$dotnet = Join-Path $repoRoot '.dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet -PathType Leaf)) {
    $dotnet = 'dotnet'
}

Set-LocalTestConnectionString

if (-not $SkipRestore) {
    Write-Step "Restoring KCAS.slnx"
    Invoke-Checked $dotnet @('restore', 'KCAS.slnx')
}

if (-not $SkipBuild) {
    Write-Step "Building KCAS.slnx ($Configuration)"
    $buildArgs = @('build', 'KCAS.slnx', '--configuration', $Configuration, '--no-restore')
    Invoke-Checked $dotnet $buildArgs
}

if (-not $SkipTests) {
    Write-Step "Running tests"
    $testArgs = @('test', 'KCAS.slnx', '--configuration', $Configuration, '--no-build', '--verbosity', 'normal')
    Invoke-Checked $dotnet $testArgs
}

Write-Step "Validating Windows deployment scripts"
$scripts = @(
    Get-Item (Join-Path $repoRoot 'Apply-KCAS-Database.ps1')
    Get-ChildItem (Join-Path $repoRoot 'deploy\windows') -Filter '*.ps1'
)
$failed = $false
foreach ($script in $scripts) {
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$null, [ref]$errors) | Out-Null
    foreach ($parseError in $errors) {
        Write-Error "$($script.Name): $($parseError.Message)" -ErrorAction Continue
        $failed = $true
    }
}
if ($failed) {
    throw 'One or more Windows deployment scripts have syntax errors.'
}

@(
    'deploy\windows\Deploy-KCAS.bat',
    'deploy\windows\Deploy-KCAS.ps1',
    'deploy\windows\Deploy-KCAS-Release.ps1',
    'deploy\windows\Install-KCAS-Deployment.bat',
    'deploy\windows\Install-KCAS-Deployment.ps1',
    'deploy\windows\Rollback-KCAS-Release.ps1'
) | ForEach-Object {
    $path = Join-Path $repoRoot $_
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required deployment tool is missing: $_"
    }
}

Write-Step "Validating deployment migration SQL"
$migrationsPath = Join-Path $repoRoot 'src\KCAS.Admin\Data\Migrations'
$schemaPath = Join-Path $migrationsPath 'kcas_blazor_schema.sql'
$scriptsPath = Join-Path $migrationsPath 'Scripts'
$migrations = @(Get-ChildItem -LiteralPath $migrationsPath -Filter '*.cs' |
    Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -match '^\d{14}_.+\.cs$' } |
    Sort-Object Name |
    Select-Object -ExpandProperty BaseName)

if ($migrations.Count -eq 0) {
    throw "No EF migrations found under '$migrationsPath'."
}
if (-not (Test-Path -LiteralPath $schemaPath -PathType Leaf)) {
    throw "Fresh database schema script is missing: $schemaPath"
}

$latestMigration = $migrations[-1]
if (-not (Select-String -LiteralPath $schemaPath -SimpleMatch "VALUES ('$latestMigration'" -Quiet)) {
    throw "Fresh database schema '$schemaPath' does not record latest migration '$latestMigration'. Regenerate and review it before merging."
}

if ($migrations.Count -gt 1) {
    $previousMigration = $migrations[-2]
    $directScriptName = "${previousMigration}_to_${latestMigration}.sql"
    $directScriptPath = Join-Path $scriptsPath $directScriptName
    if (-not (Test-Path -LiteralPath $directScriptPath -PathType Leaf)) {
        throw "Reviewed targeted migration script is missing: $directScriptPath"
    }
    if (-not (Select-String -LiteralPath $directScriptPath -SimpleMatch "VALUES ('$latestMigration'" -Quiet)) {
        throw "Targeted migration script '$directScriptPath' does not record latest migration '$latestMigration'."
    }
}

Write-Host ""
Write-Host "KCAS PR verification passed."
