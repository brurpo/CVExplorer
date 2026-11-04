<#
  Compila um ffmpeg com DXV em DXT5 (alpha).

  Por que isso existe: o encoder dxv do FFmpeg só implementa DXT1, que não guarda
  transparência. O decoder já entende DXT5 (na verdade DXT4, porque o alpha é
  pré-multiplicado), então o patch em vendor\ffmpeg só completa o outro lado.
  Sem este build, "DXV3 (alpha)" fica indisponível e o app usa HAP Alpha.

  Uso:
      .\scripts\build-ffmpeg-dxv.ps1              # baixa, aplica o patch, compila
      .\scripts\build-ffmpeg-dxv.ps1 -Forcar      # recompila do zero
      .\scripts\build-ffmpeg-dxv.ps1 -Ref n8.0    # FFmpeg de uma tag/branch específica

  Requisitos: Windows x64 e internet. O toolchain (MSYS2 + gcc + nasm + snappy)
  é instalado em .tools\msys64 — nada é instalado no sistema.
#>
param(
    [string]$Ref = "master",
    [switch]$Forcar
)

$ErrorActionPreference = "Stop"

$raiz = Split-Path $PSScriptRoot -Parent
$msys = Join-Path $raiz ".tools\msys64"
$bash = Join-Path $msys "usr\bin\bash.exe"
$origem = Join-Path $raiz ".tools\ffmpeg-build"
$destino = Join-Path $raiz ".tools\ffmpeg-dxv\bin"
$patch = Join-Path $raiz "vendor\ffmpeg\0001-dxvenc-dxt5-alpha.patch"

# Caminho no formato que o bash do MSYS2 entende (E:\x -> /e/x).
function CaminhoMsys([string]$caminho) {
    $completo = (Resolve-Path -LiteralPath $caminho).Path
    $unidade = $completo.Substring(0, 1).ToLowerInvariant()
    return "/$unidade" + ($completo.Substring(2) -replace '\\', '/')
}

function ExecutarBash([string]$comando) {
    & $bash -lc $comando
    if ($LASTEXITCODE -ne 0) {
        throw "Falhou: $comando"
    }
}

if (-not (Test-Path $bash)) {
    Write-Host "Baixando MSYS2 portatil (uma vez, ~41 MB)..." -ForegroundColor Cyan
    $arquivo = Join-Path $raiz ".tools\msys2.tar.xz"
    New-Item -ItemType Directory -Path (Join-Path $raiz ".tools") -Force | Out-Null
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest "https://github.com/msys2/msys2-installer/releases/latest/download/msys2-base-x86_64-latest.tar.xz" `
        -OutFile $arquivo -UseBasicParsing -TimeoutSec 1800

    New-Item -ItemType Directory -Path $msys -Force | Out-Null
    tar -xf $arquivo -C $msys
    Get-ChildItem -Path $msys -Directory | Where-Object { Test-Path (Join-Path $_.FullName "usr\bin\bash.exe") } |
        ForEach-Object { Get-ChildItem -Path $_.FullName -Force | Move-Item -Destination $msys -Force }
    Remove-Item $arquivo -Force
}

Write-Host "Conferindo o toolchain..." -ForegroundColor Cyan
ExecutarBash "pacman -Sy --noconfirm >/dev/null 2>&1; pacman -S --noconfirm --needed make diffutils patch mingw-w64-x86_64-gcc mingw-w64-x86_64-nasm mingw-w64-x86_64-snappy 2>&1 | tail -3"

if ($Forcar -or -not (Test-Path $origem)) {
    Write-Host "Baixando o codigo do FFmpeg ($Ref)..." -ForegroundColor Cyan
    Remove-Item $origem -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path (Join-Path $raiz ".tools") -Force | Out-Null
    $tarball = Join-Path $raiz ".tools\ffmpeg-$($Ref -replace '[/\\]', '-').tar.gz"
    $ProgressPreference = "SilentlyContinue"
    Invoke-WebRequest "https://github.com/FFmpeg/FFmpeg/archive/refs/heads/$Ref.tar.gz" `
        -OutFile $tarball -UseBasicParsing -TimeoutSec 1800
    New-Item -ItemType Directory -Path $origem -Force | Out-Null
    tar -xzf $tarball -C $origem
    Remove-Item $tarball -Force

    $fontes = Get-ChildItem $origem -Directory | Select-Object -First 1
    Write-Host "Aplicando $([System.IO.Path]::GetFileName($patch))..." -ForegroundColor Cyan
    ExecutarBash "cd '$(CaminhoMsys $fontes.FullName)' && patch -p1 --forward < '$(CaminhoMsys $patch)'"
}

$fontes = Get-ChildItem $origem -Directory | Select-Object -First 1
$msysFontes = CaminhoMsys $fontes.FullName

Write-Host "Configurando (LGPL, sem dependencias externas alem do snappy)..." -ForegroundColor Cyan
ExecutarBash @"
export PATH=/mingw64/bin:`$PATH
cd '$msysFontes' && ./configure \
  --arch=x86_64 --target-os=mingw32 \
  --disable-doc --disable-debug --disable-autodetect --disable-ffplay --disable-network \
  --enable-ffmpeg --enable-ffprobe --enable-zlib --enable-mediafoundation --enable-libsnappy \
  --extra-ldflags=-static --extra-cflags=-O2
"@

Write-Host "Compilando (isso leva alguns minutos)..." -ForegroundColor Cyan
ExecutarBash "export PATH=/mingw64/bin:`$PATH; cd '$msysFontes' && make -j$([Environment]::ProcessorCount)"

New-Item -ItemType Directory -Path $destino -Force | Out-Null
Copy-Item (Join-Path $fontes.FullName "ffmpeg.exe") $destino -Force
Copy-Item (Join-Path $fontes.FullName "ffprobe.exe") $destino -Force

Write-Host ""
Write-Host "Pronto: $destino" -ForegroundColor Green
ExecutarBash "'$(CaminhoMsys (Join-Path $destino 'ffmpeg.exe'))' -hide_banner -h encoder=dxv | grep -E 'dxt[15]'"

Write-Host ""
Write-Host "Aponte Configuracoes -> caminho do ffmpeg para essa pasta." -ForegroundColor Yellow
Write-Host "O preset 'DXV3 (alpha)' se habilita sozinho: o app consulta o encoder em tempo de execucao."
