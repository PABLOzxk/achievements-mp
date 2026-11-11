# Chamado pelo vpk (--signTemplate) com os arquivos que ele assinaria. A SignPath assina fora do vpk e precisa de
# aprovação manual, então o empacotamento roda duas vezes:
#   coletar: 1ª passada. Copia para $env:ASSINATURA_DIR/pedido os arquivos da lista arquivos.txt, sem assinar.
#            O workflow manda essa pasta para a SignPath, que devolve os assinados em $env:ASSINATURA_DIR/assinado.
#   trocar:  2ª passada. Troca cada arquivo pelo assinado, só se for byte a byte igual ao que foi coletado
#            (o vpk gera os mesmos bytes nas duas passadas; se não gerar, para tudo em vez de assinar outra coisa).
#   conferir: depois da 2ª passada, falha se algum arquivo coletado não foi trocado pelo assinado.
# O Setup.exe também passa por aqui, mas fica de fora da lista: ele embute o pacote, então é assinado depois.
param(
    [Parameter(Mandatory)] [ValidateSet('coletar', 'trocar', 'conferir')] [string] $Modo,
    [Parameter(ValueFromRemainingArguments)] [string[]] $Arquivos
)
$ErrorActionPreference = 'Stop'

$raiz = $env:ASSINATURA_DIR
if (-not $raiz) { throw 'ASSINATURA_DIR não definido' }
$pedido = Join-Path $raiz 'pedido'
$assinado = Join-Path $raiz 'assinado'
$trocados = Join-Path $raiz 'trocados.txt'

$padroes = Get-Content (Join-Path $PSScriptRoot 'arquivos.txt') |
    ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') }

# Assinatura Authenticode presente = entrada "Security" do cabeçalho PE com tamanho maior que zero.
# Lido à mão para funcionar fora do Windows também (o vpk empacota para Windows a partir de Linux).
function Test-Assinado([string] $Caminho) {
    $fs = [IO.File]::OpenRead($Caminho)
    try {
        $br = [IO.BinaryReader]::new($fs)
        if ($fs.Length -lt 0x40 -or $br.ReadUInt16() -ne 0x5A4D) { return $false }
        $fs.Position = 0x3C; $pe = $br.ReadInt32()
        $fs.Position = $pe + 24; $magic = $br.ReadUInt16()
        $fs.Position = $pe + 24 + $(if ($magic -eq 0x20B) { 112 } else { 96 }) + 4 * 8 + 4
        return $br.ReadUInt32() -gt 0
    } finally { $fs.Dispose() }
}

function Test-NaLista([string] $Nome) {
    foreach ($p in $padroes) { if ($Nome -like $p) { return $true } }
    return $false
}

if ($Modo -eq 'conferir') {
    $feitos = if (Test-Path -LiteralPath $trocados) { @(Get-Content -LiteralPath $trocados) } else { @() }
    $faltam = @(Get-ChildItem -LiteralPath $pedido -File | Where-Object { $_.Name -notin $feitos })
    if ($faltam) { throw "Ficaram sem a versão assinada no pacote: $($faltam.Name -join ', ')" }
    Write-Host "Todos os $($feitos.Count) arquivos coletados foram trocados pelos assinados."
    return
}

New-Item -ItemType Directory -Force $pedido | Out-Null
foreach ($arquivo in $Arquivos) {
    $nome = Split-Path $arquivo -Leaf
    if (-not (Test-NaLista $nome)) { continue }
    $coletado = Join-Path $pedido $nome
    $hash = (Get-FileHash -LiteralPath $arquivo -Algorithm SHA256).Hash

    if ($Modo -eq 'coletar') {
        if (Test-Assinado $arquivo) { Write-Host "Já assinado, fica como está: $nome"; continue }
        if ((Test-Path -LiteralPath $coletado) -and (Get-FileHash -LiteralPath $coletado -Algorithm SHA256).Hash -ne $hash) {
            throw "Dois arquivos diferentes com o nome $nome"
        }
        Copy-Item -LiteralPath $arquivo $coletado -Force
        Write-Host "Coletado para assinar: $nome ($hash)"
        continue
    }

    if (-not (Test-Path -LiteralPath $coletado)) { throw "$nome não foi coletado na 1ª passada" }
    if ((Get-FileHash -LiteralPath $coletado -Algorithm SHA256).Hash -ne $hash) {
        throw "$nome mudou entre as passadas ($hash); o assinado não corresponde a este arquivo"
    }
    $pronto = Join-Path $assinado $nome
    if (-not (Test-Path -LiteralPath $pronto)) { throw "A SignPath não devolveu $nome" }
    if (-not (Test-Assinado $pronto)) { throw "$nome voltou sem assinatura" }
    Copy-Item -LiteralPath $pronto $arquivo -Force
    Add-Content -LiteralPath $trocados $nome
    Write-Host "Trocado pelo assinado: $nome"
}
