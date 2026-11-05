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

## Gerar e publicar uma release

```powershell
# 1) empacota (portatil, com o ffmpeg que tem DXV3 alpha e NNEDI)
.\scripts\publish.ps1 -Versao 0.1.1 -IncluirFfmpeg

# 2) publica no GitHub usando o token do cofre do Windows
.\scripts\publish.ps1 -Versao 0.1.1 -IncluirFfmpeg -Publicar
```

Saída em `.\releases`: `CVExplorer-win-Portable.zip` (é o que se distribui — extrair e rodar,
sem instalador), `CVExplorer-<versao>-full.nupkg`, `releases.win.json` e `RELEASES` (feed que o
app lê para se atualizar).

`-IncluirFfmpeg` copia `.tools\ffmpeg-dxv\bin` para `publish\ffmpeg`; sem a flag o pacote sai
leve e o app procura o ffmpeg no PATH. Com `-Publicar` o script ainda:

- cria a tag `v<versao>` e a release publicada (pelo `vpk upload github`);
- escreve as notas de [.github/notas-release.md](.github/notas-release.md);
- anexa `ffmpeg-<sha>-fonte.tar.gz`, o snapshot do FFmpeg que gerou o binário distribuído
  (exigência da GPL — o commit usado fica em `-FfmpegRef`, igual ao `FFMPEG_REF` do workflow).

O app verifica essa release sozinho (`Velopack` + `GithubSource`) e mostra o botão **Atualizar**
quando há versão nova. **Verificar atualizações vem ligado por padrão**
(`VerificarAtualizacoes = true`); desligar é uma escolha em Configurações, não o contrário. Para
procurar na hora, o ícone de **seta circular ao lado da engrenagem** faz a mesma verificação do menu
(baixa, aplica e reinicia), com o ícone girando enquanto procura.

> O workflow [.github/workflows/release.yml](.github/workflows/release.yml) faz o mesmo numa
> máquina limpa, mas só roda por `workflow_dispatch` (Actions ▸ release ▸ Run workflow). Ele não
> tem gatilho por tag de propósito: a tag criada pelo `vpk upload` republicaria a release por
> cima, e o build do runner (que compila o ffmpeg do zero, com cache) não é o mesmo da máquina
> local.

### Criar o repositório e o primeiro push

```powershell
$env:GITHUB_TOKEN = "ghp_..."      # token com escopo "repo"
.\scripts\publicar-repo.ps1 -Nome CVExplorer -Visibilidade private
```

O script consulta a conta autenticada, cria o repositório se ainda não existir, aponta o `origin`,
faz o push e troca o `RepositorioUrl` padrão do `.csproj` para o repositório real (é de lá que o app
lê as atualizações). O token não é gravado: vai num credential store temporário que o script apaga.

Sem token, o caminho manual é o de sempre:

```powershell
git remote add origin https://github.com/SEU-USUARIO/CVExplorer.git
git push -u origin HEAD
```

---

## Funcionalidades
### Barra lateral

No estilo do Explorer, sem lista de projetos:

- **Favoritos**: pastas e arquivos que você fixa. Arraste itens da lista para cá (ou use o `+`) e
  remova pelo menu de contexto — ou pela **lixeira vermelha** que aparece na linha ao passar o
  mouse. Pasta abre na navegação; arquivo abre no app padrão. Ficam em `config.json` → `Favoritos`.
- **Este Computador**: unidades com **letra**, rótulo, espaço livre, **ícone** (disco empilhado em
  **amarelo** para local, globo em azul para rede) e o **tipo** (`Rede` para mapeadas/UNC, `Local`
  para fixas). Pastas adicionadas em *Raízes* aparecem como pastas. Antes delas vêm as pastas do
  perfil com ícone e **cor próprios** (nenhuma amarela, que é das pastas, nem azul, que é seleção):
  **Pessoal** violeta, **Área de Trabalho** verde, **Documentos** terracota, **Downloads** turquesa,
  **Imagens** rosa e **Vídeos** oliva.
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
- **Definir projeto na pasta atual**: pelo próprio chip, ou com o botão direito em qualquer pasta
  → **Definir essa pasta como projeto…** (também no vazio da grade, valendo para a pasta aberta),
  sem mover nem criar nada — só grava o marcador. Também funciona em pasta já existente com
  estrutura antiga.
