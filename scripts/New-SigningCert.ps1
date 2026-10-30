# Creates a self-signed code-signing certificate for the WinUSB driver package.
# The public CER goes into the repo and is embedded into the app;
# the PFX (private key) stays local in C:\Users\Jarvis\tools.
$ErrorActionPreference = 'Stop'

$subject = 'CN=DriverSwitcher'
$existing = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $subject }
if ($existing) {
    $cert = $existing | Select-Object -First 1
    Write-Host "Certificate already exists: $($cert.Thumbprint)"
} else {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $subject `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(20) `
        -KeyExportPolicy Exportable -KeyLength 2048 -HashAlgorithm SHA256
    Write-Host "Created certificate: $($cert.Thumbprint)"
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$cerPath = Join-Path $repoRoot 'src\DriverSwitcher\Assets\DriverSwitcher.cer'
$pfxPath = 'C:\Users\Jarvis\tools\DriverSwitcher-sign.pfx'
New-Item -ItemType Directory -Force -Path (Split-Path $cerPath) | Out-Null

Export-Certificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $cerPath -Type CERT | Out-Null

$pfxPassword = ConvertTo-SecureString -String 'DriverSwitcher2026' -Force -AsPlainText
Export-PfxCertificate -Cert "Cert:\CurrentUser\My\$($cert.Thumbprint)" -FilePath $pfxPath -Password $pfxPassword | Out-Null

Write-Host "CER: $cerPath"
Write-Host "PFX: $pfxPath"
