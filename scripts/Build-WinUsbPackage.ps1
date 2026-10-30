# Builds and signs BJJCZ_WinUSB.cat for the WinUSB INF.
# Needs: inf2cat + signtool from Microsoft.Windows.SDK.BuildTools (auto-downloaded),
# and the signing PFX at C:\Users\Jarvis\tools\DriverSwitcher-sign.pfx
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path $PSScriptRoot -Parent
$infDir     = Join-Path $repoRoot 'drivers\winusb'
$staging    = Join-Path $env:TEMP 'winusb_pkg'
$toolsRoot  = Join-Path $env:USERPROFILE 'tools\winsdk'
$sdkVersion = '10.0.26100.1742'
$pfxPath    = Join-Path $env:USERPROFILE 'tools\DriverSwitcher-sign.pfx'
$pfxPass    = 'DriverSwitcher2026'

# --- 1. SDK build tools ---
function Find-SdkBinDir {
    $versionDir = Get-ChildItem (Join-Path $toolsRoot 'bin') -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'x64\signtool.exe') } | Select-Object -First 1
    if ($versionDir) { return Join-Path $versionDir.FullName 'x64' }
    return $null
}
$binDir = Find-SdkBinDir
if (-not $binDir) {
    $pkg = Join-Path $env:TEMP 'sdkbuildtools.zip'
    Write-Host "Downloading Microsoft.Windows.SDK.BuildTools $sdkVersion ..."
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.Windows.SDK.BuildTools/$sdkVersion" -OutFile $pkg
    $extract = Join-Path $env:TEMP 'sdkbuildtools_x'
    if (Test-Path $extract) { Remove-Item $extract -Recurse -Force }
    Expand-Archive -Path $pkg -DestinationPath $extract
    New-Item -ItemType Directory -Force -Path $toolsRoot | Out-Null
    Copy-Item (Join-Path $extract 'bin') $toolsRoot -Recurse -Force
    Remove-Item $pkg, $extract -Recurse -Force
    $binDir = Find-SdkBinDir
    if (-not $binDir) { throw "SDK tools were not installed correctly" }
}
$makecat  = Join-Path $binDir 'makecat.exe'
$signtool = Join-Path $binDir 'signtool.exe'
if (-not (Test-Path $makecat))  { throw "makecat.exe not found in $binDir" }
if (-not (Test-Path $signtool)) { throw "signtool.exe not found in $binDir" }
if (-not (Test-Path $pfxPath))  { throw "PFX not found: $pfxPath (run New-SigningCert.ps1 first)" }

# --- 2. Stage the driver package ---
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force -Path $staging | Out-Null
Copy-Item (Join-Path $infDir 'BJJCZ_WinUSB.inf') $staging

# --- 3. Catalog + sign ---
$cdf = @"
[CatalogHeader]
Name=BJJCZ_WinUSB.cat
PublicVersion=0x0000001
EncodingType=0x00010001
CATATTR1=0x10010001:OSAttr:2:10.0
[CatalogFiles]
<HASH>BJJCZ_WinUSB.inf=BJJCZ_WinUSB.inf
"@
Set-Content -Path (Join-Path $staging 'BJJCZ_WinUSB.cdf') -Value $cdf -Encoding ASCII
Push-Location $staging
try {
    & $makecat BJJCZ_WinUSB.cdf
    if ($LASTEXITCODE -ne 0) { throw "makecat failed: $LASTEXITCODE" }
} finally {
    Pop-Location
}
if (-not (Test-Path (Join-Path $staging 'BJJCZ_WinUSB.cat'))) { throw 'makecat did not produce the .cat file' }

& $signtool sign /fd SHA256 /f $pfxPath /p $pfxPass (Join-Path $staging 'BJJCZ_WinUSB.cat')
if ($LASTEXITCODE -ne 0) { throw "signtool failed: $LASTEXITCODE" }

Copy-Item (Join-Path $staging 'BJJCZ_WinUSB.cat') $infDir -Force
Remove-Item $staging -Recurse -Force

$sig = Get-AuthenticodeSignature (Join-Path $infDir 'BJJCZ_WinUSB.cat')
Write-Host "Catalog signature: $($sig.Status)"
Write-Host "Done: $(Join-Path $infDir 'BJJCZ_WinUSB.cat')"
