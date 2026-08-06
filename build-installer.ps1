<#
.SYNOPSIS
    Builds the PdfEdit Windows installer end to end.

.DESCRIPTION
    1. Publishes a self-contained, ReadyToRun win-x64 build (no .NET required on the target PC).
    2. Packs it into PdfEdit-<version>-x64.msi with a full setup wizard.
    3. Wraps that MSI in PdfEditSetup-<version>.exe (WiX Burn bootstrapper).

.PARAMETER Version
    Four-part product version written into the MSI and the bundle. Default 1.0.0.0.

.PARAMETER SkipPublish
    Reuse an existing publish\win-x64 folder instead of rebuilding it.

.NOTES
    Requires the .NET SDK and WiX v5:
        dotnet tool install --global wix --version 5.*
        wix extension add -g WixToolset.UI.wixext/5.0.2
        wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0.0',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish\win-x64'
$artifactDir = Join-Path $root 'artifacts'
$iconFile = Join-Path $root 'src\PdfEdit\Assets\PdfEdit.ico'
$licenseFile = Join-Path $root 'installer\License.rtf'

$shortVersion = ($Version -split '\.')[0..2] -join '.'
$msiPath = Join-Path $artifactDir "PdfEdit-$shortVersion-x64.msi"
$setupPath = Join-Path $artifactDir "PdfEditSetup-$shortVersion.exe"

# WiX is a dotnet global tool and may not be on PATH in a fresh shell.
$env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

New-Item -ItemType Directory -Force $artifactDir | Out-Null

# ---------------------------------------------------------------- publish
if (-not $SkipPublish) {
    Step "Publishing self-contained win-x64 build"
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    dotnet publish (Join-Path $root 'src\PdfEdit\PdfEdit.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true `
        -p:Version=$shortVersion -p:FileVersion=$Version -p:AssemblyVersion=$Version `
        -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

if (-not (Test-Path (Join-Path $publishDir 'PdfEdit.exe'))) {
    throw "PdfEdit.exe not found in $publishDir"
}

$payload = (Get-ChildItem -Recurse -File $publishDir | Measure-Object Length -Sum).Sum
Write-Host ("    payload: {0:N0} files, {1:N1} MB" -f `
    (Get-ChildItem -Recurse -File $publishDir).Count, ($payload / 1MB))

# ---------------------------------------------------------------- msi
Step "Building MSI"
wix build (Join-Path $root 'installer\Package.wxs') `
    -arch x64 `
    -ext WixToolset.UI.wixext `
    -d "PublishDir=$publishDir" `
    -d "IconFile=$iconFile" `
    -d "LicenseFile=$licenseFile" `
    -d "ProductVersion=$Version" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }
Write-Host ("    {0}  ({1:N1} MB)" -f (Split-Path $msiPath -Leaf), ((Get-Item $msiPath).Length / 1MB))

# ---------------------------------------------------------------- bundle
Step "Building Setup.exe bootstrapper"
wix build (Join-Path $root 'installer\Bundle.wxs') `
    -arch x64 `
    -ext WixToolset.BootstrapperApplications.wixext `
    -d "MsiFile=$msiPath" `
    -d "IconFile=$iconFile" `
    -d "LicenseFile=$licenseFile" `
    -d "LogoFile=$(Join-Path $root 'installer\Logo.png')" `
    -d "ProductVersion=$Version" `
    -o $setupPath
if ($LASTEXITCODE -ne 0) { throw "Bundle build failed" }
Write-Host ("    {0}  ({1:N1} MB)" -f (Split-Path $setupPath -Leaf), ((Get-Item $setupPath).Length / 1MB))

Step "Done"
Get-ChildItem $artifactDir -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
