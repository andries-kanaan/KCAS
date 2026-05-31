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

Start-Sleep -Seconds 2

$kestrelStatus = & curl.exe -s -o NUL -w "%{http_code}" http://127.0.0.1:5143/clients
$proxyStatus = & curl.exe -k -L -s -o NUL -w "%{http_code}" https://kcas.test:8443/clients

Write-Host "Kestrel /clients status: $kestrelStatus"
Write-Host "Proxy /clients status: $proxyStatus"

if ($kestrelStatus -notin @('200', '302') -or $proxyStatus -ne '200') {
    throw "KCAS restart verification failed."
}
