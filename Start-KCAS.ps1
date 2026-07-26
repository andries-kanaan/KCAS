$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
$project = Join-Path $root 'src\KCAS.Admin\KCAS.Admin.csproj'
$appDir = Join-Path $root 'src\KCAS.Admin'
$appDll = Join-Path $appDir 'bin\Debug\net10.0\KCAS.Admin.dll'
$staticAssetManifest = Join-Path $appDir 'bin\Debug\net10.0\KCAS.Admin.staticwebassets.endpoints.json'

$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages'

& $dotnet build $project --no-incremental

if (-not (Test-Path $staticAssetManifest) -or
    -not (Select-String -Path $staticAssetManifest -SimpleMatch '"_framework/blazor.web.js"' -Quiet)) {
    throw "KCAS build did not produce the Blazor framework static asset endpoint."
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'

Push-Location $appDir
try {
    & $dotnet $appDll --urls 'http://127.0.0.1:5143'
}
finally {
    Pop-Location
}