- **Criar projeto**: cria a pasta, grava o marcador e monta **somente as pastas marcadas**
  na árvore do modelo. Nada é duplicado, nada é sobrescrito — rodar duas vezes não cria nada novo.
- **Auto-detecção opcional**: raízes configuradas são varridas em busca de `.cvproj` e de
  estruturas antigas reconhecíveis (`PACOTE_GRAFICO`, `CHAMADAS`, `ELEMENTOS`, `PROJETOS`…),
  marcadas como *sem marcador*. É complemento, não requisito.
- **Modelo editável**: a árvore padrão fica em `%LOCALAPPDATA%\CVExplorer\modelo-projeto.json` — é o
  único lugar onde a estrutura das pastas é definida (nada disso é fixo no código). Editar o arquivo
  vale na hora, sem reiniciar o app. O token `{QUADRO}` é substituído pelos nomes informados
  (ex.: `CHAMADA 01`, `CHAMADA 02`).

### Explorador
- Grade/lista com renderização virtualizada (pastas com milhares de itens continuam fluidas).
- **Barra de título própria**: a moldura do Windows sai de cena e entra uma faixa fina (30 px) com o
  logo, o nome do app e minimizar/maximizar/fechar. Arrasta pela faixa, duplo clique maximiza, e as
  bordas da janela continuam redimensionáveis por `WM_NCHITTEST` — maximizada respeita a barra de
  tarefas (`WM_GETMINMAXINFO` na área de trabalho do monitor).
- **Seleção em azul-claro e hover com contorno**: item selecionado usa o acento (`rgba(90,169,230,.26)`
  + contorno interno), não cinza; o hover clareia o fundo e desenha o mesmo contorno, na grade e na lista.
- **Seleção por laço**: arraste no vazio para marcar vários itens de uma vez (com `Ctrl` soma à
  seleção atual). A caixa respeita a área visível e ignora o que está fora dela.
- **Barra de endereço editável**: clique duplo no caminho (ou `Ctrl+L`) para digitar, colar ou
  copiar um caminho; `Enter` vai para a pasta, `Esc` cancela. Clicar nas migalhas também navega.
- **Type-ahead**: comece a digitar e a seleção pula para o item que combina (com indicador na tela) — e a grade/lista **rola até ele**, mesmo com milhares de itens.
- **Colunas ordenáveis**: clique em *Nome*, *Tipo*, *Tamanho*, *Modificado* ou *Criado em* para
  ordenar; clique de novo inverte. A escolha é lembrada.
- **Colunas redimensionáveis**: as **cinco** colunas têm alça — arraste a divisória no cabeçalho
  (inclusive *Nome*, que é a mais larga). Duplo clique na alça volta ao padrão. O cabeçalho
  compensa a barra de rolagem (`--sbw`), então a divisória fica exatamente sobre a coluna dos itens.
- **Abas**: cada aba guarda pasta, histórico e seleção. `Ctrl+T` abre, `Ctrl+W` fecha,
  `Ctrl+Tab`/`Ctrl+Shift+Tab` andam entre elas; o **+** da barra abre outra. `Ctrl+clique` soma ou
  tira uma aba da seleção e `Shift+clique` seleciona o intervalo. Arrastar as abas dentro da barra
  **reordena na hora**, com a etiqueta do que está sendo levado colada no cursor. **Saindo da barra**
  (para a grade, para outra janela ou para fora do app) a janela nova nasce já ali, embaixo do
  cursor, com as abas; soltar sobre a barra de outra janela junta as abas lá. Arrastar a **barra de
  título** de uma janela sobre a barra de abas de outra funde as duas (a janela arrastada some e as
  abas dela entram na outra).
