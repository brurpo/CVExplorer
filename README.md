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

```powershell
.\scripts\setup.ps1   # baixa o SDK .NET 10 para .tools\dotnet e restaura pacotes
.\scripts\run.ps1     # compila e abre o app
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

### Projetos
- **Marcador de raiz**: arquivo oculto `.cvproj` (JSON) na raiz do projeto. É o que define,
  sem ambiguidade, que a pasta é um projeto.
- **Detecção pela pasta atual**: projetos ficam em qualquer lugar, então a cada navegação o app
  sobe pelos pais procurando o `.cvproj`. O chip no topo mostra **em qual projeto você está**
  (ou "Sem projeto") e, ao clicar, abre os atalhos das pastas reais daquele projeto —
  `ELEMENTOS`, `PREVIEW` e `PROJETOS` → **AE, PSD, AI, BLENDER, MAYA**, `EDITORIA` → quadros.
- **Recent projects**: os últimos 12 projetos visitados aparecem na lateral, então você não
  depende de lembrar o caminho nem de configurar raízes.
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
- Navegação por migalhas, histórico, teclado (`Enter`, `Backspace`, `F2`, `Delete`, setas, `Ctrl+A`).
- Copiar / recortar / colar, arrastar do Explorer, conflitos com decisão explícita
  (*substituir / manter ambos / pular / cancelar*, com "aplicar a todos").
- Cópia progressiva com cancelamento; nomes nunca colidem (`arquivo (2).ext`).
- Exclusão vai para a **Lixeira**.
- Pré-visualização lateral com imagem em tamanho real, vídeo e metadados.

### Busca
- Busca **paralela** (workers configuráveis) — pensada para latência de rede (SMB), não só disco local.
- Resultados chegam em lotes, com contagem de pastas varridas e cancelamento imediato ao digitar de novo.
- Sintaxe: `ext:psd,ai`, `tipo:video|imagem|audio|3d|documento|projeto`, `nome:"shot 01"`, `pasta:"chamadas"`.

### Miniaturas

| Formato | Como é gerada |
|---|---|
| JPG, PNG, WEBP, GIF, BMP, TIFF, TGA, HEIC, JXL | Shell do Windows → Magick.NET |
| **PSD / PSB** | handler do Photoshop (Shell) → Magick.NET (composição achatada) |
| **AI** | handler do Illustrator (Shell) |
| **SVG** | Magick.NET |
| **EXR / HDR** | Magick.NET (com ajuste de exposição para não sair preta) |
| **.blend** | **miniatura embutida no próprio arquivo** (bloco `TEST`, 128×128, instantânea, sem abrir o Blender) |
| MP4, MOV, MXF, AVI, MKV… | frame extraído com ffmpeg → Shell |
| OBJ, FBX, GLB, GLTF, USD | ícone do tipo (ver [Limitações](#limitações)) |

Cache em disco (`%LOCALAPPDATA%\CVExplorer\miniaturas`), chaveado por caminho + tamanho + data:
o arquivo muda, a miniatura é refeita sozinha. Conversão para WebP, com limite de paralelismo
e timeout por item (handlers de shell podem travar em rede).

---

## Vídeo

Regra central: **colar vídeo não copia o original.** O arquivo é transcodificado direto na pasta
de destino e é esse `.mov` que entra no projeto. O original permanece intacto onde estava.

**Destino padrão: ProRes 422 HQ** (`prores_ks`, 10 bits, 4:2:2, `yuv422p10le`).

Por quê:
- ProRes é **intra-frame** — cada quadro decodifica sozinho. É isso que faz o *scrub* no
  After Effects ser rápido; H.264/H.265 são long-GOP e obrigam a decodificar cadeias inteiras.
- AE 2024/2026 no Windows têm decodificação ProRes nativa (`ProResOpt.dll`).
- Nenhuma GPU no Windows decodifica ProRes — e nos testes medidos a aceleração de hardware
  no ingest não trouxe ganho (o gargalo é o encoder, que é CPU). Por isso o app **não** liga
  flags de `-hwaccel`: seria risco sem benefício.
- Taxas: ~99 GB/h em 1080p29,97 e ~396 GB/h em UHD. O app estima o tamanho, confere o espaço
  livre e **rebaixa o perfil** (ProRes 422 → Proxy) quando não cabe, avisando na interface.

Escolha automática de perfil:

| Situação | Perfil |
|---|---|
| Padrão | ProRes 422 HQ |
| Origem com alpha, ou já 4:4:4/RGB (captura de tela, render, sequência PNG/EXR) | ProRes 4444 |
| 12 bits / pedido explícito | DNxHR HQX |
| Sem espaço em disco | ProRes 422 → Proxy |

O app também preserva resolução, fps, **timecode**, tags de cor (primárias/transferência/matriz)
e áudio em PCM 24 bits/48 kHz; VFR (celular/tela) passa sem duplicar quadros. Avisa — sem fingir
que resolve — quando há **HDR10 estático** ou **Dolby Vision**: esses metadados não existem em ProRes.

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
  Features/Media/           ffprobe, escolha de perfil, comando ffmpeg, transcodificação
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
do ffmpeg, leitura do `.blend`, e duas garantias que valem por tudo:

- **toda chave de texto usada no JS existe em `pt-BR.json`**;
- **todo comando chamado pela interface existe no host**.

Com `CVX_FFMPEG` apontando para um `ffmpeg.exe`, rodam também testes de integração reais que
verificam que colar vídeo produz **apenas** um `.mov` ProRes no destino.

## Limitações conhecidas

- **USD / .usdc**: sem loader confiável em C#; hoje cai no ícone do tipo. `.usdz` pode ser
  renderizado via three.js se adicionado no futuro.
- **Dolby Vision** e metadados estáticos HDR10 não sobrevivem à conversão para ProRes (limite do formato/ffmpeg).
- ProRes 4444 no ffmpeg aceita entrada de até 10 bits.
- O instalador e os binários **não são assinados**; o SmartScreen pode avisar na primeira execução.
