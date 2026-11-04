<#
  Prepara o ambiente de desenvolvimento sem depender de nada instalado na maquina:
  baixa o SDK .NET 10 para .tools\dotnet (ignorado pelo git) e restaura os pacotes.
#>
param(
    [string]$CanalDotnet = "10.0"
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
$dotnetDir = Join-Path $raiz ".tools\dotnet"
$dotnet = Join-Path $dotnetDir "dotnet.exe"

if (-not (Test-Path $dotnet)) {
    Write-Host "Baixando o SDK .NET $CanalDotnet..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path (Join-Path $raiz ".tools") | Out-Null
    $instalador = Join-Path $raiz ".tools\dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $instalador -UseBasicParsing
    & powershell -NoProfile -ExecutionPolicy Bypass -File $instalador -Channel $CanalDotnet -InstallDir $dotnetDir
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
& $dotnet --version
& $dotnet restore (Join-Path $raiz "CVExplorer.slnx")
Write-Host "Pronto. Use .\scripts\run.ps1 para abrir o app." -ForegroundColor Green
