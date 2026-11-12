# Ensaio da assinatura, sem a SignPath: assina com um certificado de teste criado na hora e confiado so nesta
# maquina do GitHub, que e descartada no fim. Serve para conferir o caminho todo (duas passadas do vpk, instalador,
# instalacao com os arquivos assinados). O que sai daqui nunca vai para as Releases: fora desta maquina, essa
# assinatura nao vale nada.
# Roda no Windows PowerShell (modulo PKI). Texto sem acentos de proposito: o 5.1 le o arquivo como ANSI.
param(
    [Parameter(Mandatory)] [string] $Origem,
    [Parameter(Mandatory)] [string] $Destino
)
$ErrorActionPreference = 'Stop'
$assunto = 'CN=Achievements MP (ensaio, sem valor)'

$cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object Subject -eq $assunto | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $assunto -CertStoreLocation Cert:\CurrentUser\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(2)
    foreach ($loja in 'Root', 'TrustedPublisher') {
        $store = New-Object Security.Cryptography.X509Certificates.X509Store($loja, 'LocalMachine')
        $store.Open('ReadWrite'); $store.Add($cert); $store.Close()
    }
}

New-Item -ItemType Directory -Force $Destino | Out-Null
foreach ($arquivo in Get-ChildItem -LiteralPath $Origem -File) {
    $alvo = Join-Path $Destino $arquivo.Name
    Copy-Item -LiteralPath $arquivo.FullName $alvo -Force
    $r = Set-AuthenticodeSignature -LiteralPath $alvo -Certificate $cert -HashAlgorithm SHA256
    if ($r.Status -ne 'Valid') { throw "Nao assinou $($arquivo.Name): $($r.Status) $($r.StatusMessage)" }
    Write-Host "Assinado (ensaio): $($arquivo.Name)"
}
