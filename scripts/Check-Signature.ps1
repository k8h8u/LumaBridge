param([string]$Path)
$s = Get-AuthenticodeSignature -LiteralPath $Path
"Status : $($s.Status)"
if ($s.SignerCertificate) {
    "Subject   : $($s.SignerCertificate.Subject)"
    "Issuer    : $($s.SignerCertificate.Issuer)"
    "NotAfter  : $($s.SignerCertificate.NotAfter)"
    "Thumbprint: $($s.SignerCertificate.Thumbprint)"
} else {
    "SignerCertificate: <none>"
}
