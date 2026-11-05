<#
  Cria o repositório no GitHub e publica o código.

  Precisa de um token com escopo "repo" (classic) ou "Contents/Administration: write"
  (fine-grained). Passe por parâmetro ou defina a variável de ambiente GITHUB_TOKEN:

      $env:GITHUB_TOKEN = "ghp_..."
      .\scripts\publicar-repo.ps1 -Nome CVExplorer -Visibilidade private

  O token nunca é impresso nem fica salvo: ele é usado uma vez no push, via um
  credential store temporário que o script apaga no final.
#>
param(
    [string]$Nome = "CVExplorer",
    [ValidateSet("private", "public")]
    [string]$Visibilidade = "private",
    [string]$Descricao = "Explorador de arquivos e gerente de projetos para pós-produção audiovisual",
    [string]$Token = $env:GITHUB_TOKEN,
    [switch]$SemPush
)

$ErrorActionPreference = "Stop"

$raiz = Split-Path $PSScriptRoot -Parent
$git = "git"

if (-not $Token) {
    Write-Host "Sem token do GitHub." -ForegroundColor Yellow
    Write-Host "  1) crie um em https://github.com/settings/tokens (escopo 'repo')"
    Write-Host "  2) rode: `$env:GITHUB_TOKEN = 'ghp_...' e chame este script de novo"
    Write-Host ""
    Write-Host "Sem token, o caminho manual é:" -ForegroundColor Cyan
    Write-Host "  git remote add origin https://github.com/SEU-USUARIO/$Nome.git"
    Write-Host "  git push -u origin main"
    exit 1
}

$cabecalhos = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent"           = "CVExplorer"
}

Write-Host "Consultando a conta autenticada..." -ForegroundColor Cyan
$usuario = (Invoke-RestMethod "https://api.github.com/user" -Headers $cabecalhos -TimeoutSec 60).login
Write-Host "Conta: $usuario" -ForegroundColor Green

$existe = $true
try {
    Invoke-RestMethod "https://api.github.com/repos/$usuario/$Nome" -Headers $cabecalhos -TimeoutSec 60 | Out-Null
    Write-Host "Repositório $usuario/$Nome já existe." -ForegroundColor Yellow
}
catch {
    $existe = $false
}

if (-not $existe) {
    Write-Host "Criando $usuario/$Nome ($Visibilidade)..." -ForegroundColor Cyan
    $corpo = @{
        name        = $Nome
        description = $Descricao
        private     = ($Visibilidade -eq "private")
        has_issues  = $true
        has_wiki    = $false
        auto_init   = $false
    } | ConvertTo-Json

    Invoke-RestMethod "https://api.github.com/user/repos" -Method Post -Headers $cabecalhos `
        -ContentType "application/json" -Body $corpo -TimeoutSec 60 | Out-Null
    Write-Host "Repositório criado." -ForegroundColor Green
}

$url = "https://github.com/$usuario/$Nome.git"
$urlLimpa = "https://github.com/$usuario/$Nome.git"

Push-Location $raiz
try {
    $remoto = & $git remote 2>$null
    if ($remoto -contains "origin") {
        & $git remote set-url origin $urlLimpa
    }
    else {
        & $git remote add origin $urlLimpa
    }

    if (-not $SemPush) {
        # Credencial temporária: não fica em argumento de processo nem no config global.
        $arquivoCredencial = Join-Path $env:TEMP ("cvexplorer-cred-" + [Guid]::NewGuid().ToString("N"))
        $conteudo = "https://x-access-token:$Token@github.com`n"
        Set-Content -Path $arquivoCredencial -Value $conteudo -NoNewline -Encoding ascii

        try {
            & $git -c "credential.helper=store --file=$arquivoCredencial" push -u origin HEAD
            if ($LASTEXITCODE -ne 0) { throw "git push falhou (código $LASTEXITCODE)" }
        }
        finally {
            Remove-Item $arquivoCredencial -Force -ErrorAction SilentlyContinue
        }
    }
    else {
        Write-Host "Push pulado (-SemPush)." -ForegroundColor Yellow
    }
}
finally {
    Pop-Location
}

# O build local passa a apontar as atualizações para o repositório real.
$csproj = Join-Path $raiz "src\CVExplorer.Host\CVExplorer.Host.csproj"
$texto = Get-Content $csproj -Raw
$novo = $texto -replace '(<RepositorioUrl Condition="''\$\(RepositorioUrl\)'' == ''''">)[^<]+(</RepositorioUrl>)', "`$1https://github.com/$usuario/$Nome`$2"
if ($novo -ne $texto) {
    Set-Content -Path $csproj -Value $novo -NoNewline -Encoding UTF8
    Write-Host "RepositorioUrl do csproj agora é https://github.com/$usuario/$Nome" -ForegroundColor Green
}

Write-Host ""
Write-Host "Pronto: https://github.com/$usuario/$Nome" -ForegroundColor Green
Write-Host "Para publicar uma release (o app verifica sozinho):" -ForegroundColor Cyan
Write-Host "  git tag v0.1.0"
Write-Host "  git push origin v0.1.0"
