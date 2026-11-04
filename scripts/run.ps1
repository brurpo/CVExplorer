<#
  Compila e abre o CV Explorer em modo Debug.
#>
param(
    [switch]$SemBuild
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $raiz ".tools\dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) {
    & (Join-Path $PSScriptRoot "setup.ps1")
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$exe = Join-Path $raiz "src\CVExplorer.Host\bin\Debug\net10.0-windows\CvExplorer.exe"

if (-not $SemBuild) {
    & $dotnet build (Join-Path $raiz "src\CVExplorer.Host\CVExplorer.Host.csproj") -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "A compilacao falhou." }
}

Start-Process -FilePath $exe
Write-Host "CV Explorer iniciado." -ForegroundColor Green
