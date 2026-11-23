<#
.SYNOPSIS
  Builds an x64 MSIX package of GB DeliveryCalc.

.EXAMPLE
  pwsh packaging/build-msix.ps1 -Version 1.0.0.0
  pwsh packaging/build-msix.ps1 -Version 1.0.0.0 -CertificatePath C:\certs\test.pfx   # signed, for sideload testing only

.NOTES
  Requires Windows 11 x64, the .NET SDK from global.json and the Windows SDK (MakeAppx.exe / SignTool.exe).
  Identity values come from packaging/identity.local.json if present, else the placeholders in identity.json.
  Store submissions do not need signing: the Store re-signs MSIX packages after certification.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+\.0$')] [string] $Version,
    [string] $IdentityFile,
    [string] $CertificatePath,
    [securestring] $CertificatePassword,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$packaging = $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $packaging 'out' }

if ($Version.Split('.')[0] -eq '0') { throw "The first version section cannot be 0 for Store packages. Use e.g. 1.0.0.0." }

if (-not $IdentityFile) {
    $local = Join-Path $packaging 'identity.local.json'
    $IdentityFile = if (Test-Path $local) { $local } else { Join-Path $packaging 'identity.json' }
}
$identity = Get-Content $IdentityFile -Raw | ConvertFrom-Json
if ($identity.IdentityName -like '*Placeholder*') {
    Write-Warning "Using PLACEHOLDER package identity from $IdentityFile. This package can be sideloaded for testing but will be rejected by Partner Center."
}

function Find-SdkTool([string] $name) {
    $root = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem $root -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100)." }
    $tool.FullName
}

$layout = Join-Path $OutputDirectory 'layout'
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout | Out-Null

Write-Host "Publishing self-contained win-x64 build..."
dotnet publish (Join-Path $repo 'src\GBDeliveryCalc.App\GBDeliveryCalc.App.csproj') `
    -c Release -r win-x64 --self-contained true -p:Version=$($Version.Substring(0, $Version.LastIndexOf('.'))) -o $layout
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Copy-Item (Join-Path $packaging 'Assets') (Join-Path $layout 'Assets') -Recurse

$manifest = Get-Content (Join-Path $packaging 'Package.appxmanifest.template') -Raw
$values = @{
    IdentityName         = $identity.IdentityName
    Publisher            = $identity.Publisher
    PublisherDisplayName = [System.Security.SecurityElement]::Escape($identity.PublisherDisplayName)
    DisplayName          = [System.Security.SecurityElement]::Escape($identity.DisplayName)
    Version              = $Version
}
foreach ($k in $values.Keys) { $manifest = $manifest.Replace("{{$k}}", $values[$k]) }
if ($manifest -match '\{\{') { throw "Unreplaced token in manifest." }
[xml]$null = $manifest   # throws if not well-formed
Set-Content -Path (Join-Path $layout 'AppxManifest.xml') -Value $manifest -Encoding UTF8

$msix = Join-Path $OutputDirectory ("GBDeliveryCalc_{0}_x64.msix" -f $Version)
& (Find-SdkTool 'makeappx.exe') pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed." }

if ($CertificatePath) {
    $plain = if ($CertificatePassword) { [System.Net.NetworkCredential]::new('', $CertificatePassword).Password } else { $null }
    $signArgs = @('sign', '/fd', 'SHA256', '/f', $CertificatePath)
    if ($plain) { $signArgs += @('/p', $plain) }
    $signArgs += $msix
    & (Find-SdkTool 'signtool.exe') @args
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed. The certificate subject must equal the manifest Publisher ($($identity.Publisher))." }
}

Write-Host "Package: $msix"
