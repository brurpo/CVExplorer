<#
  Gera o pacote portatil da release com Velopack (sem instalador).
  Uso:  .\scripts\publish.ps1 -Versao 0.1.0 [-IncluirFfmpeg]
  Saida: .\releases  (win-Portable.zip + pacotes de atualizacao)
#>
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Repositorio = "",
    # Copia o ffmpeg do projeto (com DXV3 alpha, HAP e NNEDI) para dentro do pacote.
    [switch]$IncluirFfmpeg
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $raiz ".tools\dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) {
    & (Join-Path $PSScriptRoot "setup.ps1")
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$publicado = Join-Path $raiz "publish"
$releases = Join-Path $raiz "releases"
Remove-Item $publicado, $releases -Recurse -Force -ErrorAction SilentlyContinue

$extra = @()
if ($Repositorio) { $extra += "-p:RepositorioUrl=$Repositorio" }

& $dotnet publish (Join-Path $raiz "src\CVExplorer.Host\CVExplorer.Host.csproj") `
    -c Release -r win-x64 --self-contained true -p:Version=$Versao @extra -o $publicado
if ($LASTEXITCODE -ne 0) { throw "Falha no publish." }

if ($IncluirFfmpeg) {
    $origemFfmpeg = Join-Path $raiz ".tools\ffmpeg-dxv\bin"
    if (-not (Test-Path (Join-Path $origemFfmpeg "ffmpeg.exe"))) {
        throw "ffmpeg nao encontrado em $origemFfmpeg (rode scripts\build-ffmpeg-dxv.ps1)."
    }
    $destinoFfmpeg = Join-Path $publicado "ffmpeg"
    New-Item -ItemType Directory -Force -Path $destinoFfmpeg | Out-Null
    Copy-Item (Join-Path $origemFfmpeg "*") $destinoFfmpeg -Recurse -Force
}

$vpk = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
if (-not (Test-Path $vpk)) {
    & $dotnet tool install --global vpk
}

# --noInst: sem Setup.exe, a distribuicao e so o zip portatil (extrair e usar).
# O pacote portatil continua recebendo atualizacoes pelo proprio app.
& $vpk pack --packId CVExplorer --packVersion $Versao --packDir $publicado `
    --mainExe CvExplorer.exe --packTitle "CV Explorer" --packAuthors "CV Explorer" `
    --runtime win-x64 --channel win --noInst --outputDir $releases

Write-Host "Release gerada em $releases" -ForegroundColor Green
Write-Host "Para publicar no GitHub:" -ForegroundColor Cyan
Write-Host "  vpk upload github --repoUrl <url do repo> --token <token> --publish --tag v$Versao"
