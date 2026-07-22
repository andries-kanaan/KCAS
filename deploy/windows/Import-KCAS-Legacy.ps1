#requires -Version 7.0

[CmdletBinding(DefaultParameterSetName = 'Scan')]
param(
    [Parameter(Position = 0, ParameterSetName = 'Scan')]
    [string]$SqlExportPath,
    [Parameter(Mandatory, ParameterSetName = 'Apply')]
    [long]$ApplyNew,
    [string]$InstallRoot,
    [string]$ToolsDirectory,
    [string]$ImporterPath,
    [string]$DotNetPath,
    [string]$ConfigurationPath,
    [string]$MySqlBasePath,
    [string]$TargetHost,
    [int]$TargetPort,
    [string]$TargetDatabase,
    [string]$TargetUser,
    [string]$SourceUser,
    [string]$ReviewUrl,
    [switch]$NoOpenReview
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3.0

function Get-ConfiguredValue {
    param([object]$State, [string]$Name, [object]$SuppliedValue, [object]$DefaultValue)
    if ($null -ne $SuppliedValue -and "$SuppliedValue" -ne '' -and "$SuppliedValue" -ne '0') { return $SuppliedValue }
    if ($null -ne $State -and $State.PSObject.Properties.Name -contains $Name) {
        $stored = $State.$Name
        if ($null -ne $stored -and "$stored" -ne '') { return $stored }
    }
    return $DefaultValue
}

function Select-SqlExport {
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = [System.Windows.Forms.OpenFileDialog]::new()
        $dialog.Title = 'Select the fresh kanaanclients SQL export'
        $dialog.Filter = 'SQL exports (*.sql)|*.sql'
        $dialog.Multiselect = $false
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $dialog.FileName }
    }
    catch {
        Write-Verbose "The Windows file picker was unavailable: $($_.Exception.Message)"
    }
    return Read-Host 'Path to the fresh kanaanclients SQL export'
}

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$runningFromRepository = Test-Path -LiteralPath (Join-Path $repositoryRoot 'KCAS.slnx') -PathType Leaf
if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = if ($runningFromRepository) {
        $existingRehearsal = Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'artifacts') -Directory -Filter 'import-rehearsal-*' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'shared\legacy-snapshots') -PathType Container } |
            Select-Object -First 1
        if ($null -ne $existingRehearsal) { $existingRehearsal.FullName }
        else { Join-Path $repositoryRoot 'artifacts\manual-import-test' }
    }
    elseif ([System.IO.Path]::GetFileName($PSScriptRoot) -eq 'current') {
        [System.IO.Path]::GetDirectoryName($PSScriptRoot)
    }
    else {
        'D:\Deploy\KCAS'
    }
}
$installRootPath = [System.IO.Path]::GetFullPath($InstallRoot)
$sharedRoot = Join-Path $installRootPath 'shared'
$stateDirectory = Join-Path $sharedRoot 'legacy-import-operator'
$statePath = Join-Path $stateDirectory 'settings.json'
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null

$state = if (Test-Path -LiteralPath $statePath -PathType Leaf) {
    Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
} else { $null }

if ([string]::IsNullOrWhiteSpace($ConfigurationPath)) {
    $ConfigurationPath = if ($runningFromRepository) {
        Join-Path $repositoryRoot 'src\KCAS.Admin\appsettings.Development.json'
    }
    else {
        Join-Path $sharedRoot 'appsettings.Production.json'
    }
}

$applicationConnectionString = [Environment]::GetEnvironmentVariable('ConnectionStrings__DefaultConnection', 'Process')
if ([string]::IsNullOrWhiteSpace($applicationConnectionString)) {
    $configurationFullPath = [System.IO.Path]::GetFullPath($ConfigurationPath)
    if (-not (Test-Path -LiteralPath $configurationFullPath -PathType Leaf)) {
        throw "KCAS configuration not found: $configurationFullPath"
    }
    $applicationConfiguration = Get-Content -LiteralPath $configurationFullPath -Raw | ConvertFrom-Json
    $applicationConnectionString = [string]$applicationConfiguration.ConnectionStrings.DefaultConnection
}
if ([string]::IsNullOrWhiteSpace($applicationConnectionString)) {
    throw "KCAS has no configured 'ConnectionStrings:DefaultConnection'."
}

$connectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
$connectionBuilder.set_ConnectionString($applicationConnectionString)
function Get-ConnectionValue {
    param([System.Data.Common.DbConnectionStringBuilder]$Builder, [string[]]$Keys, [object]$DefaultValue)
    foreach ($key in $Keys) {
        if ($Builder.ContainsKey($key) -and -not [string]::IsNullOrWhiteSpace([string]$Builder[$key])) { return $Builder[$key] }
    }
    return $DefaultValue
}

$configuredHost = [string](Get-ConnectionValue $connectionBuilder @('server','host','data source') '127.0.0.1')
$configuredPort = [int](Get-ConnectionValue $connectionBuilder @('port') 3306)
$configuredDatabase = [string](Get-ConnectionValue $connectionBuilder @('database','initial catalog') 'kcas_blazor')
$configuredUser = [string](Get-ConnectionValue $connectionBuilder @('user','user id','uid','username') '')
$configuredPassword = [string](Get-ConnectionValue $connectionBuilder @('password','pwd') '')

$defaultMySqlBasePath = if (Test-Path -LiteralPath 'C:\wamp64\bin\mysql\mysql9.1.0' -PathType Container) { 'C:\wamp64\bin\mysql\mysql9.1.0' } else { 'D:\wamp64\bin\mysql\mysql9.1.0' }
$MySqlBasePath = [string](Get-ConfiguredValue $state 'mySqlBasePath' $MySqlBasePath $defaultMySqlBasePath)
$TargetHost = if ([string]::IsNullOrWhiteSpace($TargetHost)) { $configuredHost } else { $TargetHost }
$TargetPort = if ($TargetPort -le 0) { $configuredPort } else { $TargetPort }
$TargetDatabase = if ([string]::IsNullOrWhiteSpace($TargetDatabase)) { $configuredDatabase } else { $TargetDatabase }
$TargetUser = if ([string]::IsNullOrWhiteSpace($TargetUser)) { $configuredUser } else { $TargetUser }
$SourceUser = if ([string]::IsNullOrWhiteSpace($SourceUser)) { $TargetUser } else { $SourceUser }
$defaultReviewUrl = if ($runningFromRepository) { 'http://localhost:5143/imports' } else { 'http://localhost/imports' }
$ReviewUrl = [string](Get-ConfiguredValue $state 'reviewUrl' $ReviewUrl $defaultReviewUrl)

