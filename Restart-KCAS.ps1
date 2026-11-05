$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
$project = Join-Path $root 'src\KCAS.Admin\KCAS.Admin.csproj'
$appDir = Join-Path $root 'src\KCAS.Admin'
$appDll = Join-Path $appDir 'bin\Debug\net10.0\KCAS.Admin.dll'
$staticAssetManifest = Join-Path $appDir 'bin\Debug\net10.0\KCAS.Admin.staticwebassets.endpoints.json'
$appDllCommandPart = 'KCAS.Admin.dll'

$env:NUGET_PACKAGES = Join-Path $env:USERPROFILE '.nuget\packages'

$runningApps = Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" |
    Where-Object { $_.ExecutablePath -eq $dotnet -and $_.CommandLine -and $_.CommandLine.Contains($appDllCommandPart) }

foreach ($process in $runningApps) {
    # Crash-reporting children can keep the DLL mapped after the main process exits.
    & taskkill.exe /PID $process.ProcessId /T /F
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if (Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue) {
        throw "KCAS process $($process.ProcessId) did not exit. Clear its process tree before restarting."
    }
}

if (Test-Path -LiteralPath $appDll) {
    try {
        $lockCheck = [System.IO.File]::Open($appDll, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
        $lockCheck.Dispose()
    }
    catch {
        throw "KCAS.Admin.dll is still locked. No build or startup was attempted. Clear the locking process (administrator rights may be needed)."
    }
}

& $dotnet build $project
if ($LASTEXITCODE -ne 0) {
    throw "KCAS build failed with exit code $LASTEXITCODE. KCAS was not started."
}

if (-not (Test-Path $staticAssetManifest) -or
    -not (Select-String -Path $staticAssetManifest -SimpleMatch '"_framework/blazor.web.js"' -Quiet)) {
    throw "KCAS build did not produce the Blazor framework static asset endpoint."
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
Start-Process `
    -FilePath $dotnet `
    -ArgumentList 'bin\Debug\net10.0\KCAS.Admin.dll', '--urls', 'http://127.0.0.1:5143' `
    -WorkingDirectory $appDir `
    -WindowStyle Hidden

function Wait-ForHttpStatus {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Url,
        [switch] $Insecure,
        [int] $TimeoutSeconds = 60
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $status = '000'

    do {
        $arguments = @('-L', '-s', '-o', 'NUL', '-w', '%{http_code}')
        if ($Insecure) {
            $arguments = @('-k') + $arguments
        }

        $status = & curl.exe @arguments $Url
        if ($status -in @('200', '302')) {
            return $status
        }

        Start-Sleep -Seconds 2
    } while ((Get-Date) -lt $deadline)

    return $status
}

$kestrelStatus = Wait-ForHttpStatus -Url 'http://127.0.0.1:5143/Account/Login'
$proxyStatus = Wait-ForHttpStatus -Url 'https://kcas.test:8443/Account/Login' -Insecure

Write-Host "Kestrel /Account/Login status: $kestrelStatus"
Write-Host "Proxy /Account/Login status: $proxyStatus"

if ($kestrelStatus -notin @('200', '302') -or $proxyStatus -notin @('200', '302')) {
    throw "KCAS restart verification failed."
}