- **Pastas maiores**: clique com o botão direito numa pasta → **Pastas maiores…** abre uma janela
  com o tamanho de cada subpasta direta, do maior para o menor (barra, tamanho, % do total e
  contagem de arquivos). A varredura roda em paralelo no host; duplo clique numa linha abre aquela
  pasta numa janela nova.
- **Janelas múltiplas**: `Ctrl+N` (ou o ícone no topo) abre outra janela na pasta atual, com
  histórico e seleção independentes. Configuração, miniaturas e projetos são compartilhados.
- **Arrastar entre janelas**: arraste itens de uma janela e solte em outra — a janela de destino
  mostra *"Solte para mover aqui"* (ou *copiar*, com `Ctrl`) e a operação roda com progresso.
  Também move/copia para uma pasta solta dentro da mesma janela. Como cada WebView2 captura o
  mouse, quem acompanha o cursor é o host (`GetCursorPos`), não o navegador.
- Navegação por migalhas, histórico, teclado (`Enter`, `Backspace`, `F2`, `Delete`, setas, `Ctrl+A`).
- Copiar / recortar / colar, arrastar do Explorer, conflitos com decisão explícita
  (*substituir / manter ambos / pular / cancelar*, com "aplicar a todos").
- Cópia progressiva com cancelamento; nomes nunca colidem (`arquivo (2).ext`). O card de progresso
  desaparece ao terminar — operação encerrada não pode ser "ressuscitada" por um evento atrasado do
  host (era o que deixava *"Movendo 1 item"* parado em 100%).
- Exclusão vai para a **Lixeira**.
- **Arquivos compactados abrem como pasta**: duplo clique num `.zip`, `.7z`, `.rar`, `.tar`, `.gz`,
  `.bz2`, `.xz` (e afins) lista o conteúdo como se fosse uma pasta, com migalhas, ordenação e
  colunas. Dá para **copiar de dentro** (o host extrai para um temporário e o resto do app segue
  trabalhando com arquivos de verdade) e clicar com o botão direito em **Extrair aqui** ou
  **Extrair em pasta…** (pergunta o nome e cria ao lado do arquivo). Dentro do compactado é
  somente leitura: criar, renomear, recortar e excluir ficam desligados, com aviso claro. O
  arquivo compactado usa ícone próprio (caixa amarela), não a miniatura do Windows.
- **Prévia rápida com a barra de espaço**: `Espaço` abre o item selecionado (ou o último, se houver
  vários) numa janela mínima, só com o nome e a mídia. Imagem, vídeo (sempre **em loop**) e **modelo
  3D** — no 3D, arrastar com o **esquerdo orbita**, com o **meio desloca** e com o **direito (ou a
  roda) aproxima**. A janela é reaproveitada: apertar espaço noutro item troca o conteúdo em vez de
  abrir outra janela.
- **Ambiente e tone mapping na prévia 3D**: presets de HDRI **gerados na hora** (Estúdio, Pôr do sol,
  Noite, Neve — nada é baixado), **Carregar HDRI…** para um `.hdr`/`.exr` do usuário e o botão
  **Ver como ambiente**, que abre a imagem atual como panorama lat-long (equiretangular) com a câmera
  dentro dele. A cena renderiza com **ACES** e **exposição** ajustável, então HDR aparece como HDR em
  vez de estourar.
- **Pré-visualização lateral sempre no lugar**: o painel abre junto com o app e fica visível (com
  estado vazio) até você recolher — pelo **⟩** do painel ou pelo botão no topo. Nada de abrir/fechar
  sozinho: era isso que roubava largura da grade e fazia o duplo clique acertar outro arquivo
  (a linha se mexia embaixo do cursor). Mostra imagem em tamanho real (PSD, EXR, TIFF, SVG…),
  player de vídeo (com o quadro centralizado na vertical) e metadados: tamanho, dimensões, duração,
  bitrate, **codec**, **entrelaçado (sim/não)**, **canal alpha (sim/não)**, modificado, criado em,
  tipo e caminho. A largura é arrastável (divisória à esquerda; duplo clique volta ao padrão) e o
  estado é lembrado.
