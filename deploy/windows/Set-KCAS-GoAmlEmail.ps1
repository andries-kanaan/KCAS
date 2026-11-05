[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Local', 'Live')]
    [string]$Target,
    [Parameter(Mandatory = $true)]
    [ValidateSet('Enabled', 'Disabled')]
    [string]$Sending,
    [switch]$SetPassword,
    [string]$RepositoryRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$InstallRoot = 'D:\Deploy\KCAS'
)

$ErrorActionPreference = 'Stop'

function Set-Setting($Object, [string]$Name, $Value) {
    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    } else {
        $Object.$Name = $Value
    }
}

function Get-Section($Object, [string]$Name) {
    if ($null -eq $Object.PSObject.Properties[$Name] -or $null -eq $Object.$Name) {
        Set-Setting $Object $Name ([pscustomobject]@{})
    }
    return $Object.$Name
}

if ($Target -eq 'Local') {
    $paths = @((Join-Path $RepositoryRoot 'src\KCAS.Admin\appsettings.Development.json'))
} else {
    $paths = @(
        (Join-Path $InstallRoot 'shared\appsettings.Production.json'),
        (Join-Path $InstallRoot 'current\app\appsettings.Production.json')
    )
}

# Validate every private target before requesting a secret or changing any settings.
$files = @()
foreach ($path in $paths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Private settings file not found: $path. No configuration was changed."
    }
    try { $configuration = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json }
    catch { throw "Private settings file is not valid JSON: $path. No configuration was changed." }
    if ($null -eq $configuration -or $configuration -isnot [pscustomobject]) {
        throw "Private settings must contain a JSON object: $path."
    }
    $files += [pscustomobject]@{ Path = $path; Configuration = $configuration; Acl = (Get-Acl -LiteralPath $path) }
}

$securePassword = $null
$credential = $null
try {
    if ($SetPassword) {
        Write-Host 'Enter the Gmail APP password, not your normal account password. Input is hidden.'
        $securePassword = Read-Host 'KCAS Gmail app password' -AsSecureString
        $credential = [System.Net.NetworkCredential]::new('andries@kanaantrust.com', $securePassword)
        $credential.Password = $credential.Password.Replace(' ', '')
        if ([string]::IsNullOrWhiteSpace($credential.Password)) { throw 'No app password entered. No settings changed.' }
    }
    foreach ($file in $files) {
        $configuration = $file.Configuration
        $reminders = Get-Section $configuration 'GoAmlReminders'
        Set-Setting $reminders 'Enabled' ($Target -eq 'Local' -or $Sending -eq 'Enabled')
        if ($Target -eq 'Local') {
            Set-Setting $reminders 'LocalEnabled' ($Sending -eq 'Enabled')
            Set-Setting $reminders 'LocalPublicBaseUrl' 'https://kcas.test:8443'
        }
        Set-Setting $reminders 'Recipient' 'andries@kanaantrust.com'
        Set-Setting $reminders 'PublicBaseUrl' 'https://kcas:8443'
        Set-Setting $reminders 'HourLocal' 8
        $email = Get-Section $configuration 'Email'
        $smtp = Get-Section $email 'Smtp'
        Set-Setting $smtp 'Host' 'smtp.gmail.com'
        Set-Setting $smtp 'Port' 587
        Set-Setting $smtp 'EnableSsl' $true
        Set-Setting $smtp 'From' 'andries@kanaantrust.com'
        Set-Setting $smtp 'UserName' 'andries@kanaantrust.com'
        if ($SetPassword) { Set-Setting $smtp 'Password' $credential.Password }
        if ($Sending -eq 'Enabled' -and [string]::IsNullOrWhiteSpace([string]$smtp.Password)) {
            throw "No saved SMTP password in $($file.Path). Run again with -SetPassword. No settings changed."
        }
    }

    foreach ($file in $files) {
        # Match the existing private file's ACL before writing any secrets to a temporary file.
        $temporaryPath = "$($file.Path).$([guid]::NewGuid().ToString('N')).tmp"
        try {
            [System.IO.File]::WriteAllText($temporaryPath, '')
            Set-Acl -LiteralPath $temporaryPath -AclObject $file.Acl
            $json = $file.Configuration | ConvertTo-Json -Depth 100
            [System.IO.File]::WriteAllText($temporaryPath, $json, [System.Text.UTF8Encoding]::new($false))
            Move-Item -LiteralPath $temporaryPath -Destination $file.Path -Force
        }
        finally {
            if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
        }
        Write-Host "Updated private configuration: $($file.Path)"
    }
    Write-Host "$Target reminder sending: $Sending. Restart KCAS to apply."
    Write-Host 'The password is stored in private JSON configuration (not encrypted). Keep its existing restricted NTFS permissions.'
    Write-Host 'Environment-variable overrides take precedence. No email was sent by this setup command.'
    if ($Target -eq 'Local') {
        Write-Host 'Turn local sending off after testing to avoid duplicate reminders from local and live.'
    }
}
finally {
    if ($null -ne $credential) { $credential.Password = '' }
    if ($null -ne $securePassword) { $securePassword.Dispose() }
}
