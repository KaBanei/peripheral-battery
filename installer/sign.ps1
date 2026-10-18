param(
    [string]$Path = (Join-Path $PSScriptRoot '..\dist\app'),
    [string]$PfxPath,
    [string]$PfxPassword,
    [string]$Thumbprint,
    [switch]$SelfSigned,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
# Signs every .exe under -Path with Authenticode.
# Modes:
#   -SelfSigned                 create a local code-signing cert (CurrentUser\My), trust it, sign
#   -Thumbprint <sha1>          use an existing cert in CurrentUser\My
#   -PfxPath <file> [-PfxPassword <pwd>]   import a PFX and sign
$ErrorActionPreference = 'Stop'

if ($SelfSigned -and -not $Thumbprint) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert `
        -Subject 'CN=2.4G Peripheral Battery (Self-Signed)' `
        -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(5)
    $Thumbprint = $cert.Thumbprint
    foreach ($store in @('Root','TrustedPublisher')) {
        $dst = "Cert:\CurrentUser\$store"
        $c = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
        $s = New-Object System.Security.Cryptography.X509Certificates.X509Store($store, 'CurrentUser')
        $s.Open('ReadWrite')
        $s.Add($c)
        $s.Close()
    }
    Write-Host "Created self-signed cert $Thumbprint and trusted it for CurrentUser." -ForegroundColor Yellow
    Write-Host "NOTE: self-signed only removes warnings on THIS machine." -ForegroundColor Yellow
}

if ($PfxPath) {
    $sec = ConvertTo-SecureString -String $PfxPassword -AsPlainText -Force
    Import-PfxCertificate -FilePath $PfxPath -CertStoreLocation 'Cert:\CurrentUser\My' -Password $sec | Out-Null
    if (-not $Thumbprint) {
        $Thumbprint = (Get-ChildItem 'Cert:\CurrentUser\My' | Where-Object { $_.HasPrivateKey } | Sort-Object NotAfter -Descending | Select-Object -First 1).Thumbprint
    }
}

if (-not $Thumbprint) { throw 'Provide -Thumbprint, -PfxPath or -SelfSigned.' }
$cert = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
Write-Host ("Using certificate: {0}  {1}" -f $cert.Subject, $cert.Thumbprint)

$files = Get-ChildItem -LiteralPath $Path -Recurse -File -Include *.exe, *.dll
foreach ($f in $files) {
    $sig = Set-AuthenticodeSignature -FilePath $f.FullName -Certificate $cert -TimestampServer $TimestampUrl
    Write-Host ("{0} -> {1}" -f $f.Name, $sig.Status)
}
