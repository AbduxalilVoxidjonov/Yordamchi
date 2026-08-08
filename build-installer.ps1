<#
.SYNOPSIS
    Yordamchi uchun Windows o'rnatuvchisini boshdan-oxir yig'adi.

.DESCRIPTION
    1. O'zi-yetarli (self-contained), ReadyToRun win-x64 build chiqaradi — nishon kompyuterda
       .NET o'rnatilgan bo'lishi shart emas.
    2. Uni to'liq o'rnatish sehrgari bilan Yordamchi-<versiya>-x64.msi ichiga joylaydi.
    3. Shu MSI ni YordamchiSetup-<versiya>.exe (WiX Burn bootstrapper) bilan o'raydi.

    Muallif: Abduxalil Voxidjonov — https://t.me/abduxalilvoxidjonov

.PARAMETER Version
    MSI va bundle ichiga yoziladigan to'rt qismli mahsulot versiyasi. Standart qiymat 2.4.0.0.

.PARAMETER SkipPublish
    Mavjud publish\win-x64 papkasini qayta qurmasdan ishlatadi.

.NOTES
    .NET SDK va WiX v5 talab qilinadi:
        dotnet tool install --global wix --version 5.*
        wix extension add -g WixToolset.UI.wixext/5.0.2
        wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2
        wix extension add -g WixToolset.Util.wixext/5.0.2
#>
[CmdletBinding()]
param(
    [string]$Version = '2.4.0.0',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish\win-x64'
$artifactDir = Join-Path $root 'artifacts'
$iconFile = Join-Path $root 'src\Yordamchi\Assets\Yordamchi.ico'
$licenseFile = Join-Path $root 'installer\License.rtf'

$shortVersion = ($Version -split '\.')[0..2] -join '.'
$msiPath = Join-Path $artifactDir "Yordamchi-$shortVersion-x64.msi"
$setupPath = Join-Path $artifactDir "YordamchiSetup-$shortVersion.exe"

# WiX — dotnet global tool; yangi ochilgan konsolda PATH da bo'lmasligi mumkin.
$env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

New-Item -ItemType Directory -Force $artifactDir | Out-Null

# ---------------------------------------------------------------- publish
if (-not $SkipPublish) {
    Step "O'zi-yetarli win-x64 build chiqarilmoqda"
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    dotnet publish (Join-Path $root 'src\Yordamchi\Yordamchi.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true `
        -p:Version=$shortVersion -p:FileVersion=$Version -p:AssemblyVersion=$Version `
        -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish bajarilmadi" }
}

if (-not (Test-Path (Join-Path $publishDir 'Yordamchi.exe'))) {
    throw "Yordamchi.exe topilmadi: $publishDir"
}

$payload = (Get-ChildItem -Recurse -File $publishDir | Measure-Object Length -Sum).Sum
Write-Host ("    payload: {0:N0} files, {1:N1} MB" -f `
    (Get-ChildItem -Recurse -File $publishDir).Count, ($payload / 1MB))

# ---------------------------------------------------------------- msi
Step "MSI yig'ilmoqda"
wix build (Join-Path $root 'installer\Package.wxs') `
    -arch x64 `
    -ext WixToolset.UI.wixext `
    -d "PublishDir=$publishDir" `
    -d "IconFile=$iconFile" `
    -d "LicenseFile=$licenseFile" `
    -d "ProductVersion=$Version" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "MSI yig'ilmadi" }
Write-Host ("    {0}  ({1:N1} MB)" -f (Split-Path $msiPath -Leaf), ((Get-Item $msiPath).Length / 1MB))

# ---------------------------------------------------------------- vc++ redist
# Ekran yozuvi moduli C++/CLI kutubxonaga tayanadi, u esa VC++ 2015-2022 ish vaqtini
# talab qiladi. Uni bundle ichiga joylaymiz — o'rnatuvchi internetsiz ham ishlashi kerak.
# Fayl bir marta yuklab olinadi va keyingi yig'ilishlarda qayta ishlatiladi.
$vcRedistPath = Join-Path $artifactDir 'vc_redist.x64.exe'
if (-not (Test-Path $vcRedistPath)) {
    Step "Visual C++ ish vaqti yuklab olinmoqda (bir martalik)"
    try {
        Invoke-WebRequest -Uri 'https://aka.ms/vs/17/release/vc_redist.x64.exe' `
            -OutFile $vcRedistPath -UseBasicParsing
    }
    catch {
        throw "vc_redist.x64.exe yuklab olinmadi. Uni qo'lda yuklab, shu yerga qo'ying: $vcRedistPath"
    }
}
Write-Host ("    vc_redist.x64.exe  ({0:N1} MB)" -f ((Get-Item $vcRedistPath).Length / 1MB))

# ---------------------------------------------------------------- bundle
Step "Setup.exe bootstrapper yig'ilmoqda"
wix build (Join-Path $root 'installer\Bundle.wxs') `
    -arch x64 `
    -ext WixToolset.BootstrapperApplications.wixext `
    -ext WixToolset.Util.wixext `
    -d "MsiFile=$msiPath" `
    -d "VCRedistFile=$vcRedistPath" `
    -d "IconFile=$iconFile" `
    -d "LicenseFile=$licenseFile" `
    -d "LogoFile=$(Join-Path $root 'installer\Logo.png')" `
    -d "ProductVersion=$Version" `
    -o $setupPath
if ($LASTEXITCODE -ne 0) { throw "Bundle yig'ilmadi" }
Write-Host ("    {0}  ({1:N1} MB)" -f (Split-Path $setupPath -Leaf), ((Get-Item $setupPath).Length / 1MB))

Step "Tayyor"
Get-ChildItem $artifactDir -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
