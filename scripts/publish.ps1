<#
  Gera o pacote portatil da release com Velopack (sem instalador).
  Uso:  .\scripts\publish.ps1 -Versao 0.1.0 [-IncluirFfmpeg [-Publicar]]
  Saida: .\releases  (win-Portable.zip + pacotes de atualizacao)

  -IncluirFfmpeg  copia .tools\ffmpeg-dxv\bin para dentro do pacote (DXV3 alpha,
                  HAP e NNEDI). Sem a flag o app procura o ffmpeg no PATH.
  -Publicar       envia para o GitHub (tag v<versao>) usando o token guardado no
                  cofre do Windows por scripts\token-github.ps1, escreve as notas de
                  .github\notas-release.md e anexa o fonte do ffmpeg (GPL).
#>
param(
    [Parameter(Mandatory = $true)][string]$Versao,
    [string]$Repositorio = "",
    [switch]$IncluirFfmpeg,
    [switch]$Publicar,
    # Commit do FFmpeg que o pacote leva. O fonte desse mesmo commit vai no release.
    [string]$FfmpegRef = "baa68e68b34e2de912eb7e94342cebd2545f05ed",
    # Token do GitHub. Vazio = le do cofre do Windows.
    [string]$Token = ""
)

$ErrorActionPreference = "Stop"
$raiz = Split-Path $PSScriptRoot -Parent
$dotnet = Join-Path $raiz ".tools\dotnet\dotnet.exe"

if (-not (Test-Path $dotnet)) {
    # Numa maquina limpa (CI) o SDK vem do PATH em vez de .tools.
    $noPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($noPath) { $dotnet = $noPath.Source }
    else { & (Join-Path $PSScriptRoot "setup.ps1") }
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

if (-not $Publicar) {
    Write-Host "Para publicar no GitHub:" -ForegroundColor Cyan
    Write-Host "  .\scripts\publish.ps1 -Versao $Versao -IncluirFfmpeg -Publicar"
    return
}

# ---------------------------------------------------------------- publicacao

function Get-TokenCofre {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CofreGitHub {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENCIAL {
        public uint Flags;
        public uint Tipo;
        public string Alvo;
        public string Comentario;
        public System.Runtime.InteropServices.ComTypes.FILETIME UltimoAcesso;
        public uint TamanhoCredencial;
        public IntPtr BlobCredencial;
        public uint Persistir;
        public uint ContagemAtributos;
        public IntPtr Atributos;
        public string AlvoAlias;
        public string NomeUtilizador;
    }
    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, int type, int flags, out IntPtr cred);
    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CredFree(IntPtr cred);
}
"@
    $ptr = [IntPtr]::Zero
    if (-not [CofreGitHub]::CredRead("git:https://github.com", 1, 0, [ref]$ptr)) { return $null }
    $cred = [System.Runtime.InteropServices.Marshal]::PtrToStructure($ptr, [type][CofreGitHub+CREDENCIAL])
    $valor = [System.Runtime.InteropServices.Marshal]::PtrToStringUni($cred.BlobCredencial, $cred.TamanhoCredencial / 2)
    [void][CofreGitHub]::CredFree($ptr)
    return $valor
}

if (-not $Repositorio) {
    $csproj = Get-Content (Join-Path $raiz "src\CVExplorer.Host\CVExplorer.Host.csproj") -Raw
    if ($csproj -match '(https://github\.com/[^<\s"]+)') { $Repositorio = $Matches[1] }
    else { throw "Informe -Repositorio https://github.com/usuario/repo." }
}
$Repositorio = $Repositorio.TrimEnd('/')

if (-not $Token) {
    $Token = Get-TokenCofre
    if (-not $Token) { throw "Token nao encontrado no cofre. Rode scripts\token-github.ps1." }
}

$tag = "v$Versao"
$cabecalhos = @{ Authorization = "Bearer $Token"; "User-Agent" = "cvexplorer"; Accept = "application/vnd.github+json" }

Write-Host "Enviando $tag para $Repositorio..." -ForegroundColor Cyan
# O vpk cria a release ja publicada (sem --merge: uma versao nova nunca sobrescreve outra).
& $vpk upload github --repoUrl $Repositorio --token $Token --publish `
    --releaseName "CV Explorer $Versao" --tag $tag --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "Falha no vpk upload (exit $LASTEXITCODE)." }

$api = "https://api.github.com/repos/" + ($Repositorio -replace '^https://github\.com/', '')
$release = Invoke-RestMethod -Uri "$api/releases/tags/$tag" -Headers $cabecalhos -TimeoutSec 60

Write-Host "Escrevendo as notas do release..." -ForegroundColor Cyan
$notas = [System.IO.File]::ReadAllText((Join-Path $raiz ".github\notas-release.md"), [System.Text.Encoding]::UTF8)
$corpo = @{ body = $notas } | ConvertTo-Json
Invoke-RestMethod -Method Patch -Uri "$api/releases/$($release.id)" -Headers $cabecalhos `
    -ContentType "application/json; charset=utf-8" `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($corpo)) -TimeoutSec 60 | Out-Null

if ($IncluirFfmpeg) {
    # GPL: o fonte que corresponde ao binario distribuido vai junto do release.
    Write-Host "Anexando o fonte do ffmpeg ($FfmpegRef)..." -ForegroundColor Cyan
    $nomeFonte = "ffmpeg-$($FfmpegRef.Substring(0, 7))-fonte.tar.gz"
    $arquivoFonte = Join-Path $releases $nomeFonte
    if (-not (Test-Path $arquivoFonte)) {
        $ProgressPreference = "SilentlyContinue"
        Invoke-WebRequest "https://github.com/FFmpeg/FFmpeg/archive/$FfmpegRef.tar.gz" `
            -OutFile $arquivoFonte -UseBasicParsing -TimeoutSec 1800
    }
    $urlAsset = ($release.upload_url -split '\{')[0] + "?name=$nomeFonte"
    Invoke-RestMethod -Method Post -Uri $urlAsset -Headers $cabecalhos `
        -ContentType "application/gzip" -InFile $arquivoFonte -TimeoutSec 1800 | Out-Null
}

$final = Invoke-RestMethod -Uri "$api/releases/tags/$tag" -Headers $cabecalhos -TimeoutSec 60
Write-Host "Publicado: $($final.html_url)" -ForegroundColor Green
$final.assets | ForEach-Object { "  - {0} ({1:N1} MB)" -f $_.name, ($_.size / 1MB) }