- **Slider de miniaturas no rodapé**: controla o tamanho das telhas da grade ao vivo (96–340 px),
  e o valor fica salvo em `TamanhoGrade`.
- **Menu "Novo" nas pastas**: ao clicar com o botão direito numa pasta — ou na área vazia — o submenu
  **Novo ▸** lista as subpastas que o *modelo do projeto* prevê para aquele lugar: na raiz de um
  projeto (pasta com `.cvproj`) são as de primeiro nível (`PACOTE_GRAFICO`, `CHAMADAS`, `ABERTURA`,
  `EDITORIA`, `SAIDA`, `REFERENCIAS`); dentro de uma pasta do modelo são as filhas dela; e dentro de um
  **quadro** são as do token `{QUADRO}`. O que já existe aparece como *"já existe"* e desabilitado,
  então não há duplicata. Sempre há também *Pasta em branco…* e *Novo quadro…* — este último pede o
  nome e abre a árvore do quadro com **só PROJETOS, SAIDA e ELEMENTOS marcados** (o padrão); o resto
  (`REFERENCIAS`, subpastas de `PROJETOS`/`ELEMENTOS`…) só nasce se for marcado. Pode ser usado em
  qualquer pasta, não só dentro de `EDITORIA`.
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
| **.blend** | **miniatura embutida no próprio arquivo** (bloco `TEST`), lida antes do Shell — instantânea, sem abrir o Blender. Funciona com arquivo comprimido (zstd, que é o padrão ao salvar) e com o cabeçalho novo do Blender 5 (`BLENDER17-01v0502`), por isso a leitura é por **varredura validada** do bloco, não por posição fixa |
| **OBJ, FBX, GLB, GLTF, STL, USDZ, DAE** | **render próprio em 3D** (three.js em Web Worker + WebGL), câmera em 3/4 |
| **PLY** | malha pelo `PLYLoader`; se o arquivo for de **Gaussian Splats** (`f_dc_`, `opacity`, `scale_`), o app desenha os splats com shader próprio (cor pelos coeficientes de SH, opacidade com queda radial e tamanho pela escala) |
| **USD (.usd/.usda/.usdc) e Alembic (.abc)** | não têm loader no navegador: o app chama o **Blender instalado** uma vez (`-b --factory-startup`), converte para GLB num cache em `%LOCALAPPDATA%\CVExplorer\modelos` e serve o GLB. Sem Blender, fica o ícone do tipo |
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
| `.glb/.obj/.stl/.usdz` | **Web Workers** com OffscreenCanvas | 2–3 workers (`hardwareConcurrency - 2`) || `.ai/.pdf` | pdf.js | 3 por vez |
| `.fbx` | thread da interface (FBXLoader usa `document` para texturas) | 1 por vez |
| `.dae` | thread da interface (ColladaLoader é carregado sob demanda, com o TGALoader junto) | 1 por vez |

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

**Primeiro caminho: tocar o original.** Quando o Chromium do WebView2 decodifica o codec — H.264
(8 bits), AV1, VP8/VP9 em `.mp4`, `.m4v`, `.mov` ou `.webm`, com áudio AAC/MP3/Opus/Vorbis/FLAC —
o `<video>` recebe o **arquivo original** por HTTP em `127.0.0.1`, com suporte a `Range`. Quem
decodifica é o próprio navegador: **nenhum processo de ffmpeg** é aberto, o arquivo começa na hora e
o seek é **instantâneo**, inclusive com o vídeo pausado (a barra pula e continua pausado).

**Segundo caminho: ffmpeg em fluxo.** MXF, ProRes, DNxHR, DXV, R3D e afins não existem no Chromium,
então o ffmpeg decodifica e entrega um fluxo H.264 fragmentado pelo mesmo servidor loopback (com
token aleatório na URL):

