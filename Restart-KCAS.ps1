$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
$project = Join-Path $root 'src\KCAS.Admin\KCAS.Admin.csproj'
$appDir = Join-Path $root 'src\KCAS.Admin'
$appDll = Join-Path $appDir 'bin\Debug\net10.0\KCAS.Admin.dll'
$appDllCommandPart = 'KCAS.Admin.dll'

$runningApps = Get-CimInstance Win32_Process -Filter "name = 'dotnet.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.Contains($appDllCommandPart) }

foreach ($process in $runningApps) {
    Stop-Process -Id $process.ProcessId -Force
}

& $dotnet build $project

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
