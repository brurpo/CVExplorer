<#
  Gera src\CVExplorer.Host\app.ico a partir do logo.png da raiz.
  Uso:  .\scripts\gerar-icone.ps1
  Rode sempre que o logo mudar: o icone do executavel e o da barra de tarefas vem daqui.
#>
param(
    [string]$Origem = "",
    [string]$Destino = ""
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent

if (-not $Origem) { $Origem = Join-Path $raiz "logo.png" }
if (-not $Destino) { $Destino = Join-Path $raiz "src\CVExplorer.Host\app.ico" }

if (-not (Test-Path $Origem)) { throw "Logo nao encontrado: $Origem" }

Add-Type -AssemblyName System.Drawing

$tamanhos = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$base = [System.Drawing.Image]::FromFile((Resolve-Path $Origem))

function New-PngBytes([System.Drawing.Image]$imagem, [int]$lado) {
    $bmp = New-Object System.Drawing.Bitmap($lado, $lado, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($imagem, (New-Object System.Drawing.Rectangle(0, 0, $lado, $lado)))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $ms.ToArray()
}

function New-DibBytes([System.Drawing.Image]$imagem, [int]$lado) {
    $bmp = New-Object System.Drawing.Bitmap($lado, $lado, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($imagem, (New-Object System.Drawing.Rectangle(0, 0, $lado, $lado)))
    $g.Dispose()

    $dados = $bmp.LockBits(
        (New-Object System.Drawing.Rectangle(0, 0, $lado, $lado)),
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $linha = $lado * 4
    $pixels = New-Object byte[] ($linha * $lado)
    for ($y = 0; $y -lt $lado; $y++) {
        $destinoLinha = [IntPtr]::Add($dados.Scan0, ($lado - 1 - $y) * $dados.Stride)
        [System.Runtime.InteropServices.Marshal]::Copy($destinoLinha, $pixels, $y * $linha, $linha)
    }
    $bmp.UnlockBits($dados)
    $bmp.Dispose()

    $mascaraLinha = [int][Math]::Ceiling($lado / 8.0)
    if ($mascaraLinha % 4 -ne 0) { $mascaraLinha += 4 - ($mascaraLinha % 4) }
    $mascara = New-Object byte[] ($mascaraLinha * $lado)

    $ms = New-Object System.IO.MemoryStream
    $escritor = New-Object System.IO.BinaryWriter($ms)
    $escritor.Write([int]40)            # biSize
    $escritor.Write([int]$lado)         # biWidth
    $escritor.Write([int]($lado * 2))   # biHeight (XOR + AND)
    $escritor.Write([int16]1)           # biPlanes
    $escritor.Write([int16]32)          # biBitCount
    $escritor.Write([int]0)             # biCompression (BI_RGB)
    $escritor.Write([int]($pixels.Length + $mascara.Length))
    $escritor.Write([int]0); $escritor.Write([int]0)
    $escritor.Write([int]0); $escritor.Write([int]0)
    $escritor.Write([byte[]]$pixels)
    $escritor.Write([byte[]]$mascara)
    $escritor.Flush()
    $bytes = $ms.ToArray()
    $escritor.Dispose()
    return $bytes
}

$imagens = @()
foreach ($lado in $tamanhos) {
    if ($lado -ge 128) { $bytes = New-PngBytes $base $lado }
    else { $bytes = New-DibBytes $base $lado }
    $imagens += [pscustomobject]@{ Lado = $lado; Bytes = $bytes }
}
$base.Dispose()

$saida = New-Object System.IO.MemoryStream
$escritor = New-Object System.IO.BinaryWriter($saida)
$escritor.Write([int16]0)                  # reservado
$escritor.Write([int16]1)                  # tipo: icone
$escritor.Write([int16]$imagens.Count)

$deslocamento = 6 + 16 * $imagens.Count
foreach ($img in $imagens) {
    $dim = if ($img.Lado -ge 256) { 0 } else { $img.Lado }
    $escritor.Write([byte]$dim)
    $escritor.Write([byte]$dim)
    $escritor.Write([byte]0)                # cores da paleta
    $escritor.Write([byte]0)                # reservado
    $escritor.Write([int16]1)               # planos
    $escritor.Write([int16]32)              # bits por pixel
    $escritor.Write([int]$img.Bytes.Length)
    $escritor.Write([int]$deslocamento)
    $deslocamento += $img.Bytes.Length
}
foreach ($img in $imagens) { $escritor.Write([byte[]]$img.Bytes) }
$escritor.Flush()

[System.IO.File]::WriteAllBytes($Destino, $saida.ToArray())
$escritor.Dispose()

Write-Host ("app.ico gerado: {0} ({1:N0} bytes; {2} tamanhos)" -f $Destino, (Get-Item $Destino).Length, $imagens.Count) -ForegroundColor Green
