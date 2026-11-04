<#
  Compila e abre o CV Explorer em modo Debug.

  Uso normal (duplo clique em CVExplorer.cmd na raiz, ou):
      .\scripts\run.ps1

  Se ja existe uma instancia aberta, ela e fechada antes: durante o
  desenvolvimento o executavel fica travado e a janela velha ficaria na tela.
#>
param(
    [switch]$SemBuild,
    [string]$Pasta,
    [switch]$ManterAberta
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

if (-not $ManterAberta) {
    foreach ($processo in @(Get-Process CvExplorer -ErrorAction SilentlyContinue)) {
        Write-Host "Fechando a instancia aberta (pid $($processo.Id))..." -ForegroundColor DarkGray
        Stop-Process -Id $processo.Id -Force
        try { [void]$processo.WaitForExit(3000) } catch { }
    }

    # O ffmpeg do player fica preso a janela: encerra junto.
    Get-Process ffmpeg -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.Id -Force }

    # Espera o mutex de instancia unica ser liberado, senao a nova janela sai na hora.
    $limite = (Get-Date).AddSeconds(6)
    while ((Get-Process CvExplorer -ErrorAction SilentlyContinue) -and (Get-Date) -lt $limite) {
        Start-Sleep -Milliseconds 120
    }
}

if (-not $SemBuild) {
    & $dotnet build (Join-Path $raiz "src\CVExplorer.Host\CVExplorer.Host.csproj") -v q --nologo
    if ($LASTEXITCODE -ne 0) { throw "A compilacao falhou." }
}

if ($Pasta) {
    # Aspas obrigatorias: "NOVEMBRO AZUL" tem espaco e viraria tres argumentos.
    Start-Process -FilePath $exe -ArgumentList @("`"$Pasta`"")
} else {
    Start-Process -FilePath $exe
}

Write-Host "CV Explorer iniciado." -ForegroundColor Green
