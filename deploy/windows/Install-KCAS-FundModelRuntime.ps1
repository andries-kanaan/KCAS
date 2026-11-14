[CmdletBinding()]
param(
    [string]$Python = 'python',
    [string]$RuntimeDirectory = (Join-Path $PSScriptRoot '..\..\.fund-model-runtime'),
    [string]$Requirements = (Join-Path $PSScriptRoot '..\..\src\KCAS.Admin\FundModel\requirements.txt')
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Requirements) -and (Test-Path (Join-Path $PSScriptRoot 'app\FundModel\requirements.txt'))) {
    $Requirements = Join-Path $PSScriptRoot 'app\FundModel\requirements.txt'
}
if (-not (Test-Path -LiteralPath $Requirements)) { throw "Requirements not found: $Requirements" }
& $Python -m venv $RuntimeDirectory
if ($LASTEXITCODE -ne 0) { throw 'Could not create fund-model runtime.' }
$runtimePython = Join-Path $RuntimeDirectory 'Scripts\python.exe'
& $runtimePython -m pip install --disable-pip-version-check -r $Requirements
if ($LASTEXITCODE -ne 0) { throw 'Could not install pinned calculation dependencies.' }
& $runtimePython -c 'import numpy, scipy, openpyxl; print("Fund modelling runtime ready")'
if ($LASTEXITCODE -ne 0) { throw 'Runtime health check failed.' }
Write-Output "Configure FundModelling__PythonPath=$((Resolve-Path -LiteralPath $runtimePython).Path) in the KCAS process environment."