- **Nada é gravado em disco** e nada é esperado: o ffmpeg começa a decodificar e o player toca em
  ~1 segundo, mesmo em arquivo de 300 MB.
- **O fluxo é all-intra** (`-g 1 -bf 0`): todo quadro é keyframe. Isso custa bitrate — que aqui é
  loopback — e compra o que importa: dentro do trecho já baixado o `<video>` pula para qualquer
  instante sem esperar GOP, e um fluxo reaberto mostra o primeiro quadro na hora.
- **Scrub em tempo real**: arrastar a barra atualiza a imagem a cada movimento. No arquivo que o
  navegador toca, é seek nativo; no fluxo do ffmpeg, o ponto já baixado é seek nativo no `<video>`
  (o fluxo é all-intra) e, fora do trecho baixado, aparece um **quadro extraído na hora** por cima
  do vídeo — um pedido por vez, sempre o mais recente. O fluxo só é reiniciado quando você solta a
  barra, então nada de reabrir ffmpeg a cada movimento.
- Soltar a barra reinicia o fluxo no ponto escolhido (`-ss` antes de `-i`, busca por keyframe) e o
  fluxo novo entra no mesmo `<video>`, sem dar play sozinho. A barra de tempo usa a duração real do
  `ffprobe`.
- **Controles próprios** (play/pause, linha do tempo, tempo, abrir no player externo) porque um
  fluxo contínuo não tem duração de arquivo. A **prévia rápida** (barra de espaço) usa esses mesmos
  controles quando o vídeo vem em fluxo: com o controle nativo, a duração é desconhecida, o
  navegador desenha a barra como *transmissão ao vivo* e o cabeçote fica pulando de um lado para o
  outro. Nos arquivos que o navegador decodifica sozinho, os controles nativos continuam (duração
  exata e seek nativo).
- **Loop que não quebra o fluxo**: no fluxo o `<video>` não usa `loop` — a URL é de uso único e
  relê-la daria erro de rede e vídeo parado. Ao terminar, o app pede um fluxo novo do zero. No
  arquivo original o loop é o do próprio navegador.
- **Tropeço no fluxo não derruba a prévia**: se o `<video>` der erro (acontece ao pular para o
  extremo do fim de um fluxo já encerrado), o app reabre no ponto pedido — até duas vezes — antes de
  mostrar a falha.
- **Som**: a prévia lateral tem botão de mudo (o estado fica guardado entre vídeos) e, nos dois
  players — lateral e prévia rápida —, um **seletor de saída de áudio** com as caixas instaladas no
  computador (`setSinkId`). Na prévia rápida o seletor fica na linha do nome do arquivo, nunca por
  cima da imagem. A escolha vale para os vídeos seguintes; para o Chromium listar as saídas, o
  WebView2 recebe permissão de microfone para a própria interface — o microfone nunca é aberto, a
  permissão só destrava a lista de dispositivos.
- O processo do ffmpeg morre ao trocar de arquivo, ao fechar a prévia e ao fechar a janela — e o
  app **espera** o processo sair antes de seguir, para o arquivo não ficar travado.

O servidor de mídia abre o arquivo com `FileShare.ReadWrite | Delete`: mover ou apagar um vídeo que
está na prévia funciona (o app também encerra a prévia e as miniaturas em geração antes de mexer no
arquivo).

Encoder do fluxo: o primeiro H.264 disponível (`libx264` → `h264_nvenc` → `h264_qsv` →
`h264_amf` → `h264_mf` → `libopenh264`), cada um com o **seu** preset — passar `-preset speed` para
NVENC, por exemplo, faz o encoder recusar abrir o fluxo, e aí o app tenta o próximo. Antes de
desistir, ele ainda repete a lista com ajustes conservadores: uma opção que o ffmpeg do usuário não
aceite (aconteceu com `-rate_control cbr_lowdelay`, que só existe em algumas versões do `h264_mf`)
degradaria o scrub, não o player inteiro.

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
| MP4 | H.264 — automático | encoder H.264 disponível, bitrate acompanha a origem | `.mp4` | AAC 320 kbps |
| MP4 | H.264 — 50 Mbps | mesmo encoder, **CBR 50 Mbps** (`-minrate/-maxrate/-bufsize`) | `.mp4` | AAC 320 kbps |
| MP4 | H.264 leve (WhatsApp) | **CBR 4 Mbps**, largura máxima 1920 px, `+faststart` | `.mp4` | AAC 128 kbps |

