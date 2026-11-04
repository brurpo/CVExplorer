<#
  Gera o instalador/release local com Velopack.
  Uso:  .\scripts\publish.ps1 -Versao 0.1.0
  Saida: .\releases
#>
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Repositorio = ""
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

$vpk = Join-Path $env:USERPROFILE ".dotnet\tools\vpk.exe"
if (-not (Test-Path $vpk)) {
    & $dotnet tool install --global vpk
}

& $vpk pack --packId CVExplorer --packVersion $Versao --packDir $publicado `
    --mainExe CvExplorer.exe --packTitle "CV Explorer" --packAuthors "CV Explorer" `
    --runtime win-x64 --channel win --outputDir $releases

Write-Host "Release gerada em $releases" -ForegroundColor Green
Write-Host "Para publicar no GitHub:" -ForegroundColor Cyan
Write-Host "  vpk upload github --repoUrl <url do repo> --token <token> --publish --tag v$Versao"
