## Pacote portátil

**Não precisa de instalação**: extraia o zip e execute `CV Explorer.exe`.
A pasta pode ficar onde quiser (inclusive num pendrive) e o próprio app se atualiza ali mesmo,
sem passar por instalador.

### Incluído
- Aplicativo self-contained (.NET 10, win-x64) — nenhum runtime precisa ser instalado.
- `ffmpeg/ffmpeg.exe`, `ffprobe.exe` e `nnedi3_weights.bin` compilados para win64, com:
  - encoder **DXV3 em DXT5 (com alpha)** — patch próprio aplicado ao FFmpeg;
  - filtro **`nnedi`** (desentrelaçamento por rede neural), com os pesos ao lado do binário;
  - `hap` (HAP/HAP Alpha), `h264_mf`, `libsnappy`, `zlib`, `mediafoundation`.

### Atualizações
O app consulta este repositório em busca de versões novas (**Configurações ▸ Atualizações**,
ligado por padrão) e aplica a atualização no próprio pacote portátil, mantendo tudo na mesma pasta.

### Código do FFmpeg (GPL)
Este pacote distribui um binário do FFmpeg sob **GPLv2 ou superior**.
- Fonte exata usada no build: o snapshot anexado `ffmpeg-*-fonte.tar.gz` deste release
  (mesmo commit que gerou o binário), também disponível em <https://github.com/FFmpeg/FFmpeg>.
- Patch aplicado por cima do fonte: `vendor/ffmpeg/0001-dxvenc-dxt5-alpha.patch`.
- Receita de compilação: `scripts/build-ffmpeg-dxv.ps1`; as flags de `configure` estão em
  `ffmpeg -buildconf`.
- Pesos do nnedi: <https://github.com/dubhater/vapoursynth-nnedi3> (GPL).