O preset *leve* existe para tirar o arquivo do estúdio: 4 Mbps com 1920 px de teto cabe nos limites
de anexo do WhatsApp sem virar sopa de blocos, mantém 30/60 fps e o áudio em 128 kbps. `-vf
scale='min(1920\,iw)':-2` só reduz o que passa de 1920 de largura (altura sempre par) — nada de
upscale.

Com **vários vídeos selecionados** o mesmo preset vale para todos: o menu mostra *Converter N
arquivos…* e a conversão roda em sequência com um card só (o original de cada um é preservado).

Detalhes que valem registro: `.mxf` recebe `-f mxf` e **não** recebe `-movflags`; XDCAM usa
`-b:v/-minrate/-maxrate 50M` com `-bufsize 17825792 -g 12 -bf 2` (é o que emissora espera, e o
bitrate é fixo — não acompanha resolução); H.264 resolve o encoder em tempo de execução
(`libx264` → `h264_nvenc` → `h264_qsv` → `h264_amf` → `h264_mf` → `libopenh264`). Preset escolhido à
mão nunca é trocado por falta de espaço — a escolha é do usuário; só o automático se ajusta. Se a
origem tem alpha e o preset não guarda (H.264, DNxHR, XDCAM, DXV3), o diálogo avisa que a
transparência será descartada e indica HAP Alpha ou ProRes 4444.

### Alpha em codec de playback (DXV3 e HAP)

DXV3 guarda alpha em texturas **DXT5**; DXT1 não. O encoder `dxv` do FFmpeg oficial só
implementa DXT1 — está assim no código-fonte (`dxvenc.c` tem `DXV_FMT_DXT1` como única opção) —,
mas o **decoder já entende DXT5** (na verdade DXT4: o alpha é armazenado pré-multiplicado).

Por isso o projeto traz um patch de ~80 linhas em
[`vendor/ffmpeg/0001-dxvenc-dxt5-alpha.patch`](./vendor/ffmpeg/0001-dxvenc-dxt5-alpha.patch) e um
script que compila o ffmpeg com ele:

```powershell
.\scripts\build-ffmpeg-dxv.ps1          # instala o toolchain em .tools\msys64 e compila
```

Ele instala MSYS2 **portátil** dentro de `.tools\msys64` (nada no sistema), baixa o FFmpeg,
aplica o patch, e deixa `ffmpeg.exe`/`ffprobe.exe` em `.tools\ffmpeg-dxv\bin`. Aponte
**Configurações → caminho do ffmpeg** para essa pasta. O resto do app não muda: o preset
"DXV3 (alpha)" se habilita sozinho, porque a capacidade é consultada em tempo de execução
(`ffmpeg -h encoder=dxv`, com cache, exposta em `app.info.ffmpeg.dxvAlpha`).

O que o patch faz: registra a opção `dxt5`, usa o compressor de blocos `dxt5_block` que o FFmpeg
já tem (é o mesmo do encoder DDS) com `tex_ratio = 16`, escreve um compressor de textura que
espelha o esquema do DXT1 em unidades de 4 dwords (alpha usa repetição/literal, cor usa
back-reference), e **pré-multiplica** o quadro antes de comprimir — exatamente o que o decoder
desfaz ao ler. Sem o patch o preset aparece desabilitado, com o motivo no tooltip.

