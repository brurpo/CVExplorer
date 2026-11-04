# CV Explorer

Explorador de arquivos e gerenciador de projetos para produção audiovisual.
Windows, tema escuro, interface em português, atualização automática pelo GitHub.

Feito para o fluxo de pós-produção: abrir projeto, criar só as pastas necessárias,
achar mídia rápido, ver miniatura de tudo (inclusive PSD, AI, SVG, EXR, Blender, OBJ/FBX/GLB)
e — ao colar um vídeo — entregar já em formato de edição para o After Effects.

---

## Requisitos

| Item | Detalhe |
|---|---|
| Sistema | Windows 10/11 (x64) |
| WebView2 | Runtime **Evergreen** (já vem no Windows 11; no 10 instale o *Evergreen WebView2 Runtime*) |
| .NET | Nada a instalar para o app publicado — o build é **self-contained** |
| ffmpeg | Necessário apenas para converter vídeo. Veja [Vídeo](#vídeo) |

## Rodar a partir do código

**Duplo clique em `CVExplorer.cmd`** na raiz — ele baixa o SDK na primeira vez, compila e abre.
Se já existir uma janela aberta, ela é fechada antes (o `.exe` fica travado durante o build).

Pelo terminal, se preferir:

```powershell
.\scripts\setup.ps1              # baixa o SDK .NET 10 para .tools\dotnet e restaura pacotes
.\scripts\run.ps1                # compila e abre o app
.\scripts\run.ps1 -Pasta "T:\..."  # abre já numa pasta
.\scripts\run.ps1 -SemBuild      # usa o build atual, sem recompilar
```

## Gerar uma release

```powershell
.\scripts\publish.ps1 -Versao 0.1.0 -Repositorio https://github.com/SEU-USUARIO/CVExplorer
```

Saída em `.\releases` (instalador `Setup.exe`, portátil e pacote de atualização).

Para publicar de verdade no seu GitHub, basta criar uma tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

O workflow [.github/workflows/release.yml](.github/workflows/release.yml) roda os testes,
compila, empacota com Velopack e publica a release. O app verifica essa release sozinho
(`Velopack` + `GithubSource`) e mostra o botão **Atualizar** quando há versão nova.

> Troque `SEU-USUARIO` em [AppInfo.cs](src/CVExplorer.Host/AppInfo.cs) — ou apenas use o
> workflow, que injeta a URL real do repositório no build (`-p:RepositorioUrl=...`).

---

## Funcionalidades

### Barra lateral

No estilo do Explorer, sem lista de projetos:

- **Favoritos**: pastas e arquivos que você fixa. Arraste itens da lista para cá (ou use o `+`) e
  remova pelo menu de contexto. Pasta abre na navegação; arquivo abre no app padrão. Ficam em
  `config.json` → `Favoritos`.
- **Este Computador**: unidades com **letra**, rótulo, espaço livre e o **tipo** (`Rede` para
  mapeadas/UNC, `Local` para fixas). Pastas adicionadas em *Raízes* aparecem como pastas.
- Ícone de pasta em **amarelo**: com o acervo em tons de cinza, a pasta desaparecia no meio das
  miniaturas coloridas.

### Projetos
- **Marcador de raiz**: arquivo oculto `.cvproj` (JSON) na raiz do projeto. É o que define,
  sem ambiguidade, que a pasta é um projeto.
- **Detecção pela pasta atual**: projetos ficam em qualquer lugar, então a cada navegação o app
  sobe pelos pais procurando o `.cvproj`. O chip no topo mostra **em qual projeto você está**
  (ou "Sem projeto") e, ao clicar, abre os atalhos das pastas reais daquele projeto —
  `ELEMENTOS`, `PREVIEW` e `PROJETOS` → **AE, PSD, AI, BLENDER, MAYA**, `EDITORIA` → quadros.
- **Recent projects**: os últimos 12 projetos visitados ficam guardados e alimentam o chip do topo
  quando você abre uma pasta sem marcador por perto. A barra lateral **não** lista projetos: com
  milhares deles, uma lista ali vira ruído — quem responde "onde estou" é a detecção pela pasta atual.
- **Definir projeto na pasta atual**: pelo próprio chip, sem mover nem criar nada — só grava o
  marcador. Também funciona em pasta já existente com estrutura antiga.
- **Criar projeto**: cria a pasta, grava o marcador e monta **somente as pastas marcadas**
  na árvore do modelo. Nada é duplicado, nada é sobrescrito — rodar duas vezes não cria nada novo.
- **Auto-detecção opcional**: raízes configuradas são varridas em busca de `.cvproj` e de
  estruturas antigas reconhecíveis (`PACOTE_GRAFICO`, `CHAMADAS`, `ELEMENTOS`, `PROJETOS`…),
  marcadas como *sem marcador*. É complemento, não requisito.
- **Modelo editável**: a árvore padrão fica em `%LOCALAPPDATA%\CVExplorer\modelo-projeto.json`.
  O token `{QUADRO}` é substituído pelos nomes informados (ex.: `CHAMADA 01`, `CHAMADA 02`).

### Explorador
- Grade/lista com renderização virtualizada (pastas com milhares de itens continuam fluidas).
- **Barra de endereço editável**: clique duplo no caminho (ou `Ctrl+L`) para digitar, colar ou
  copiar um caminho; `Enter` vai para a pasta, `Esc` cancela. Clicar nas migalhas também navega.
- **Type-ahead**: comece a digitar e a seleção pula para o item que combina (com indicador na tela).
- **Colunas ordenáveis**: clique em *Nome*, *Tipo*, *Tamanho*, *Modificado* ou *Criado em* para
  ordenar; clique de novo inverte. A escolha é lembrada.
- **Colunas redimensionáveis**: as **cinco** colunas têm alça — arraste a divisória no cabeçalho
  (inclusive *Nome*, que é a mais larga). Duplo clique na alça volta ao padrão. O cabeçalho
  compensa a barra de rolagem (`--sbw`), então a divisória fica exatamente sobre a coluna dos itens.
- **Janelas múltiplas**: `Ctrl+N` (ou o ícone no topo) abre outra janela na pasta atual, com
  histórico e seleção independentes. Configuração, miniaturas e projetos são compartilhados.
- **Arrastar entre janelas**: arraste itens de uma janela e solte em outra — a janela de destino
  mostra *"Solte para mover aqui"* (ou *copiar*, com `Ctrl`) e a operação roda com progresso.
  Também move/copia para uma pasta solta dentro da mesma janela. Como cada WebView2 captura o
  mouse, quem acompanha o cursor é o host (`GetCursorPos`), não o navegador.
- Navegação por migalhas, histórico, teclado (`Enter`, `Backspace`, `F2`, `Delete`, setas, `Ctrl+A`).
- Copiar / recortar / colar, arrastar do Explorer, conflitos com decisão explícita
  (*substituir / manter ambos / pular / cancelar*, com "aplicar a todos").
- Cópia progressiva com cancelamento; nomes nunca colidem (`arquivo (2).ext`).
- Exclusão vai para a **Lixeira**.
- **Pré-visualização lateral** com imagem em tamanho real (PSD, EXR, TIFF, SVG…), player de vídeo
  e metadados (tamanho, modificado, **criado em**, tipo, caminho). A largura do painel é arrastável
  (divisória à esquerda; duplo clique volta ao padrão) e a grade recalcula as colunas ao abrir/fechar —
  sem telha cortada na borda.
- Escalas de Windows são respeitadas: o app é *per monitor v2*, então arraste de coluna, divisória
  e cursor usam as mesmas unidades (px de CSS) em 100%, 150% ou 200%.

### Busca
- Busca **paralela** (workers configuráveis) — pensada para latência de rede (SMB), não só disco local.
- Resultados chegam em lotes, com contagem de pastas varridas e cancelamento imediato ao digitar de novo.
- Sintaxe: `ext:psd,ai`, `tipo:video|imagem|audio|3d|documento|projeto`, `nome:"shot 01"`, `pasta:"chamadas"`.

### Miniaturas

Ícones de tipo usam o acervo do BRP_CHROMA (`ui/img/tipos/*.png`, 256px, tons neutros) e
acompanham o tamanho da telha — antes eram 34 px fixos e ficavam minúsculos em tela com escala
alta. O selo de formato (PDF, AI, XML…) também escala junto e **continua visível depois que a
miniatura chega** (antes era apagado junto com o ícone).

| Formato | Como é gerada |
|---|---|
| JPG, PNG, WEBP, GIF, BMP, TIFF, TGA, HEIC, JXL | Shell do Windows → Magick.NET |
| **PSD / PSB** | handler do Photoshop (Shell) → Magick.NET (composição achatada) |
| **AI / PDF** | **pdf.js desenha a primeira página** (o `.ai` é um PDF por dentro); Shell como reserva |
| **SVG** | Magick.NET |
| **EXR / HDR** | Magick.NET (com ajuste de exposição para não sair preta) |
| **.blend** | **miniatura embutida no próprio arquivo** (bloco `TEST`), lida antes do Shell — instantânea, sem abrir o Blender |
| **OBJ, FBX, GLB, GLTF, STL, USDZ** | **render próprio em 3D** (three.js em Web Worker + WebGL), câmera em 3/4 |
| MP4, MOV, MXF, AVI, MKV… | frame extraído com ffmpeg → Shell |
| USD, USDA, USDC, ABC, MA, MB, 3DS, DAE… | ícone do tipo (sem loader no app) |

Cache em disco (`%LOCALAPPDATA%\CVExplorer\miniaturas`), chaveado por **caminho + tamanho + data
+ tamanho pedido + versão do pipeline** — quando o pipeline muda, o cache antigo é invalidado
sozinho em vez de servir miniatura velha.

### Como as miniaturas são paralelas

Tudo que decodifica roda fora da thread da interface, em três filas independentes:

| Etapa | Onde roda | Paralelismo |
|---|---|---|
| Decoders do host (Shell, Magick, .blend, ffmpeg) | pool de tarefas com `SemaphoreSlim` | `núcleos / 4`, entre **4 e 12** (era 6 no seu 32-núcleos; agora 8) |
| `.glb/.obj/.stl/.usdz` | **Web Workers** com OffscreenCanvas | 2–3 workers (`hardwareConcurrency - 2`) |
| `.ai/.pdf` | pdf.js | 3 por vez |
| `.fbx` | thread da interface (FBXLoader usa `document` para texturas) | 1 por vez |

Medido no app com um **GLB de 225 MB**: miniatura pronta em **1,57 s**, com o pior intervalo
entre quadros da interface em **19 ms** — ou seja, a janela não congela enquanto o modelo é
lido. Quatro modelos diferentes (FBX, OBJ, GLB) renderizados ao mesmo tempo: **640 ms** no total.

Requisições repetidas do mesmo arquivo são deduplicadas por chave (uma geração só, mesmo com a
grade pedindo duas vezes), e o arquivo 3D é servido ao renderizador por HTTP em loopback, sem
copiar bytes para a memória do app.

Bibliotecas embarcadas para desenhar miniaturas: **three.js** (MIT, ~1 MB em `ui/vendor/three`)
e **pdf.js** (Apache-2.0, ~1,6 MB em `ui/vendor/pdfjs`). Ambas ficam no repositório — nada de CDN,
então miniatura de 3D/PDF funciona offline.

---

## Vídeo

Regra central: **colar vídeo não copia o original.** O arquivo é transcodificado direto na pasta
de destino e é esse `.mov` que entra no projeto. O original permanece intacto onde estava.

Na hora de colar aparece uma confirmação — **Converter** ou **Colar original** — com formato de
destino e tamanho estimado. "Colar original" copia o arquivo como está (sem perda, sem economia).
A caixa *Não perguntar de novo* grava a escolha daquele momento em `config.json`
(`PerguntarAntesDeConverter`); a opção pode ser reativada em **Configurações**. Arquivos que já
estão em codec de edição (ProRes, DNxHR, CineForm…) não geram pergunta: são sempre copiados.

### Player embutido (prévia de vídeo)

O `<video>` do WebView2 é Chromium: não abre MXF, ProRes, DNxHR, R3D nem a maioria dos formatos de
câmera. E o player do Windows também não — é justamente o motivo de a prévia falhar. Então o vídeo
é **tocado pelo ffmpeg** e entregue ao `<video>` como fluxo H.264 fragmentado, por HTTP em
`127.0.0.1` (loopback, com token aleatório na URL):

- **Nada é gravado em disco** e nada é esperado: o ffmpeg começa a decodificar e o player toca em
  ~1 segundo, mesmo em arquivo de 300 MB.
- **Scrub** reinicia o ffmpeg no segundo pedido (`-ss` antes de `-i`, busca por keyframe) e o fluxo
  novo entra no mesmo `<video>` (≈2 s). A barra de tempo usa a duração real do `ffprobe`.
- Controles próprios (play/pause, linha do tempo, tempo, abrir no player externo) porque um fluxo
  contínuo não tem duração de arquivo.
- O processo do ffmpeg morre ao trocar de arquivo, ao fechar a prévia e ao fechar a janela.

Encoder: o primeiro H.264 disponível no ffmpeg do usuário (`libx264` → `h264_nvenc` → `h264_qsv` →
`h264_amf` → `h264_mf` → `libopenh264`), cada um com o **seu** preset — passar `-preset speed` para
NVENC, por exemplo, faz o encoder recusar abrir o fluxo, e aí o app tenta o próximo.

### Por que o arquivo cresce (e como evitar)

ProRes é **intra-frame**: cada quadro é comprimido sozinho. MXF/H.264 de câmera são **long-GOP**:
um quadro "cheio" a cada ~12 e os outros guardados como diferenças. É por isso que o mesmo material
ocupa muito mais em ProRes — e é exatamente esse custo que dá *scrub* instantâneo no After Effects.

Medição real (MXF XDCAM HD422, 1080p29.97, 45 s, 50 Mbps — 322,9 MB):

| Destino | Tamanho | vs MXF | vs ProRes HQ | Encode |
|---|---|---|---|---|
| ProRes 422 HQ (220 Mbps) | 1.206 MB | 3,7× | 100% | 25 s |
| **ProRes 422 LT (102 Mbps)** | **591 MB** | 1,8× | **49%** | 29 s |
| DNxHR LB (~45 Mbps) | 292 MB | **0,90×** | 24% | 12 s |
| ProRes 422 Proxy (38 Mbps) | 282 MB | 0,87× | 23% | 21 s |

Conclusão prática: para origem de ~50 Mbps em 8 bits, **ProRes 422 HQ é desperdício** — ele gasta
4,4× mais bits para guardar exatamente a mesma informação. O app não usa mais HQ como padrão.

### Escolha automática (a partir da origem)

O alvo é o menor perfil intra-frame que ainda entregue **~2× o bitrate de vídeo da origem**
(margem prática de transparência):

| Origem | Perfil escolhido |
|---|---|
| 8 bits, ≤ ~22 Mbps | DNxHR LB |
| 8 bits, ~22–50 Mbps (o caso do MXF) | **ProRes 422 LT** |
| 8 bits, ~50–73 Mbps | ProRes 422 |
| 8 bits, > ~73 Mbps | ProRes 422 HQ |
| 10 bits, HDR, alpha ou 4:4:4/RGB | ProRes 422 HQ / ProRes 4444 |
| Sem espaço em disco | cai para o menor perfil que cabe |

O diálogo de conversão mostra o tamanho estimado de **cada** perfil e a porcentagem em relação ao
arquivo atual, então a decisão de espaço é consciente. Perfis disponíveis: `Automático`,
`ProRes 422 Proxy/LT/422/HQ/4444` e `DNxHR LB/SQ/HQX`.

### Conversão manual (menu de contexto)

Clique com o botão direito em um vídeo → **Converter para ▸** e escolha o formato exato. Cada preset
define codec, container e áudio:

| Grupo | Preset | Codec | Container | Áudio |
|---|---|---|---|---|
| MOV | ProRes 422 | `prores_ks -profile:v 2` | `.mov` | PCM 24 bits |
| MOV | ProRes 4444 (alpha) | `prores_ks -profile:v 4 -alpha_bits 16` | `.mov` | PCM 24 bits |
| MOV | DNxHR SQ | `dnxhd -profile:v dnxhr_sq` | `.mov` | PCM 24 bits |
| MOV | DNxHR HQ | `dnxhd -profile:v dnxhr_hq` | `.mov` | PCM 24 bits |
| MOV | DXV3 | `dxv -format dxt1` | `.mov` | PCM 16 bits |
| MOV | DXV3 (alpha) | `dxv -format dxt5` | `.mov` | PCM 16 bits |
| MOV | HAP Alpha | `hap -format hap_alpha` | `.mov` | PCM 16 bits |
| MXF | DNxHR SQ / HQ | `dnxhd` | `.mxf` (OP1a) | PCM 24 bits |
| MXF | XDCAM HD422 50 Mbps | `mpeg2video` CBR 50 Mbps, 4:2:2 | `.mxf` (OP1a) | PCM 24 bits |
| MP4 | H.264 | encoder H.264 disponível, ~20 Mbps | `.mp4` | AAC 320 kbps |

Detalhes que valem registro: `.mxf` recebe `-f mxf` e **não** recebe `-movflags`; XDCAM usa
`-b:v/-minrate/-maxrate 50M` com `-bufsize 17825792 -g 12 -bf 2` (é o que emissora espera, e o
bitrate é fixo — não acompanha resolução); H.264 resolve o encoder em tempo de execução
(`libx264` → `h264_nvenc` → `h264_qsv` → `h264_amf` → `h264_mf` → `libopenh264`). Preset escolhido à
mão nunca é trocado por falta de espaço — a escolha é do usuário; só o automático se ajusta. Se a
origem tem alpha e o preset não guarda (H.264, DNxHR, XDCAM, DXV3), o diálogo avisa que a
transparência será descartada e indica HAP Alpha ou ProRes 4444.

### Alpha em codec de playback (DXV3 e HAP)

DXV3 guarda alpha em texturas **DXT5**; DXT1 não. O encoder `dxv` do FFmpeg, porém, só implementa
DXT1 — está assim no código-fonte do próprio FFmpeg (`dxvenc.c` tem `DXV_FMT_DXT1` como única opção,
sem DXT5/YG10), não é limitação do build LGPL empacotado. Conferido também no binário:
`ffmpeg -h encoder=dxv` lista apenas `dxt1`, e `-format dxt5` é recusado com *"Undefined constant"*.

Por isso o **app verifica a capacidade antes de oferecer**: `app.info` expõe `ffmpeg.dxvAlpha`
(consulta `-h encoder=dxv` uma vez, com cache). Sem suporte, a entrada **DXV3 (alpha)** aparece
desabilitada no submenu e no seletor com o motivo escrito — em vez de falhar no meio da conversão.
Se um dia o FFmpeg implementar DXT5, o preset passa a funcionar sem mudar código.

O caminho que **entrega alpha hoje** é o **HAP Alpha** (`-c:v hap -format hap_alpha`, texturas DXT5):
é playback em tempo real e o Resolume toca HAP nativamente. Verificado ponta a ponta com uma fonte
ProRes 4444 com gradiente de alpha: a saída sai `hap`/tag `Hap5`, `pix_fmt rgba`, e o canal alpha
decodificado mantém exatamente os mesmos níveis da origem (YMIN 40, YMAX 255).

### Sequência de imagens

Botão direito em qualquer quadro de uma sequência (PNG, JPG, TIF, EXR, DPX, TGA, BMP, HDR) →
**Converter sequência (N quadros)…**. O app reconhece os irmãos por *prefixo + numeração + extensão*
(mínimo de 3 quadros) e mostra quantos são, a faixa e o total em disco.

- **Furos na numeração não truncam o vídeo.** A entrada é uma lista do demuxer
  `concat` (`file '…'` + `duration 1/fps`), não o padrão `%04d` do image2 — que para no primeiro
  quadro faltante. Um still removido no meio simplesmente não entra.
- **O fps é escolha do usuário** (23.976 / 24 / 25 / 29.97 / 30 / 50 / 59.94 / 60): quadros soltos
  não carregam essa informação.
- Sem áudio (`-an`), e as tags de cor do primeiro quadro são ignoradas (PNG é RGB/"gbr", que o
  encoder recusa como `-colorspace`).
- Mesma tabela de presets do vídeo, e as imagens originais ficam intactas.

O app também preserva resolução, fps, **timecode**, tags de cor (primárias/transferência/matriz)
e áudio em PCM 24 bits/48 kHz; VFR (celular/tela) passa sem duplicar quadros. Avisa — sem fingir
que resolve — quando há **HDR10 estático** ou **Dolby Vision**: esses metadados não existem em ProRes.

> Para economizar mais que o próprio MXF mantendo intra-frame, use **DNxHR LB** no diálogo
> (ficou em 292 MB, abaixo dos 323 MB do MXF). É 8 bits, então não adicione headroom de cor.

### ProRes ou DNxHR no After Effects (Windows)?

Pergunta justa, e o senso comum de que "DNx é mais rápido no Windows" **não se sustenta em dado**.
O que existe, medido e verificado:

**1. Os dois decodificam nativamente.** Verificado nos binários desta máquina: o AE 2026 traz
`ProResOpt.dll` (decoder ProRes da própria Adobe) e `DNxSDK-vs2019.dll` (SDK oficial da Avid 3.0).
Não existe mais dependência de QuickTime 7 no Windows para nenhum dos dois.

**2. A velocidade de decode acompanha o bitrate, não o codec.** Medição local, mesmo MXF, só decode
(`-f null`), 1.350 quadros 1080p:

| Arquivo | Tamanho | Decode | × tempo real |
|---|---|---|---|
| MXF origem (MPEG-2 long-GOP, 50 Mbps) | 322,9 MB | 1,8 s | 5,8× |
| **DNxHR LB** (~43 Mbps) | 242,6 MB | 0,45 s | **99×** |
| **ProRes 422 LT** (~102 Mbps) | 541,4 MB | 0,74 s | 61× |
| ProRes 422 HQ (~220 Mbps) | 1.156,3 MB | 1,21 s | 37× |
| DNxHR HQX (~208 Mbps) | 1.181,3 MB | 1,16 s | 39× |

Leitura: **DNxHR HQX e ProRes 422 HQ empatam** (mesma faixa de bitrate), **LB é o mais rápido dos
quatro** e **ProRes LT fica no meio**. Codec não muda a ordem — o que manda é quantos bytes por
segundo precisam ser lidos e as mesmas tabelas de decodificação.

**3. A especificação oficial confirma esse empate.**

| Faixa | DNxHR | ProRes |
|---|---|---|
| Menor | LB ≈ 43 Mbps (29,97p) | 422 Proxy 45 Mbps |
| Média | SQ ≈ 138 Mbps | 422 147 Mbps |
| Alta | HQ ≈ 208 Mbps · **HQX ≈ 208 Mbps (12 bits)** | 422 HQ 220 Mbps |
| 4:4:4 | 444 ≈ 417 Mbps | 4444 330 Mbps |

Fontes: [Avid — DNx naming scheme and data rates](https://kb.avid.com/pkb/articles/en_US/Knowledge/Avid-DNx-naming-scheme-and-data-rates)
e [Apple ProRes White Paper](https://www.apple.com/final-cut-pro/docs/Apple_ProRes.pdf).

**4. Não existe benchmark de scrub dentro do After Effects** — nem da Adobe, nem da Puget (o
Puget Bench for AE fixa a saída em ProRes 422 e varia hardware, não codec). O dado público mais
próximo é do Premiere em 4K ([Puget](https://www.pugetsystems.com/labs/articles/premiere-pro-cc-2017-mac-pro-vs-pc-performance-904/)):
DNxHR HQ e ProRes 422 HQ tocam em resolução cheia; só ProRes 4444 cai para 1/2 — coerente com o
bitrate 1,5× maior dele.

**5. Onde DNxHR leva desvantagem real:** a Adobe **não lista DNxHR** na
[página oficial de formatos do AE](https://helpx.adobe.com/after-effects/desktop/get-started/supported-file-formats/supported-file-formats.html)
(lista "Apple ProRes codec — Import and Export" explicitamente). Funciona — o SDK da Avid está lá —
mas ProRes é o único caminho *documentado*. Em compensação, os bugs ativos hoje (2021→2025) são de
**ProRes 4444 com alpha** na exportação do AE/AME.

**Conclusão adotada pelo app:** o padrão automático continua **ProRes**, porque bitrate igual =
desempenho igual no AE e ProRes é o formato documentado (melhor para round-trip e entrega). Para
tamanho, os perfis que realmente economizam são **DNxHR LB** e **ProRes 422 Proxy**; **HQ/4444 não
têm ganho nenhum sobre fonte 8-bit** — só custam 4× o disco. Trocar para DNxHR é uma escolha de
tamanho, não de velocidade.

### ffmpeg

O app **não** embute o ffmpeg. Ele procura, nesta ordem:

1. o caminho definido em **Configurações**;
2. a pasta `ffmpeg\` ao lado do `CvExplorer.exe`;
3. o `PATH` do Windows.

Use um build **LGPL** (ex.: [BtbN `lgpl`](https://github.com/BtbN/FFmpeg-Builds)). Os encoders
`prores_ks`, `dnxhd` e `cfhd` são nativos do FFmpeg, então **não é preciso build GPL**. O ffmpeg
roda como processo separado — seu binário não é incorporado ao app.

---

## Onde ficam os dados

Tudo em `%LOCALAPPDATA%\CVExplorer`:

| Caminho | Conteúdo |
|---|---|
| `config.json` | preferências (raízes, perfil de vídeo, miniaturas, janela) |
| `modelo-projeto.json` | árvore de pastas padrão (editável) |
| `miniaturas\` | cache de miniaturas (WebP) |
| `conversoes.jsonl` | histórico do que foi convertido, quando e com qual perfil |
| `logs\app.log` | log de erros |
| `webview2\` | dados do WebView2 |

O app **não grava nada** ao lado do executável (que pode estar em `Program Files`).

---

## Estrutura do código

```
src/CVExplorer.Host/        host C# (.NET 10, WinForms + WebView2)
  App/                      caminhos, configuração
  Features/Projects/        marcador .cvproj, modelo de pastas, detecção
  Features/FileSystem/      listagem em lotes, cópia/movimentação, conflitos
  Features/Search/          parser da busca + varredura paralela
  Features/Thumbnails/      cadeia de miniaturas, leitor de .blend
  Features/Media/           ffprobe, escolha de perfil, comando ffmpeg, transcodificação,
                            sequência de imagens, seleção de encoder H.264
  Ipc/                      ponte tipada host ↔ interface
  Native/                   interop: shell, espaço em disco, caminhos longos
  Commands.cs               contrato de comandos expostos à interface
ui/                         interface: HTML + CSS + ES modules (sem build)
  i18n/pt-BR.json           todos os textos
tests/CVExplorer.Tests/     testes de domínio + integração com ffmpeg real
```

A interface é **JS puro com módulos ES** e `// @ts-check` — sem Node, sem bundler.
O GitHub Action compila apenas o C#.

## Testes

```powershell
.\scripts\setup.ps1
.\.tools\dotnet\dotnet.exe test tests\CVExplorer.Tests\CVExplorer.Tests.csproj
```

Cobrem: idempotência do modelo de pastas (nada duplicado), marcador de projeto, detecção,
nomes duplicados, parser da busca, escolha de perfil/estimativa de tamanho, linha de comando
do ffmpeg (todos os presets e a entrada em sequência), detecção de sequência de imagens,
leitura do `.blend`, e duas garantias que valem por tudo:

- **toda chave de texto usada no JS existe em `pt-BR.json`**;
- **todo comando chamado pela interface existe no host**.

Com `CVX_FFMPEG` apontando para um `ffmpeg.exe`, rodam também testes de integração reais que
verificam que colar vídeo produz **apenas** um `.mov` ProRes no destino.

## Limitações conhecidas

- **USD/USDA/USDC, ABC, MA/MB, 3DS, DAE**: sem loader no app; caem no ícone do tipo.
  `.usdz` é renderizado, mas só quando o pacote é `usda` — `.usdz` com crate binário (usdc)
  é recusado pelo three.js.
- **FBX de animação sem malha** (ex.: clipes do Mixamo) não tem o que enquadrar: fica o ícone.
- **Blender sem "Save Preview Images"**: o `.blend` não tem miniatura embutida e cai no ícone
  do Blender. Renderizar abrindo o Blender seria dezenas de segundos por arquivo.
- **Dolby Vision** e metadados estáticos HDR10 não sobrevivem à conversão para ProRes (limite do formato/ffmpeg).
- ProRes 4444 no ffmpeg aceita entrada de até 10 bits.
- O instalador e os binários **não são assinados**; o SmartScreen pode avisar na primeira execução.
