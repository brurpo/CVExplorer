import { call } from './bridge.js';
import { t } from './i18n.js';
import { deBase64, paraBase64 } from './bytes.js';
import * as ui from './ui.js';

/**
 * PDF na prévia rápida (barra de espaço): pdf.js desenha a página e pdf-lib aplica as
 * edições. Editar não é reescrever texto: são operações de página (girar, mover, excluir)
 * e extração, sempre gravadas por cima do arquivo com um `.bak` ao lado.
 */
let pdfjs = null;
let pdfLib = null;

async function carregarPdfjs() {
  if (pdfjs) {
    return pdfjs;
  }

  const modulo = await import('../vendor/pdfjs/pdf.min.mjs');
  modulo.GlobalWorkerOptions.workerSrc = './vendor/pdfjs/pdf.worker.min.mjs';
  pdfjs = modulo;
  return pdfjs;
}

async function carregarPdfLib() {
  if (!pdfLib) {
    // UMD: o módulo se registra em globalThis.PDFLib.
    await import('../vendor/pdf-lib/pdf-lib.min.js');
    pdfLib = window.PDFLib;
  }

  if (!pdfLib) {
    throw new Error('biblioteca de edição de PDF não carregou');
  }

  return pdfLib;
}

let estado = null;

export function fechar() {
  if (!estado) return;

  try {
    estado.documento?.destroy();
  } catch {
    // já destruído
  }

  estado = null;
  const acoes = document.getElementById('previa-pdf-acoes');
  if (acoes) acoes.hidden = true;
}

export async function abrir(caminho) {
  fechar();

  const corpo = document.getElementById('previa-rapida-corpo');
  if (!corpo) return;

  const carregando = document.createElement('div');
  carregando.className = 'spinner';
  corpo.replaceChildren(carregando);

  const resposta = await call('pdf.dados', { caminho });
  if (!resposta?.ok) {
    throw new Error(resposta?.erro ?? t('pdf.erro', { motivo: '—' }));
  }

  const bytes = deBase64(resposta.dados);

  const area = document.createElement('div');
  area.className = 'pdf-area';

  const caixa = document.createElement('div');
  caixa.className = 'pdf-pagina-caixa';

  const canvas = document.createElement('canvas');
  canvas.className = 'pdf-canvas';

  const camadaTexto = document.createElement('div');
  camadaTexto.className = 'textLayer';

  const camadaAnotacoes = document.createElement('div');
  camadaAnotacoes.className = 'annotationLayer';

  caixa.append(canvas, camadaTexto, camadaAnotacoes);
  area.append(caixa);
  corpo.replaceChildren(area);

  estado = {
    caminho,
    original: bytes,
    bytes,
    sujo: false,
    documento: null,
    pagina: 1,
    total: 0,
    escala: 0, // 0 = ajustar à largura
    canvas,
    caixa,
    area,
    camadaTexto,
    camadaAnotacoes,
    valores: new Map(),
  };

  ligarBarra();
  await recarregar(1);
}

async function recarregar(pagina) {
  if (!estado) return;

  const lib = await carregarPdfjs();

  try {
    estado.documento?.destroy();
  } catch {
    // já destruído
  }

  // pdf.js toma posse do buffer: manda uma cópia para poder guardar o original.
  estado.documento = await lib.getDocument({
    data: estado.bytes.slice(),
    useSystemFonts: true,
    disableAutoFetch: true,
    disableStream: true,
  }).promise;

  estado.total = estado.documento.numPages;
  estado.pagina = Math.min(Math.max(1, pagina), Math.max(1, estado.total));

  await desenhar();
}

async function desenhar() {
  if (!estado?.documento) return;

  const pagina = await estado.documento.getPage(estado.pagina);
  const base = pagina.getViewport({ scale: 1 });

  const larguraArea = estado.area.clientWidth || 720;
  const larguraMaxima = Math.max(240, larguraArea - 32);
  const escala = estado.escala > 0
    ? estado.escala
    : Math.min(larguraMaxima / base.width, 2.5);

  estado.escalaAtual = escala;

  const viewport = pagina.getViewport({ scale: escala });
  const proporcao = window.devicePixelRatio || 1;

  estado.canvas.width = Math.max(1, Math.round(viewport.width * proporcao));
  estado.canvas.height = Math.max(1, Math.round(viewport.height * proporcao));
  estado.canvas.style.width = `${Math.round(viewport.width)}px`;
  estado.canvas.style.height = `${Math.round(viewport.height)}px`;
  estado.caixa.style.width = `${Math.round(viewport.width)}px`;
  estado.caixa.style.height = `${Math.round(viewport.height)}px`;

  const contexto = estado.canvas.getContext('2d', { alpha: false });
  contexto.setTransform(proporcao, 0, 0, proporcao, 0, 0);
  contexto.fillStyle = '#ffffff';
  contexto.fillRect(0, 0, viewport.width, viewport.height);

  await pagina.render({ canvasContext: contexto, viewport }).promise;

  await desenharCamadas(pagina, viewport);
  atualizarBarra();
}

