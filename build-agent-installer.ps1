<#
.SYNOPSIS
    Yordamchi Agent uchun Windows o'rnatuvchisini boshdan-oxir yig'adi.

.DESCRIPTION
    1. O'zi-yetarli (self-contained), ReadyToRun win-x64 agent build chiqaradi — nishon
       kompyuterda .NET o'rnatilgan bo'lishi shart emas.
    2. Uni YordamchiAgent-<versiya>-x64.msi ichiga joylaydi; MSI o'rnatish paytida agentning
       `--install` rejimini chaqiradi (xizmat + brandmauer qoidasi).
    3. Shu MSI ni YordamchiAgentSetup.exe (WiX Burn bootstrapper) bilan o'raydi.

    Chiqadigan fayl nomi ATAYLAB versiyasiz: dasturdagi yuklab olish havolasi shu nomga
    bog'langan (Yordamchi -> "Kompyuterlarni boshqarish" bo'limi).

    Muallif: Abduxalil Voxidjonov — https://t.me/abduxalilvoxidjonov

.PARAMETER Version
    MSI va bundle ichiga yoziladigan to'rt qismli mahsulot versiyasi.

.PARAMETER SkipPublish
    Mavjud publish\agent-win-x64 papkasini qayta qurmasdan ishlatadi.

.NOTES
    .NET SDK va WiX v5 talab qilinadi:
        dotnet tool install --global wix --version 5.*
        wix extension add -g WixToolset.BootstrapperApplications.wixext/5.0.2

    O'rnatish administrator huquqini talab qiladi (xizmat va brandmauer qoidasi).
#>
[CmdletBinding()]
param(
    [string]$Version = '1.0.0.0',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$publishDir = Join-Path $root 'publish\agent-win-x64'
$artifactDir = Join-Path $root 'artifacts'
$iconFile = Join-Path $root 'src\Yordamchi\Assets\Yordamchi.ico'
$licenseFile = Join-Path $root 'installer\License.rtf'

$shortVersion = ($Version -split '\.')[0..2] -join '.'
$msiPath = Join-Path $artifactDir "YordamchiAgent-$shortVersion-x64.msi"
$setupPath = Join-Path $artifactDir 'YordamchiAgentSetup.exe'

# WiX — dotnet global tool; yangi ochilgan konsolda PATH da bo'lmasligi mumkin.
$env:PATH = "$env:USERPROFILE\.dotnet\tools;$env:PATH"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

New-Item -ItemType Directory -Force $artifactDir | Out-Null

# ---------------------------------------------------------------- publish
if (-not $SkipPublish) {
    Step "Agentning o'zi-yetarli win-x64 buildi chiqarilmoqda"
    if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }

    dotnet publish (Join-Path $root 'src\Yordamchi.Agent\Yordamchi.Agent.csproj') `
        -c Release -r win-x64 --self-contained true `
        -p:PublishReadyToRun=true `
        -p:Version=$shortVersion -p:FileVersion=$Version -p:AssemblyVersion=$Version `
        -o $publishDir --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish bajarilmadi" }
}

if (-not (Test-Path (Join-Path $publishDir 'YordamchiAgent.exe'))) {
    throw "YordamchiAgent.exe topilmadi: $publishDir"
}

$files = Get-ChildItem -Recurse -File $publishDir
Write-Host ("    payload: {0:N0} files, {1:N1} MB" -f $files.Count, (($files | Measure-Object Length -Sum).Sum / 1MB))

# ---------------------------------------------------------------- msi
Step "MSI yig'ilmoqda"
wix build (Join-Path $root 'installer\Agent.wxs') `
    -arch x64 `
    -d "PublishDir=$publishDir" `
    -d "IconFile=$iconFile" `
    -d "ProductVersion=$Version" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "MSI yig'ilmadi" }
Write-Host ("    {0}  ({1:N1} MB)" -f (Split-Path $msiPath -Leaf), ((Get-Item $msiPath).Length / 1MB))

# ---------------------------------------------------------------- bundle
Step "YordamchiAgentSetup.exe yig'ilmoqda"
wix build (Join-Path $root 'installer\AgentBundle.wxs') `
    -arch x64 `
    -ext WixToolset.BootstrapperApplications.wixext `
    -d "MsiFile=$msiPath" `
    -d "IconFile=$iconFile" `
    -d "LicenseFile=$licenseFile" `
    -d "LogoFile=$(Join-Path $root 'installer\Logo.png')" `
    -d "ProductVersion=$Version" `
    -o $setupPath
if ($LASTEXITCODE -ne 0) { throw "Bundle yig'ilmadi" }
Write-Host ("    {0}  ({1:N1} MB)" -f (Split-Path $setupPath -Leaf), ((Get-Item $setupPath).Length / 1MB))

Step "Tayyor"
Write-Host "  Agentni o'rnatish (administrator sifatida): $setupPath"
Write-Host "  Xizmatni qo'lda boshqarish:  sc query YordamchiAgent"
