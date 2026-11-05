## Pacote portatil

**Nao precisa de instalacao**: extraia o zip e execute `CV Explorer.exe`.
A pasta pode ficar onde quiser (inclusive num pendrive) e o proprio app se atualiza ali mesmo,
sem passar por instalador.

### Incluido
- Aplicativo self-contained (.NET 10, win-x64) — nenhum runtime precisa ser instalado.
- `ffmpeg/ffmpeg.exe`, `ffprobe.exe` e `nnedi3_weights.bin` compilados para win64, com:
  - encoder **DXV3 em DXT5 (com alpha)** — patch proprio aplicado ao FFmpeg;
  - filtro **`nnedi`** (desentrelacamento por rede neural), com os pesos ao lado do binario;
  - `hap` (HAP/HAP Alpha), `h264_mf`, `libsnappy`, `zlib`, `mediafoundation`.

### Atualizacoes
O app consulta este repositorio em busca de versoes novas (**Configuracoes > Atualizacoes**,
ligado por padrao) e aplica a atualizacao no proprio pacote portatil, mantendo tudo na mesma pasta.

### Codigo do FFmpeg (GPL)
Este pacote distribui um binario do FFmpeg sob **GPLv2 ou superior**.
- Fonte exata usada no build: o commit listado no anexo `ffmpeg-*-fonte.tar.gz` deste release
  (snapshot identico ao compilado), tambem disponivel em
  <https://github.com/FFmpeg/FFmpeg>.
- Patch aplicado por cima do fonte: `vendor/ffmpeg/0001-dxvenc-dxt5-alpha.patch`.
- Receita de compilacao: `scripts/build-ffmpeg-dxv.ps1`; as flags de `configure` estao em
  `ffmpeg -buildconf`.
- Pesos do nnedi: <https://github.com/dubhater/vapoursynth-nnedi3> (GPL).
