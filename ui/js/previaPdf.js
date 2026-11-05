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
  const canvas = document.createElement('canvas');
  canvas.className = 'pdf-canvas';
  area.append(canvas);
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
    area,
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

  const contexto = estado.canvas.getContext('2d', { alpha: false });
  contexto.setTransform(proporcao, 0, 0, proporcao, 0, 0);
  contexto.fillStyle = '#ffffff';
  contexto.fillRect(0, 0, viewport.width, viewport.height);

  await pagina.render({ canvasContext: contexto, viewport }).promise;

  atualizarBarra();
}

/** Aplica uma edição com pdf-lib e recarrega a prévia com o resultado. */
async function editar(acao, paginaDepois) {
  if (!estado) return;

  const lib = await carregarPdfLib();
  const documento = await lib.PDFDocument.load(estado.bytes, { updateMetadata: false });

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
  atualizarBarra();
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
    ['pdf-ajustar', () => { if (estado) { estado.escala = 0; desenhar(); } }],
    ['pdf-girar', () => girar()],
    ['pdf-mover-esq', () => mover(-1)],
    ['pdf-mover-dir', () => mover(1)],
    ['pdf-excluir', () => excluir()],
    ['pdf-extrair', () => extrair()],
    ['pdf-salvar', () => salvar()],
    ['pdf-descartar', () => descartar()],
  ];

  for (const [id, acao] of cliques) {
    porId(id)?.addEventListener('click', () => {
      Promise.resolve(acao()).catch((erro) => {
        ui.toast(t('pdf.erro', { motivo: erro?.message ?? '—' }), 'erro', 6000);
      });
    });
  }

  document.addEventListener('keydown', atalhos);
  ligada = true;
  atualizarBarra();
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
  estado.escala = Math.min(4, Math.max(0.25, atual + delta));
  await desenhar();
}

function atualizarBarra() {
  if (!estado) return;

  const rotulo = porId('pdf-pagina');
  if (rotulo) rotulo.textContent = t('pdf.pagina', { atual: estado.pagina, total: estado.total });

  const anterior = porId('pdf-anterior');
  const proxima = porId('pdf-proxima');
  if (anterior) anterior.disabled = estado.pagina <= 1;
  if (proxima) proxima.disabled = estado.pagina >= estado.total;

  const excluirBotao = porId('pdf-excluir');
  if (excluirBotao) excluirBotao.disabled = estado.total <= 1;

  const esquerda = porId('pdf-mover-esq');
  const direita = porId('pdf-mover-dir');
  if (esquerda) esquerda.disabled = estado.pagina <= 1;
  if (direita) direita.disabled = estado.pagina >= estado.total;

  const salvarBotao = porId('pdf-salvar');
  const descartarBotao = porId('pdf-descartar');
  if (salvarBotao) salvarBotao.disabled = !estado.sujo;
  if (descartarBotao) descartarBotao.disabled = !estado.sujo;
}