Verificação feita com fonte ProRes 4444 com alpha (opaco à esquerda, 60/40% à direita):
saída `dxv`/tag `DXD3`, `pix_fmt rgba`, alpha **exato** em toda a amostragem (erro médio 0,00 e
máximo 0/255), RGB médio 2,7/255 de diferença (quantização DXT). Dimensão ímpar (1918×1078, fora
do múltiplo de 16 que o formato exige) também passa: alpha com erro máximo de 1/255.

Se algum dia o FFmpeg implementar DXT5 upstream, o build oficial passa a servir e o patch pode ser
jogado fora — a detecção em tempo de execução faz o preset funcionar nos dois casos. Vale mandar o
patch upstream: o decoder já suporta, então é uma lacuna clara.

### HAP Alpha (alternativa que não exige build)

`-c:v hap -format hap_alpha` (texturas DXT5) é playback em tempo real e o Resolume toca HAP
nativamente. Verificado ponta a ponta com a mesma fonte: saída `hap`/tag `Hap5`, `pix_fmt rgba`,
canal alpha com os mesmos níveis da origem.

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

### Desentrelaçamento automático

Origem entrelaçada (`field_order` tt/bb/tb/bt no `ffprobe`) é **desentrelaçada na conversão**, sem
pergunta: quadro entrelaçado em ProRes vira dois campos serrilhados no AE a cada movimento. O
diálogo avisa qual foi a origem e a cadeia de filtros vai junto com o resto (`-vf` único, combinado
com o ajuste de dimensão ímpar).

O filtro é escolhido por capacidade, em ordem de qualidade: `nnedi` → `bwdif` → `yadif`
(`deint=interlaced` só age em quadro marcado; `mode=send_frame` mantém o fps, então 25i vira 25p).

Sobre o **NNEDI**: o filtro do FFmpeg é **GPL** (`nnedi_filter_deps="gpl"`) e exige o arquivo
`nnedi3_weights.bin` (~13 MB, licença GPL). Por isso ele não existe em build LGPL nenhum — e é
justamente o build de `scripts\build-ffmpeg-dxv.ps1` que resolve: ele configura `--enable-gpl`,
baixa os pesos para `nnedi3_weights.bin` **ao lado do ffmpeg.exe** e o host roda o ffmpeg com o
diretório de trabalho nessa pasta, então o nome padrão do arquivo resolve sem escapar caminho em
`-vf`. Com um ffmpeg sem nnedi, o app cai para **bwdif** (motion-adaptive), que é o melhor nativo.

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

O app **não** embute o ffmpeg no próprio executável. Ele procura, nesta ordem:

1. o caminho definido em **Configurações**;
2. a pasta `ffmpeg\` ao lado do `CvExplorer.exe` (é onde o pacote portátil publica coloca o ffmpeg);
3. o `PATH` do Windows.

Use um build **LGPL** (ex.: [BtbN `lgpl`](https://github.com/BtbN/FFmpeg-Builds)). Os encoders
`prores_ks`, `dnxhd` e `cfhd` são nativos do FFmpeg, então **não é preciso build GPL**. O ffmpeg
roda como processo separado — seu binário não é incorporado ao app.

Única exceção: **DXV com alpha** não existe em nenhum build oficial (o encoder `dxv` do FFmpeg só
implementa DXT1). Para isso existe `scripts\build-ffmpeg-dxv.ps1`, que compila um ffmpeg com o
patch de [`vendor/ffmpeg`](./vendor/ffmpeg) — ver [Alpha em codec de playback](#alpha-em-codec-de-playback-dxv3-e-hap).
Sem ele o app continua funcionando e o preset apenas fica indisponível.

Esse build é **GPL v2+** (o filtro `nnedi` de desentrelaçamento é GPL e entra junto), e é por isso que
ele fica em `.tools\ffmpeg-dxv\` e não dentro do app: o ffmpeg continua sendo um processo separado,
que o CV Explorer apenas executa. As releases publicadas com `-IncluirFfmpeg` levam esse build em
`ffmpeg\`, junto do snapshot de fonte do mesmo commit do FFmpeg (exigência da GPL).

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