if ([string]::IsNullOrWhiteSpace($ToolsDirectory)) {
    $packagedTools = Join-Path $PSScriptRoot 'tools\legacy-import'
    if (Test-Path -LiteralPath $packagedTools -PathType Container) {
        $ToolsDirectory = $packagedTools
    }
    elseif ($runningFromRepository) {
        $ToolsDirectory = $PSScriptRoot
    }
    else { $ToolsDirectory = $PSScriptRoot }
}
$toolsPath = [System.IO.Path]::GetFullPath($ToolsDirectory)
$stageScript = Join-Path $toolsPath 'Stage-KCAS-LegacySnapshot.ps1'
$runScript = Join-Path $toolsPath 'Run-KCAS-LegacyImport.ps1'
$defaultImporterPath = if ($runningFromRepository) {
    Join-Path $repositoryRoot 'tools\KCAS.LegacyImport\bin\Release\net10.0\KCAS.LegacyImport.dll'
} else {
    Join-Path $toolsPath 'KCAS.LegacyImport.dll'
}
$ImporterPath = if ([string]::IsNullOrWhiteSpace($ImporterPath)) { $defaultImporterPath } else { $ImporterPath }
$defaultDotNetPath = if ($runningFromRepository) { Join-Path $repositoryRoot '.dotnet\dotnet.exe' } else { Join-Path $installRootPath 'repo\.dotnet\dotnet.exe' }
$DotNetPath = if ([string]::IsNullOrWhiteSpace($DotNetPath)) { $defaultDotNetPath } else { $DotNetPath }
foreach ($path in @($stageScript, $runScript, $ImporterPath,$DotNetPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Import component not found: $path" }
}

$targetPasswordOverride = [Environment]::GetEnvironmentVariable('KCAS_MYSQL_PASSWORD', 'Process')
$targetPassword = if ($null -ne $targetPasswordOverride) { $targetPasswordOverride } else { $configuredPassword }
$sourcePasswordOverride = [Environment]::GetEnvironmentVariable('KCAS_LEGACY_STAGE_PASSWORD', 'Process')
$sourcePassword = if ($null -ne $sourcePasswordOverride) { $sourcePasswordOverride } else { $targetPassword }

$latestManifestPath = if ($null -ne $state -and $state.PSObject.Properties.Name -contains 'latestManifestPath') { [string]$state.latestManifestPath } else { '' }
$latestScanRunId = if ($null -ne $state -and $state.PSObject.Properties.Name -contains 'latestScanRunId') { [long]$state.latestScanRunId } else { 0 }

if ($PSCmdlet.ParameterSetName -eq 'Scan') {
    if ([string]::IsNullOrWhiteSpace($SqlExportPath)) { $SqlExportPath = Select-SqlExport }
    if ([string]::IsNullOrWhiteSpace($SqlExportPath)) { throw 'No SQL export was selected.' }

    Write-Host 'Staging the immutable legacy snapshot...'
    $latestManifestPath = & $stageScript `
        -SqlExportPath $SqlExportPath `
        -SharedRoot $sharedRoot `
        -MySqlBasePath $MySqlBasePath `
        -MySqlHost $TargetHost `
        -MySqlPort $TargetPort `
        -MySqlUser $SourceUser `
        -MySqlPassword $sourcePassword

    Write-Host 'Comparing the snapshot with KCAS...'
    $latestScanRunId = & $runScript `
        -SnapshotManifestPath $latestManifestPath `
        -Mode Scan `
        -InstallRoot $installRootPath `
        -ImporterPath $ImporterPath `
        -DotNetPath $DotNetPath `
        -MySqlBasePath $MySqlBasePath `
        -TargetHost $TargetHost `
        -TargetPort $TargetPort `
        -TargetDatabase $TargetDatabase `
        -TargetUser $TargetUser `
        -TargetPassword $targetPassword `
        -SourceUser $SourceUser `
        -SourcePassword $sourcePassword
}
else {
    if ($ApplyNew -le 0) { throw '-ApplyNew requires the reviewed scan run number shown in KCAS.' }
    if ([string]::IsNullOrWhiteSpace($latestManifestPath) -or -not (Test-Path -LiteralPath $latestManifestPath -PathType Leaf)) {
        throw 'No staged snapshot is available. Run a scan first.'
    }

    Write-Host "Applying only safe new records approved from scan $ApplyNew..."
    $null = & $runScript `
        -SnapshotManifestPath $latestManifestPath `
        -Mode ApplyNew `
        -ApprovedScanRunId $ApplyNew `
        -InstallRoot $installRootPath `
        -ImporterPath $ImporterPath `
        -DotNetPath $DotNetPath `
        -MySqlBasePath $MySqlBasePath `
        -TargetHost $TargetHost `
        -TargetPort $TargetPort `
        -TargetDatabase $TargetDatabase `
        -TargetUser $TargetUser `
        -TargetPassword $targetPassword `
        -SourceUser $SourceUser `
        -SourcePassword $sourcePassword

    Write-Host 'Running the automatic post-apply verification scan...'
    $latestScanRunId = & $runScript `
        -SnapshotManifestPath $latestManifestPath `
        -Mode Scan `
        -InstallRoot $installRootPath `
        -ImporterPath $ImporterPath `
        -DotNetPath $DotNetPath `
        -MySqlBasePath $MySqlBasePath `
        -TargetHost $TargetHost `
        -TargetPort $TargetPort `
        -TargetDatabase $TargetDatabase `
        -TargetUser $TargetUser `
        -TargetPassword $targetPassword `
        -SourceUser $SourceUser `
        -SourcePassword $sourcePassword
}

$savedState = [ordered]@{
    schemaVersion = 1
    mySqlBasePath = $MySqlBasePath
    reviewUrl = $ReviewUrl
    latestManifestPath = $latestManifestPath
    latestScanRunId = $latestScanRunId
    updatedAtUtc = [DateTime]::UtcNow.ToString('O')
}
$savedState | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM

Write-Host ''
Write-Host "Scan complete. Review run $latestScanRunId at $ReviewUrl"
if (-not $NoOpenReview) {
    try { Start-Process $ReviewUrl } catch { Write-Warning "Could not open the review page automatically: $($_.Exception.Message)" }
}
Write-Output $latestScanRunId