/**
 * Camada de texto (selecionar/copiar) e de campos de formulário, por cima do desenho.
 *
 * Os campos são desenhados aqui mesmo: o `AnnotationStorage`/`AnnotationLayer` do pdf.js é
 * do pacote do *viewer* e não vem neste build — e para o pdf-lib o que interessa é o
 * `fieldName` de cada widget, que a API de anotações já entrega.
 */
async function desenharCamadas(pagina, viewport) {
  const lib = await carregarPdfjs();

  estado.camadaTexto.replaceChildren();
  estado.camadaAnotacoes.replaceChildren();

  try {
    const texto = new lib.TextLayer({
      textContentSource: pagina.streamTextContent(),
      container: estado.camadaTexto,
      viewport,
    });
    await texto.render();
  } catch {
    // sem camada de texto a página continua visível; só não dá para selecionar
  }

  let anotacoes = [];
  try {
    anotacoes = await pagina.getAnnotations();
  } catch {
    return;
  }

  for (const anotacao of anotacoes) {
    if (anotacao.subtype !== 'Widget' || !anotacao.fieldName || anotacao.readOnly) continue;
    if (anotacao.fieldType === 'Sig') continue;

    const [x1, y1, x2, y2] = viewport.convertToViewportRectangle(anotacao.rect);
    const esquerda = Math.min(x1, x2);
    const topo = Math.min(y1, y2);
    const largura = Math.max(8, Math.abs(x2 - x1));
    const altura = Math.max(8, Math.abs(y2 - y1));

    const salvo = estado.valores.get(anotacao.fieldName);
    const valor = salvo ? salvo.valor : anotacao.fieldValue;

    const campo = criarCampo(anotacao, valor);
    campo.style.left = `${esquerda}px`;
    campo.style.top = `${topo}px`;
    campo.style.width = `${largura}px`;
    campo.style.height = `${altura}px`;
    campo.style.fontSize = `${Math.max(9, Math.round(altura * 0.62))}px`;
    campo.dataset.campo = anotacao.fieldName;

    estado.camadaAnotacoes.appendChild(campo);
  }
}

function criarCampo(anotacao, valor) {
  let campo;

  if (anotacao.fieldType === 'Btn' && anotacao.checkBox) {
    campo = document.createElement('input');
    campo.type = 'checkbox';
    campo.checked = valor === true || valor === anotacao.exportValue || valor === 'On';
    campo.className = 'pdf-campo pdf-campo-marca';
  } else if (anotacao.fieldType === 'Ch' && Array.isArray(anotacao.options) && anotacao.options.length > 0) {
    campo = document.createElement('select');
    campo.className = 'pdf-campo';
    for (const opcao of anotacao.options) {
      const item = document.createElement('option');
      item.value = opcao.exportValue ?? opcao.displayValue ?? String(opcao);
      item.textContent = opcao.displayValue ?? opcao.exportValue ?? String(opcao);
      campo.appendChild(item);
    }
    if (typeof valor === 'string') campo.value = valor;
  } else {
    campo = document.createElement('input');
    campo.type = 'text';
    campo.className = 'pdf-campo';
    campo.value = typeof valor === 'string' ? valor : '';
  }

  campo.addEventListener('input', () => registrarValor(campo));
  campo.addEventListener('change', () => registrarValor(campo));
  return campo;
}

/** Guarda o que foi digitado: o valor precisa sobreviver à troca de página e ao salvar. */
function registrarValor(campo) {
  if (!estado) return;

  const nome = campo.dataset.campo;
  if (!nome) return;

  const valor = campo.type === 'checkbox' ? campo.checked : campo.value;
  estado.valores.set(nome, { valor, tipo: campo.type === 'checkbox' ? 'marca' : 'texto' });

  estado.sujo = true;
  atualizarBarra();
}

/**
 * Leva os campos preenchidos na tela para dentro do PDF (pdf-lib). Sem isso, girar uma
 * página jogaria fora o que o usuário digitou nos formulários.
 */
