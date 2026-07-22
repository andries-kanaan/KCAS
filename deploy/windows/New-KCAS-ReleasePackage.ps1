[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$OutputDirectory = (Join-Path $RepositoryRoot 'artifacts\windows-release'),
    [string]$DotNetPath = 'dotnet',
    [string]$GitCommit,
    [string]$VersionLabel,
    [switch]$NoRestore,
    [switch]$AllowDirtyWorkingTree
)

$ErrorActionPreference = 'Stop'

$repositoryRootPath = [System.IO.Path]::GetFullPath($RepositoryRoot)
$projectPath = Join-Path $repositoryRootPath 'src\KCAS.Admin\KCAS.Admin.csproj'
$importerProjectPath = Join-Path $repositoryRootPath 'tools\KCAS.LegacyImport\KCAS.LegacyImport.csproj'
$migrationsPath = Join-Path $repositoryRootPath 'src\KCAS.Admin\Data\Migrations'
$databaseScriptPath = Join-Path $repositoryRootPath 'Apply-KCAS-Database.ps1'
$snapshotScriptPath = Join-Path $repositoryRootPath 'deploy\windows\Stage-KCAS-LegacySnapshot.ps1'
$importScriptPath = Join-Path $repositoryRootPath 'deploy\windows\Run-KCAS-LegacyImport.ps1'

foreach ($requiredPath in @($projectPath, $importerProjectPath, $migrationsPath, $databaseScriptPath, $snapshotScriptPath, $importScriptPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required release input not found: $requiredPath"
    }
}

if (-not $AllowDirtyWorkingTree) {
    $workingTreeStatus = & git -C $repositoryRootPath status --porcelain
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not inspect the Git working tree before packaging.'
    }
    if ($workingTreeStatus) {
        throw 'Refusing to create a release from a dirty working tree. Commit the reviewed changes first.'
    }
}

if ([string]::IsNullOrWhiteSpace($GitCommit)) {
    $GitCommit = (& git -C $repositoryRootPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($GitCommit)) {
        throw 'Could not determine the Git commit for the release package.'
    }
}

if ($GitCommit -notmatch '^[0-9a-fA-F]{40}$') {
    throw "GitCommit must be a full 40-character commit SHA. Received '$GitCommit'."
}

if ([string]::IsNullOrWhiteSpace($VersionLabel)) {
    $VersionLabel = $GitCommit.Substring(0, 12)
}

$safeVersionLabel = $VersionLabel -replace '[^A-Za-z0-9._-]', '-'
if ([string]::IsNullOrWhiteSpace($safeVersionLabel)) {
    throw 'VersionLabel does not contain any safe filename characters.'
}

$latestMigration = Get-ChildItem -LiteralPath $migrationsPath -Filter '*.cs' |
    Where-Object { $_.Name -notlike '*.Designer.cs' -and $_.Name -match '^\d{14}_.+\.cs$' } |
    Sort-Object Name |
    Select-Object -Last 1 -ExpandProperty BaseName

if ([string]::IsNullOrWhiteSpace($latestMigration)) {
    throw "Could not determine the latest migration under '$migrationsPath'."
}