function aplicarCampos(documento) {
  if (!estado || estado.valores.size === 0) return;

  let formulario;
  try {
    formulario = documento.getForm();
  } catch {
    return;
  }

  for (const [nome, { valor, tipo }] of estado.valores) {
    let campo;
    try {
      campo = formulario.getField(nome);
    } catch {
      continue;
    }

    if (!campo) continue;

    try {
      if (tipo === 'marca' && typeof campo.check === 'function') {
        if (valor) campo.check();
        else campo.uncheck();
      } else if (typeof campo.setText === 'function') {
        campo.setText(String(valor ?? ''));
      } else if (typeof campo.select === 'function') {
        campo.select(String(valor ?? ''));
      }
    } catch {
      // campo somente leitura ou de tipo que o pdf-lib não reescreve
    }
  }

  try {
    formulario.updateFieldAppearances?.();
  } catch {
    // sem aparência atualizada o valor continua gravado
  }
}

/** Aplica uma edição com pdf-lib e recarrega a prévia com o resultado. */
async function editar(acao, paginaDepois) {
  if (!estado) return;

  const lib = await carregarPdfLib();
  const documento = await lib.PDFDocument.load(estado.bytes, { updateMetadata: false });

  aplicarCampos(documento);
  await acao(documento, lib);

  estado.bytes = await documento.save();
  estado.sujo = true;

  await recarregar(paginaDepois ?? estado.pagina);
}

async function girar() {
  await editar(async (documento, lib) => {
    const pagina = documento.getPage(estado.pagina - 1);
    const atual = pagina.getRotation().angle ?? 0;
    pagina.setRotation(lib.degrees((atual + 90) % 360));
  });
}

async function mover(direcao) {
  const destino = estado.pagina + direcao;
  if (!estado || destino < 1 || destino > estado.total) return;

  await editar(async (documento) => {
    const [pagina] = await documento.copyPages(documento, [estado.pagina - 1]);
    documento.removePage(estado.pagina - 1);
    documento.insertPage(destino - 1, pagina);
  }, destino);
}

async function excluir() {
  if (!estado || estado.total <= 1) {
    ui.toast(t('pdf.ultimaPagina'), 'aviso', 4000);
    return;
  }

  const pagina = estado.pagina;
  await editar((documento) => {
    documento.removePage(pagina - 1);
  }, Math.min(pagina, estado.total - 1));
}

async function extrair() {
  if (!estado) return;

  const lib = await carregarPdfLib();
  const origem = await lib.PDFDocument.load(estado.bytes);
  const novo = await lib.PDFDocument.create();
  const [copiada] = await novo.copyPages(origem, [estado.pagina - 1]);
  novo.addPage(copiada);

  const bytes = await novo.save();
  const destino = caminhoDaPagina(estado.caminho, estado.pagina);

  const resposta = await call('pdf.salvar', { caminho: destino, dados: paraBase64(bytes) });
  if (!resposta?.ok) {
    ui.toast(t('pdf.erro', { motivo: resposta?.erro ?? '—' }), 'erro', 6000);
    return;
  }

  ui.toast(t('pdf.extraido', { nome: destino.split(/[\\/]/).pop() }), 'ok', 6000);
}

async function salvar() {
  if (!estado?.sujo) return;

  // Campos preenchidos na tela entram no arquivo; sem isso o que foi digitado se perde.
  if (estado.valores.size > 0) {
    const lib = await carregarPdfLib();
    const documento = await lib.PDFDocument.load(estado.bytes, { updateMetadata: false });
    aplicarCampos(documento);
    estado.bytes = await documento.save();
  }

  const resposta = await call('pdf.salvar', {
    caminho: estado.caminho,
    dados: paraBase64(estado.bytes),
  });

  if (!resposta?.ok) {
    ui.toast(t('pdf.erro', { motivo: resposta?.erro ?? '—' }), 'erro', 6000);
    return;
  }

  estado.original = estado.bytes;
  estado.sujo = false;
  await recarregar(estado.pagina);
  ui.toast(t('pdf.salvo'), 'ok', 4000);
}

async function descartar() {
  if (!estado?.sujo) return;

  estado.bytes = estado.original;
  estado.sujo = false;
  await recarregar(estado.pagina);
}

async function irPara(pagina) {
  if (!estado) return;

  estado.pagina = Math.min(Math.max(1, pagina), Math.max(1, estado.total));
  await desenhar();
}

function caminhoDaPagina(caminho, pagina) {
  const barra = caminho.lastIndexOf('\\') >= 0 ? '\\' : '/';
  const pasta = caminho.slice(0, caminho.lastIndexOf(barra) + 1);
  const nome = caminho.slice(caminho.lastIndexOf(barra) + 1);
  const ponto = nome.toLowerCase().lastIndexOf('.pdf');
  const base = ponto > 0 ? nome.slice(0, ponto) : nome;
  return `${pasta}${base} - página ${pagina}.pdf`;
}

// ---- barra ---------------------------------------------------------------

let ligada = false;

const porId = (id) => document.getElementById(id);