$schemaPath = Join-Path $migrationsPath 'kcas_blazor_schema.sql'
if (-not (Select-String -LiteralPath $schemaPath -SimpleMatch "VALUES ('$latestMigration'" -Quiet)) {
    throw "The fresh-database schema does not include latest migration '$latestMigration'. Regenerate and review kcas_blazor_schema.sql before packaging."
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputDirectoryPath = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path $outputDirectoryPath ('.staging-' + [Guid]::NewGuid().ToString('N'))
$releaseRoot = Join-Path $stagingRoot 'release'
$appOutput = Join-Path $releaseRoot 'app'
$importerOutput = Join-Path $releaseRoot 'tools\legacy-import'
$databaseOutput = Join-Path $releaseRoot 'database'
$packageName = "KCAS-$safeVersionLabel-win-x64"
$packagePath = Join-Path $outputDirectoryPath "$packageName.zip"
$checksumPath = "$packagePath.sha256"

try {
    New-Item -ItemType Directory -Path $appOutput -Force | Out-Null
    New-Item -ItemType Directory -Path $importerOutput -Force | Out-Null
    New-Item -ItemType Directory -Path $databaseOutput -Force | Out-Null

    $publishArguments = @(
        'publish',
        $projectPath,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $appOutput,
        '-p:UseAppHost=true'
    )
    if ($NoRestore) {
        $publishArguments += '--no-restore'
    }

    & $DotNetPath @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    $importerPublishArguments = @(
        'publish',
        $importerProjectPath,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $importerOutput,
        '-p:UseAppHost=true'
    )
    if ($NoRestore) { $importerPublishArguments += '--no-restore' }
    & $DotNetPath @importerPublishArguments
    if ($LASTEXITCODE -ne 0) { throw "Legacy importer publish failed with exit code $LASTEXITCODE." }

    # Environment-specific configuration and generated local artifacts must
    # never enter an immutable release, even if a developer publishes locally.
    foreach ($outputPath in @($appOutput, $importerOutput)) {
        foreach ($configurationName in @('appsettings.Development.json', 'appsettings.Local.json', 'appsettings.Production.json')) {
            $configurationPath = Join-Path $outputPath $configurationName
            if (Test-Path -LiteralPath $configurationPath) {
                Remove-Item -LiteralPath $configurationPath -Force
            }
        }
    }
    foreach ($excludedDirectory in @((Join-Path $appOutput 'artifacts'), (Join-Path $appOutput 'App_Data\DataProtectionKeys'))) {
        if (Test-Path -LiteralPath $excludedDirectory) {
            Remove-Item -LiteralPath $excludedDirectory -Recurse -Force
        }
    }

    Copy-Item -LiteralPath $databaseScriptPath -Destination (Join-Path $databaseOutput 'Apply-KCAS-Database.ps1')
    Copy-Item -LiteralPath $migrationsPath -Destination (Join-Path $databaseOutput 'Migrations') -Recurse
    Copy-Item -LiteralPath $snapshotScriptPath -Destination (Join-Path $importerOutput 'Stage-KCAS-LegacySnapshot.ps1')
    Copy-Item -LiteralPath $importScriptPath -Destination (Join-Path $importerOutput 'Run-KCAS-LegacyImport.ps1')

    foreach ($requiredOutput in @(
        (Join-Path $appOutput 'KCAS.Admin.exe'),
        (Join-Path $importerOutput 'KCAS.LegacyImport.exe'),
        (Join-Path $importerOutput 'Stage-KCAS-LegacySnapshot.ps1'),
        (Join-Path $importerOutput 'Run-KCAS-LegacyImport.ps1')
    )) {
        if (-not (Test-Path -LiteralPath $requiredOutput -PathType Leaf)) { throw "Release output is missing '$requiredOutput'." }
    }

    $manifest = [ordered]@{
        schemaVersion = 1
        application = 'KCAS.Admin'
        version = $safeVersionLabel
        gitCommit = $GitCommit.ToLowerInvariant()
        builtAtUtc = [DateTime]::UtcNow.ToString('O')
        targetFramework = 'net10.0'
        runtime = 'win-x64'
        selfContained = $true
        entryPoint = 'app/KCAS.Admin.exe'
        legacyImporter = 'tools/legacy-import/KCAS.LegacyImport.exe'
        legacySnapshotStager = 'tools/legacy-import/Stage-KCAS-LegacySnapshot.ps1'
        legacyImportRunner = 'tools/legacy-import/Run-KCAS-LegacyImport.ps1'
        latestMigration = $latestMigration
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $releaseRoot 'deployment-manifest.json') -Encoding utf8NoBOM

    if (Test-Path -LiteralPath $packagePath) {
        Remove-Item -LiteralPath $packagePath -Force
    }
    if (Test-Path -LiteralPath $checksumPath) {
        Remove-Item -LiteralPath $checksumPath -Force
    }

    Compress-Archive -Path (Join-Path $releaseRoot '*') -DestinationPath $packagePath -CompressionLevel Optimal
    $checksum = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$checksum  $([System.IO.Path]::GetFileName($packagePath))" |
        Set-Content -LiteralPath $checksumPath -Encoding ascii

    Write-Host "Created immutable KCAS Windows release: $packagePath"
    Write-Host "Checksum: $checksumPath"
    Write-Output $packagePath
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }
}