function ligarBarra() {
  const acoes = porId('previa-pdf-acoes');
  if (!acoes) return;

  acoes.hidden = false;

  if (ligada) {
    atualizarBarra();
    return;
  }

  const cliques = [
    ['pdf-anterior', () => irPara((estado?.pagina ?? 1) - 1)],
    ['pdf-proxima', () => irPara((estado?.pagina ?? 1) + 1)],
    ['pdf-zoom-menos', () => mudarZoom(-0.25)],
    ['pdf-zoom-mais', () => mudarZoom(0.25)],
    ['pdf-ajustar', () => ajustar()],
    ['pdf-original', () => definirZoom(1)],
    ['pdf-girar', () => girar()],
    ['pdf-menu', (evento) => abrirMenuPaginas(evento.currentTarget)],
    ['pdf-salvar', () => salvar()],
    ['pdf-descartar', () => descartar()],
  ];

  for (const [id, acao] of cliques) {
    porId(id)?.addEventListener('click', (evento) => {
      Promise.resolve(acao(evento)).catch((erro) => {
        ui.toast(t('pdf.erro', { motivo: erro?.message ?? '—' }), 'erro', 6000);
      });
    });
  }

  // Botão direito: seleção, giro, ordem das páginas e extração no mesmo lugar.
  document.getElementById('previa-rapida-corpo')?.addEventListener('contextmenu', (evento) => {
    if (!estado) return;
    evento.preventDefault();
    abrirMenuPaginas(null, evento.clientX, evento.clientY);
  });

  document.addEventListener('keydown', atalhos);
  ligada = true;
  atualizarBarra();
}

/** Menu das operações de página — vale para o botão da barra e para o botão direito. */
function abrirMenuPaginas(botao, x, y) {
  if (!estado) return;

  const selecao = String(window.getSelection() ?? '').trim();
  const base = botao?.getBoundingClientRect();

  const itens = [
    { rotulo: t('pdf.copiar'), atalho: 'Ctrl+C', desabilitado: !selecao, acao: () => copiarTexto(selecao) },
    { separador: true },
    { rotulo: t('pdf.girar'), acao: () => girar() },
    { rotulo: t('pdf.moverEsquerda'), desabilitado: estado.pagina <= 1, acao: () => mover(-1) },
    { rotulo: t('pdf.moverDireita'), desabilitado: estado.pagina >= estado.total, acao: () => mover(1) },
    { separador: true },
    { rotulo: t('pdf.extrair'), acao: () => extrair() },
    { rotulo: t('pdf.excluir'), perigo: true, desabilitado: estado.total <= 1, acao: () => excluir() },
  ];

  ui.menuContexto(x ?? base?.left ?? 0, y ?? (base?.bottom ?? 0) + 4, itens);
}

async function copiarTexto(texto) {
  try {
    await navigator.clipboard.writeText(texto);
    ui.toast(t('pdf.copiado'), 'ok', 2400);
  } catch {
    ui.toast(t('pdf.copiarFalhou'), 'erro', 4000);
  }
}

function atalhos(evento) {
  if (!estado) return;
  if (evento.target instanceof HTMLInputElement) return;

  if (evento.key === 'ArrowLeft' || evento.key === 'PageUp') irPara(estado.pagina - 1);
  else if (evento.key === 'ArrowRight' || evento.key === 'PageDown') irPara(estado.pagina + 1);
}

async function mudarZoom(delta) {
  if (!estado) return;

  const atual = estado.escalaAtual ?? estado.escala ?? 1;
  await definirZoom(atual + delta);
}

/** `escala` 0 = ajustar à largura; qualquer outro valor é o zoom absoluto. */
async function definirZoom(escala) {
  if (!estado) return;

  estado.escala = Math.min(4, Math.max(0.2, escala));
  await desenhar();
}

async function ajustar() {
  if (!estado) return;

  estado.escala = 0;
  await desenhar();
}

function atualizarBarra() {
  if (!estado) return;

  const rotulo = porId('pdf-pagina');
  if (rotulo) rotulo.textContent = t('pdf.pagina', { atual: estado.pagina, total: estado.total });

  const zoom = porId('pdf-zoom');
  if (zoom) zoom.textContent = `${Math.round((estado.escalaAtual ?? 1) * 100)}%`;

  const anterior = porId('pdf-anterior');
  const proxima = porId('pdf-proxima');
  if (anterior) anterior.disabled = estado.pagina <= 1;
  if (proxima) proxima.disabled = estado.pagina >= estado.total;

  const salvarBotao = porId('pdf-salvar');
  const descartarBotao = porId('pdf-descartar');
  if (salvarBotao) salvarBotao.disabled = !estado.sujo;
  if (descartarBotao) descartarBotao.disabled = !estado.sujo;
}
