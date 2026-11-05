import { call, on } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as ui from './ui.js';
import * as thumbs from './thumbnails.js';
import * as preview from './preview.js';
import * as dialogs from './dialogs.js';
import * as lateral from './projects.js';
import * as projetoAtual from './projetoAtual.js';
import { icone } from './icons.js';
import { state, itensAtuais, selecionados, limparSelecao } from './state.js';

const area = document.getElementById('area-itens');
const vazio = document.getElementById('vazio');
const migalhas = document.getElementById('migalhas');
const buscaInput = document.getElementById('busca');
const btnVoltar = document.getElementById('btn-voltar');
const btnAvancar = document.getElementById('btn-avancar');
const btnLimparBusca = document.getElementById('busca-limpar');
const areaEndereco = document.getElementById('endereco-area');
const entradaEndereco = document.getElementById('endereco');
const pillDigitacao = document.getElementById('digitacao');
const cabecalhoEl = document.getElementById('cabecalho-lista');

const ORDENADOR = new Intl.Collator('pt-BR', { numeric: true, sensitivity: 'base' });
const PAD = 12;
const GAP = 10;

let espacoVirtual = null;
let opDirAtual = null;
let agendado = false;
let temporizadorBusca = null;
let bufferDigitacao = '';
let horaDigitacao = 0;
let temporizadorDigitacao = null;

export function iniciarExplorer() {
  espacoVirtual = document.createElement('div');
  espacoVirtual.style.cssText = 'position:absolute;left:0;top:0;width:1px;pointer-events:none;';
  area.appendChild(espacoVirtual);

  area.addEventListener('click', aoClicar);
  area.addEventListener('dblclick', aoDuploClique);
  area.addEventListener('contextmenu', aoMenuContexto);
  area.addEventListener('scroll', desenhar, { passive: true });
  document.addEventListener('keydown', aoTeclar);
  iniciarSliderGrade();
  window.addEventListener('resize', () => {
    medirBarraRolagem();
    desenhar();
    if (state.caminho) atualizarMigalhas(state.caminho);
  });

  // A barra de rolagem aparece/desaparece conforme a lista: mantem o cabecalho alinhado.
  if (typeof ResizeObserver === 'function') {
    new ResizeObserver(() => medirBarraRolagem()).observe(area);
  }

  // Abrir/fechar/arrastar a previa muda a largura disponivel: refaz o layout.
  document.addEventListener('layout', () => {
    medirBarraRolagem();
    desenhar();
  });

  btnVoltar.addEventListener('click', voltar);
  btnAvancar.addEventListener('click', avancar);
  document.getElementById('btn-subir').addEventListener('click', subir);
  document.getElementById('btn-atualizar').addEventListener('click', () => recarregar());
  document.getElementById('btn-visualizacao').addEventListener('click', alternarVisao);

  buscaInput.addEventListener('input', aoDigitarBusca);
  btnLimparBusca.addEventListener('click', () => {
    buscaInput.value = '';
    sairDaBusca();
  });

  carregarPreferencias();
  aplicarColunas();
  medirBarraRolagem();
  iniciarEndereco();

  on('dir.batch', (dados) => {
    if (dados.opId !== opDirAtual) return;
    state.itens.push(...dados.itens);
    desenhar();
  });

  on('dir.done', (dados) => {
    if (dados.opId !== opDirAtual) return;
    state.carregando = false;
    ordenarListas();
    desenhar();
  });

  on('search.batch', (dados) => {
    if (dados.opId !== state.busca.opId) return;
    state.busca.resultados.push(...dados.itens);
    desenhar();
  });

  on('search.progress', (dados) => {
    if (dados.opId !== state.busca.opId) return;
    state.busca.pastas = dados.pastas;
    atualizarStatus();
  });

  on('search.done', (dados) => {
    if (dados.opId !== state.busca.opId) return;
    state.busca.carregando = false;
    desenhar();
  });

  on('os.drop', async (dados) => {
    if (!dados?.caminhos?.length) return;

    // Solto do Explorer em cima dos Favoritos: vira favorito, não cola arquivo.
    if (Number.isFinite(dados.x) && Number.isFinite(dados.y)) {
      const escala = window.devicePixelRatio || 1;
      if (lateral.alvoFavoritos(dados.x / escala, dados.y / escala)) {
        lateral.destacarFavoritos(false);
        await lateral.adicionarFavoritos(dados.caminhos);
        return;
      }
    }

    lateral.destacarFavoritos(false);
    if (!state.caminho) return;
    await colarCaminhos(dados.caminhos, false);
  });

  on('os.dragOver', (dados) => {
    if (!dados || !Number.isFinite(dados.x) || !Number.isFinite(dados.y)) return;
    const escala = window.devicePixelRatio || 1;
    lateral.destacarFavoritos(lateral.alvoFavoritos(dados.x / escala, dados.y / escala));
  });

  // Outra janela comecou um arraste: se o cursor estiver sobre esta, mostra o alvo.
  on('arraste.estado', (dados) => mostrarAlvoArraste(dados));
  on('arraste.erro', (dados) => {
    if (dados?.mensagem) ui.toast(dados.mensagem, 'erro', 7000);
  });
  on('dir.refresh', (dados) => {
    if (!dados?.caminho) return;
    const mesmo = (a, b) => a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase();
    if (state.caminho && mesmo(dados.caminho, state.caminho)) recarregar();
  });

  iniciarArrasteExterno();
  iniciarLacoSelecao();

  on('op.progress', (dados) => {
    const card = ui.progresso({
      id: dados.OperacaoId,
      titulo: dados.Tipo === 'mover' ? t('progresso.movendo', { n: dados.ItensTotal }) : t('progresso.copiando', { n: dados.ItensTotal }),
    });
    card.aoCancelar = () => call('fs.cancelar', { opId: dados.OperacaoId }).catch(() => {});
    const pct = dados.BytesTotal > 0
      ? (dados.BytesConcluidos / dados.BytesTotal) * 100
      : (dados.ItensConcluidos / Math.max(1, dados.ItensTotal)) * 100;
    card.atualizar(pct, dados.ItemAtual || t('progresso.itens', { feitos: dados.ItensConcluidos, total: dados.ItensTotal }));
  });

  on('op.conflict', async (info) => {
    const decisao = await dialogs.conflito(info);
    const aplicarTodos = decisao.aplicarTodos ?? false;
    const chave = { substituir: 'substituir', manterAmbos: 'manterAmbos', pular: 'pular' }[decisao.escolha] ?? 'cancelar';
    await call('fs.conflict', { id: info.Id, decisao: chave, aplicarTodos }).catch(() => {});
  });
}

// ---------- arraste (dentro e entre janelas) ----------

/**
 * Arrastar entre janelas nao pode ser HTML5 drag-and-drop: cada WebView2 e um
 * navegador separado e captura o mouse no mousedown, entao o "soltar" nunca chega
 * na outra janela. O host acompanha o cursor de verdade e entrega os arquivos na
 * pasta atual da janela alvo; aqui so avisamos o inicio/fim do arraste.
 */
function iniciarArrasteExterno() {
  area.addEventListener('pointerdown', (evento) => {
    if (evento.button !== 0) return;

    const alvo = evento.target.closest('[data-caminho]');
    if (!alvo || evento.target.closest('button, input, .alca')) return;

    const inicio = { x: evento.clientX, y: evento.clientY };
    let ativo = false;
    let caminhos = [];
    let copiar = false;

    const mover = (e) => {
      if (!ativo) {
        if (Math.abs(e.clientX - inicio.x) < 6 && Math.abs(e.clientY - inicio.y) < 6) return;

        const caminhoAlvo = alvo.dataset.caminho;
        if (!state.selecao.has(caminhoAlvo)) {
          state.selecao.clear();
          state.selecao.add(caminhoAlvo);
          aplicarSelecao();
        }

        caminhos = selecionados().map((i) => i.Caminho ?? i.caminho).filter(Boolean);
        if (caminhos.length === 0) return;

        copiar = e.ctrlKey;
        ativo = true;
        document.body.classList.add('arrastando');
        call('arraste.iniciar', { caminhos, copiar }).catch(() => {});
      }

      lateral.destacarFavoritos(lateral.alvoFavoritos(e.clientX, e.clientY));
    };

    const soltar = (e) => {
      encerrar();
      if (!ativo) return;

      // Soltou em cima dos Favoritos: fixa o item em vez de mover na pasta.
      if (lateral.alvoFavoritos(e.clientX, e.clientY)) {
        call('arraste.cancelar').catch(() => {});
        lateral.adicionarFavoritos(caminhos);
        return;
      }

      // Soltou dentro desta janela, sobre uma pasta: move/copia para ela.
      const sobCursor = document.elementFromPoint(e.clientX, e.clientY)?.closest('[data-caminho]');
      const item = sobCursor
        ? itensAtuais().find((i) => (i.Caminho ?? i.caminho) === sobCursor.dataset.caminho)
        : null;

      if (item && (item.Pasta ?? item.pasta)) {
        call('arraste.cancelar').catch(() => {});
        entregarEm(item.Caminho ?? item.caminho, caminhos, e.ctrlKey || copiar);
      } else {
        // Deixa o host decidir: outra janela sob o cursor recebe os arquivos.
        call('arraste.soltar').catch(() => {});
      }
    };

    const cancelar = () => {
      encerrar();
      if (ativo) call('arraste.cancelar').catch(() => {});
    };

    function encerrar() {
      window.removeEventListener('pointermove', mover);
      window.removeEventListener('pointerup', soltar);
      window.removeEventListener('pointercancel', cancelar);
      document.body.classList.remove('arrastando');
      lateral.destacarFavoritos(false);
    }

    window.addEventListener('pointermove', mover);
    window.addEventListener('pointerup', soltar);
    window.addEventListener('pointercancel', cancelar);
  });
}

async function entregarEm(destino, caminhos, copiar) {
  const opId = `arraste-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  try {
    if (copiar) {
      await call('fs.copy', { origens: caminhos, destino, opId });
    } else {
      await call('fs.move', { origens: caminhos, destino, opId });
    }
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }

  recarregar();
}

function mostrarAlvoArraste(dados) {
  const overlay = document.getElementById('arraste-alvo');
  if (!overlay) return;

  const meuId = state.info?.janela;
  if (!dados?.ativo || !dados.alvo || dados.alvo !== meuId) {
    overlay.hidden = true;
    return;
  }

  overlay.hidden = false;
  overlay.querySelector('.arraste-texto').textContent = dados.copiar
    ? t('arraste.copiarAqui')
    : t('arraste.moverAqui');
  overlay.querySelector('.arraste-pasta').textContent = dados.pasta ?? '';
}

// ---------- navegação ----------

export async function navegar(caminho, { historico = true } = {}) {
  if (!caminho) return;

  sairDaBusca({ silencioso: true });
  state.caminho = caminho;
  state.itens = [];
  state.carregando = true;
  limparSelecao();
  preview.limpar();
  area.scrollTop = 0;
  desenhar();

  const opId = `dir-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  opDirAtual = opId;

  try {
    await call('fs.list', {
      caminho,
      mostrarOcultos: Boolean(state.config.MostrarOcultos),
      opId,
    });
  } catch (erro) {
    state.carregando = false;
    ui.toast(t('erro.pasta', { mensagem: erro.message }), 'erro');
    desenhar();
    return;
  }

  state.carregando = false;
  ordenarListas();
  desenhar();
  atualizarMigalhas(caminho);

  if (historico) {
    state.historico = state.historico.slice(0, state.indiceHistorico + 1);
    if (state.historico[state.historico.length - 1] !== caminho) state.historico.push(caminho);
    state.indiceHistorico = state.historico.length - 1;
  }

  atualizarBotoesNav();
  atualizarEspaco(caminho);
  projetoAtual.atualizar(caminho).catch(() => {});

  call('app.ultimaPasta', { caminho }).catch(() => {});
}

export function recarregar() {
  if (state.caminho) navegar(state.caminho, { historico: false });
}

export function subir() {
  if (!state.caminho) return;
  const pai = fmt.pastaDe(state.caminho);
  if (pai && pai !== state.caminho) navegar(pai);
}

function voltar() {
  if (state.indiceHistorico <= 0) return;
  state.indiceHistorico--;
  navegar(state.historico[state.indiceHistorico], { historico: false });
  atualizarBotoesNav();
}

function avancar() {
  if (state.indiceHistorico >= state.historico.length - 1) return;
  state.indiceHistorico++;
  navegar(state.historico[state.indiceHistorico], { historico: false });
  atualizarBotoesNav();
}

function atualizarBotoesNav() {
  btnVoltar.disabled = state.indiceHistorico <= 0;
  btnAvancar.disabled = state.indiceHistorico >= state.historico.length - 1;
}

async function atualizarEspaco(caminho) {
  try {
    const espaco = await call('fs.free', { caminho });
    document.getElementById('status-espaco').textContent = t('status.espacoLivre', { livre: espaco.livreTexto });
  } catch {
    document.getElementById('status-espaco').textContent = '';
  }
}

function atualizarMigalhas(caminho) {
  entradaEndereco.hidden = true;
  migalhas.hidden = false;
  const partes = partesDeCaminho(caminho);

  desenharMigalhas(partes, false);

  // Em janelas estreitas, colapsa o meio (como o Explorer) em vez de picar todo nome.
  requestAnimationFrame(() => {
    if (migalhas.scrollWidth > migalhas.clientWidth + 2 && partes.length > 4) {
      desenharMigalhas(partes, true);
    }
  });
}

function desenharMigalhas(partes, compacto) {
  migalhas.replaceChildren();

  const visiveis = compacto && partes.length > 4
    ? [partes[0], null, partes[partes.length - 2], partes[partes.length - 1]]
    : partes;

  visiveis.forEach((parte, indice) => {
    if (indice > 0) {
      const sep = document.createElement('span');
      sep.className = 'migalha-sep';
      sep.textContent = '›';
      migalhas.appendChild(sep);
    }

    if (parte === null) {
      const reticencias = document.createElement('button');
      reticencias.className = 'migalha';
      reticencias.textContent = '…';
      reticencias.title = t('endereco.editar');
      reticencias.addEventListener('click', entrarEdicaoEndereco);
      migalhas.appendChild(reticencias);
      return;
    }

    const botao = document.createElement('button');
    botao.className = `migalha${indice === visiveis.length - 1 ? ' atual' : ''}`;
    botao.textContent = parte.nome;
    botao.title = parte.caminho;
    botao.addEventListener('click', () => navegar(parte.caminho));
    migalhas.appendChild(botao);
  });
}

function partesDeCaminho(caminho) {
  const normalizado = caminho.replace(/\//g, '\\');

  if (normalizado.startsWith('\\\\')) {
    const resto = normalizado.slice(2).split('\\').filter(Boolean);
    if (resto.length < 2) return [{ nome: normalizado, caminho: normalizado }];

    const raiz = `\\\\${resto[0]}\\${resto[1]}`;
    const partes = [{ nome: raiz, caminho: raiz }];
    let atual = raiz;
    for (const nome of resto.slice(2)) {
      atual = `${atual}\\${nome}`;
      partes.push({ nome, caminho: atual });
    }
    return partes;
  }

  const pedacos = normalizado.split('\\').filter(Boolean);
  const partes = [];
  let atual = '';
  for (const pedaco of pedacos) {
    atual = atual ? `${atual}\\${pedaco}` : `${pedaco}\\`;
    partes.push({ nome: pedaco, caminho: atual });
  }

  return partes;
}

// ---------- renderização ----------

function desenhar() {
  if (agendado) return;
  agendado = true;
  requestAnimationFrame(() => {
    agendado = false;
    renderizar();
  });
}

function ordenar(a, b) {
  const pastaA = a.Pasta ?? a.pasta;
  const pastaB = b.Pasta ?? b.pasta;
  if (pastaA !== pastaB) return pastaA ? -1 : 1;

  const { campo, desc } = state.ordenacao;
  const nomeA = a.Nome ?? a.nome;
  const nomeB = b.Nome ?? b.nome;

  let comparacao;
  if (campo === 'tamanho') {
    comparacao = (a.Tamanho ?? a.tamanho ?? 0) - (b.Tamanho ?? b.tamanho ?? 0);
  } else if (campo === 'modificado') {
    comparacao = new Date(a.Modificado ?? a.modificado) - new Date(b.Modificado ?? b.modificado);
  } else if (campo === 'criado') {
    comparacao = new Date(a.Criacao ?? a.criacao) - new Date(b.Criacao ?? b.criacao);
  } else if (campo === 'tipo') {
    comparacao = ORDENADOR.compare(rotuloTipo(a), rotuloTipo(b));
  } else {
    comparacao = ORDENADOR.compare(nomeA, nomeB);
  }

  if (comparacao === 0) {
    comparacao = ORDENADOR.compare(nomeA, nomeB);
  }

  return desc ? -comparacao : comparacao;
}

function rotuloTipo(item) {
  const nome = item.Nome ?? item.nome ?? '';
  if (item.Pasta ?? item.pasta) return t('coluna.pasta');
  const ext = fmt.extensao(nome);
  const categoria = fmt.categoria(ext);

  const nomeCategoria = {
    imagem: t('coluna.tipoImagem'),
    video: t('coluna.tipoVideo'),
    audio: t('coluna.tipoAudio'),
    modelo3d: t('coluna.tipo3d'),
    documento: t('coluna.tipoDocumento'),
    projeto: t('coluna.tipoProjeto'),
    outro: t('coluna.tipoArquivo'),
  }[categoria] ?? t('coluna.tipoArquivo');

  return ext ? `${nomeCategoria} ${ext.toUpperCase()}` : nomeCategoria;
}

function ordenarListas() {
  state.itens.sort(ordenar);
  state.busca.resultados.sort(ordenar);
}

function aplicarOrdenacao() {
  ordenarListas();
  localStorage.setItem('cvexplorer:ordenacao', JSON.stringify(state.ordenacao));
  atualizarSetasOrdenacao();
  desenhar();
}

function alternarOrdenacao(campo) {
  if (!campo) return;

  if (state.ordenacao.campo === campo) {
    state.ordenacao.desc = !state.ordenacao.desc;
  } else {
    state.ordenacao.campo = campo;
    state.ordenacao.desc = campo !== 'nome';
  }

  aplicarOrdenacao();
}

function atualizarSetasOrdenacao() {
  if (!cabecalhoEl.querySelector('[data-campo]')) return;

  for (const celula of cabecalhoEl.querySelectorAll('[data-campo]')) {
    const ativo = celula.dataset.campo === state.ordenacao.campo;
    celula.classList.toggle('ordenado', ativo);
    const seta = celula.querySelector('.seta');
    if (seta) {
      seta.textContent = ativo ? (state.ordenacao.desc ? '▼' : '▲') : '';
    }
  }
}

// ---------- colunas redimensionáveis ----------

const COLUNAS = {
  nome: { min: 120, max: 2400, padrao: 340, variavel: '--w-nome' },
  tipo: { min: 70, max: 400, padrao: 96, variavel: '--w-tipo' },
  tamanho: { min: 64, max: 600, padrao: 92, variavel: '--w-tam' },
  data: { min: 88, max: 600, padrao: 132, variavel: '--w-data' },
  criado: { min: 88, max: 600, padrao: 132, variavel: '--w-criado' },
};

function aplicarColunas() {
  // Na raiz: vale para o cabecalho (fora do scroller) e para as linhas.
  const raiz = document.documentElement;
  for (const [chave, def] of Object.entries(COLUNAS)) {
    raiz.style.setProperty(def.variavel, `${state.colunas[chave]}px`);
  }
}

/**
 * O cabecalho fica fora do scroller, entao a barra de rolagem vertical
 * encurta as linhas sem encurtar o cabecalho: sem isso a alca fica alguns
 * pixels a direita da divisoria real.
 */
function medirBarraRolagem() {
  const largura = area.clientWidth > 0 ? Math.max(0, area.offsetWidth - area.clientWidth) : 0;
  document.documentElement.style.setProperty('--sbw', `${Math.round(largura)}px`);
}

function garantirAlca(coluna) {
  const def = COLUNAS[coluna];
  const alca = document.createElement('div');
  alca.className = 'alca';
  alca.dataset.coluna = coluna;
  alca.title = t('coluna.redimensionar');
  alca.setAttribute('role', 'separator');
  alca.setAttribute('aria-orientation', 'vertical');

  // Captura de ponteiro: os eventos continuam chegando mesmo se o mouse
  // sair do cabecalho durante o arraste.
  alca.addEventListener('pointerdown', (evento) => {
    if (evento.button !== 0) return;
    evento.preventDefault();
    evento.stopPropagation();

    // clientX ja vem em px de CSS: independe da escala do Windows.
    const inicioX = evento.clientX;
    const larguraInicial = state.colunas[coluna];
    let larguraAtual = larguraInicial;
    let arrastou = false;

    alca.classList.add('ativa');
    document.body.classList.add('redimensionando');
    try { alca.setPointerCapture(evento.pointerId); } catch { /* ponteiro sintetico */ }

    const mover = (e) => {
      const delta = e.clientX - inicioX;
      if (!arrastou && Math.abs(delta) < 2) return;
      arrastou = true;

      // limite: nunca deixa a coluna empurrar tamanho/data para fora da area
      const limiteArea = Math.max(def.min, area.clientWidth - 40);
      larguraAtual = Math.min(def.max, Math.max(def.min, larguraInicial + delta));
      if (coluna === 'nome') {
        larguraAtual = Math.min(larguraAtual, limiteArea);
      }

      state.colunas[coluna] = Math.round(larguraAtual);
      aplicarColunas();
    };

    const soltar = () => {
      alca.removeEventListener('pointermove', mover);
      alca.removeEventListener('pointerup', soltar);
      alca.removeEventListener('pointercancel', soltar);
      alca.classList.remove('ativa');
      document.body.classList.remove('redimensionando');
      try { alca.releasePointerCapture(evento.pointerId); } catch { /* ja liberado */ }

      if (arrastou) {
        localStorage.setItem('cvexplorer:colunas', JSON.stringify(state.colunas));
        medirBarraRolagem();
        desenhar();
      }
    };

    alca.addEventListener('pointermove', mover);
    alca.addEventListener('pointerup', soltar);
    alca.addEventListener('pointercancel', soltar);
  });

  // Duplo clique volta ao padrao da coluna.
  alca.addEventListener('dblclick', (e) => {
    e.preventDefault();
    e.stopPropagation();
    state.colunas[coluna] = def.padrao;
    aplicarColunas();
    localStorage.setItem('cvexplorer:colunas', JSON.stringify(state.colunas));
    desenhar();
  });

  alca.addEventListener('click', (e) => e.stopPropagation());
  return alca;
}

function carregarPreferencias() {
  try {
    const colunas = JSON.parse(localStorage.getItem('cvexplorer:colunas') ?? 'null');
    if (colunas) {
      for (const [chave, def] of Object.entries(COLUNAS)) {
        if (typeof colunas[chave] === 'number') {
          state.colunas[chave] = Math.min(def.max, Math.max(def.min, colunas[chave]));
        }
      }
    }

    const ordenacao = JSON.parse(localStorage.getItem('cvexplorer:ordenacao') ?? 'null');
    if (ordenacao && ['nome', 'tamanho', 'modificado', 'tipo', 'criado'].includes(ordenacao.campo)) {
      state.ordenacao = { campo: ordenacao.campo, desc: Boolean(ordenacao.desc) };
    }
  } catch {
    // preferencias corrompidas: segue com os padroes
  }
}

function garantirCabecalho() {
  if (cabecalhoEl.querySelector('[data-campo]')) {
    atualizarSetasOrdenacao();
    return;
  }

  const colunas = [
    { campo: null, texto: '' },
    { campo: 'nome', texto: t('coluna.nome'), redimensionavel: 'nome' },
    { campo: 'tipo', texto: t('coluna.tipo'), redimensionavel: 'tipo' },
    { campo: 'tamanho', texto: t('preview.tamanho'), redimensionavel: 'tamanho' },
    { campo: 'modificado', texto: t('preview.modificado'), redimensionavel: 'data' },
    { campo: 'criado', texto: t('coluna.criado'), redimensionavel: 'criado' },
    { campo: null, texto: '' },
  ];

  cabecalhoEl.replaceChildren();

  for (const coluna of colunas) {
    if (!coluna.campo) {
      cabecalhoEl.appendChild(document.createElement('span'));
      continue;
    }

    const celula = document.createElement('button');
    celula.className = 'cabecalho-celula';
    if (coluna.campo === 'nome') celula.classList.add('inicio');
    celula.dataset.campo = coluna.campo;
    celula.type = 'button';
    celula.title = t('coluna.ordenar', { coluna: coluna.texto });
    celula.append(document.createTextNode(coluna.texto));

    const seta = document.createElement('span');
    seta.className = 'seta';
    celula.appendChild(seta);

    celula.addEventListener('click', () => alternarOrdenacao(coluna.campo));

    if (coluna.redimensionavel) {
      celula.appendChild(garantirAlca(coluna.redimensionavel));
    }

    cabecalhoEl.appendChild(celula);
  }

  atualizarSetasOrdenacao();
}

function renderizar() {
  const itens = itensAtuais();
  const total = itens.length;

  vazio.hidden = total > 0 || state.carregando;
  cabecalhoEl.hidden = state.visao !== 'lista' || total === 0;

  if (state.visao === 'grade') {
    renderizarGrade(itens);
  } else {
    renderizarLista(itens);
  }

  atualizarStatus();
}

function renderizarGrade(itens) {
  const disponivel = Math.max(120, area.clientWidth - PAD * 2);
  const larguraMinima = Math.max(96, state.grade);
  const colunas = Math.max(1, Math.floor((disponivel + GAP) / (larguraMinima + GAP)));

  // As colunas preenchem exatamente a largura visivel: sem barra horizontal.
  const larguraTelha = Math.max(72, (disponivel - (colunas - 1) * GAP) / colunas);
  const alturaTelha = larguraTelha + 38;
  const alturaLinha = alturaTelha + GAP;
  const linhas = Math.ceil(itens.length / colunas);

  espacoVirtual.style.height = `${PAD * 2 + linhas * alturaLinha}px`;

  const topo = area.scrollTop;
  const primeiraLinha = Math.max(0, Math.floor((topo - PAD) / alturaLinha) - 1);
  const ultimaLinha = Math.min(linhas - 1, Math.ceil((topo + area.clientHeight) / alturaLinha) + 1);

  const fragmento = document.createDocumentFragment();
  for (let linha = primeiraLinha; linha <= ultimaLinha; linha++) {
    for (let coluna = 0; coluna < colunas; coluna++) {
      const indice = linha * colunas + coluna;
      if (indice >= itens.length) break;
      fragmento.appendChild(criarTelha(itens[indice], coluna, linha, larguraTelha, alturaTelha, alturaLinha));
    }
  }

  area.querySelectorAll('.tile,.row,.col-cabecalho').forEach((el) => el.remove());
  area.appendChild(fragmento);
}

function criarTelha(item, coluna, linha, largura, altura, alturaLinha) {
  const caminho = item.Caminho ?? item.caminho;
  const nome = item.Nome ?? item.nome;
  const ehPasta = item.Pasta ?? item.pasta;
  const ext = fmt.extensao(nome);
  const cat = ehPasta ? 'pasta' : fmt.categoria(ext);

  const telha = document.createElement('div');
  telha.className = `tile${state.selecao.has(caminho) ? ' selecionado' : ''}`;
  telha.dataset.caminho = caminho;
  telha.style.left = `${PAD + coluna * (largura + GAP)}px`;
  telha.style.top = `${PAD + linha * alturaLinha}px`;
  telha.style.width = `${largura}px`;
  telha.style.height = `${altura}px`;
  telha.style.setProperty('--lado-telha', `${Math.round(largura)}px`);

  const thumb = document.createElement('div');
  thumb.className = 'tile-thumb';

  const usarMiniatura = !ehPasta && !['audio', 'documento', 'outro'].includes(cat);
  const emCache = usarMiniatura ? thumbs.emCache(item) : null;

  if (emCache) {
    const img = document.createElement('img');
    img.src = emCache;
    img.alt = '';
    img.loading = 'lazy';
    thumb.appendChild(img);
  } else {
    const iconeEl = document.createElement('div');
    iconeEl.className = 'tile-icone';
    iconeEl.dataset.cat = cat;
    iconeEl.innerHTML = icone(cat, ext);
    thumb.appendChild(iconeEl);
    if (usarMiniatura) {
      thumbs.solicitar(item, Math.round(largura * (window.devicePixelRatio || 1)), (url) => {
        if (!telha.isConnected) return;
        const img = document.createElement('img');
        img.src = url;
        img.alt = '';
        // Troca só o ícone: replaceChildren apagaria o selo de formato.
        thumb.replaceChild(img, iconeEl);
      });
    }
  }

  if (!ehPasta) {
    const badge = document.createElement('span');
    badge.className = 'tile-badge';
    badge.textContent = ext || '•';
    thumb.appendChild(badge);
  }

  const rotulo = document.createElement('div');
  rotulo.className = 'tile-nome';
  rotulo.textContent = nome;
  rotulo.title = nome;

  telha.append(thumb, rotulo);
  return telha;
}

function renderizarLista(itens) {
  garantirCabecalho();

  const alturaLinha = 28;
  espacoVirtual.style.height = `${itens.length * alturaLinha + 20}px`;

  const topo = area.scrollTop;
  const primeira = Math.max(0, Math.floor(topo / alturaLinha) - 1);
  const ultima = Math.min(itens.length - 1, Math.ceil((topo + area.clientHeight) / alturaLinha) + 1);

  const fragmento = document.createDocumentFragment();
  for (let indice = primeira; indice <= ultima; indice++) {
    const item = itens[indice];
    if (!item) continue;

    const caminho = item.Caminho ?? item.caminho;
    const nome = item.Nome ?? item.nome;
    const ehPasta = item.Pasta ?? item.pasta;
    const ext = fmt.extensao(nome);

    const linha = document.createElement('div');
    linha.className = `row${state.selecao.has(caminho) ? ' selecionado' : ''}`;
    linha.dataset.caminho = caminho;
    linha.style.top = `${indice * alturaLinha}px`;

    const iconeEl = document.createElement('span');
    iconeEl.className = 'row-icone';
    iconeEl.dataset.cat = ehPasta ? 'pasta' : fmt.categoria(ext);
    iconeEl.innerHTML = icone(ehPasta ? 'pasta' : fmt.categoria(ext), ext);

    const nomeEl = document.createElement('span');
    nomeEl.className = 'row-nome';
    nomeEl.textContent = nome;
    nomeEl.title = caminho;

    const tamanhoEl = document.createElement('span');
    tamanhoEl.className = 'row-tamanho';
    tamanhoEl.textContent = ehPasta ? '' : fmt.bytes(item.Tamanho ?? item.tamanho);

    const tipoEl = document.createElement('span');
    tipoEl.className = 'row-tipo';
    tipoEl.textContent = rotuloTipo(item);

    const dataEl = document.createElement('span');
    dataEl.className = 'row-data';
    dataEl.textContent = fmt.dataCurta(item.Modificado ?? item.modificado);

    const criadoEl = document.createElement('span');
    criadoEl.className = 'row-criado';
    criadoEl.textContent = fmt.dataCurta(item.Criacao ?? item.criacao);

    linha.append(iconeEl, nomeEl, tipoEl, tamanhoEl, dataEl, criadoEl);
    fragmento.appendChild(linha);
  }

  area.querySelectorAll('.tile,.row').forEach((el) => el.remove());
  area.appendChild(fragmento);
}

// ---------- tamanho das miniaturas ----------

let salvarGradeAgendado = null;

function iniciarSliderGrade() {
  const slider = document.getElementById('slider-grade');
  if (!slider) return;

  slider.value = String(state.grade ?? 168);

  slider.addEventListener('input', () => {
    const valor = Number(slider.value);
    if (!Number.isFinite(valor)) return;

    state.grade = valor;
    // Só a grade usa o tamanho: em lista o slider não muda nada, mas o valor fica guardado.
    if (state.visao === 'grade') desenhar();

    if (salvarGradeAgendado) clearTimeout(salvarGradeAgendado);
    salvarGradeAgendado = setTimeout(() => {
      state.config.TamanhoGrade = state.grade;
      call('settings.save', state.config).catch(() => {});
    }, 500);
  });
}

function atualizarStatus() {  const itens = itensAtuais();
  document.getElementById('status-itens').textContent = state.carregando
    ? t('status.carregando')
    : t('status.itens', { n: itens.length });

  document.getElementById('status-selecao').textContent = state.selecao.size > 0
    ? t('status.selecao', { n: state.selecao.size })
    : '';

  const statusBusca = document.getElementById('status-busca');
  if (state.busca.ativa) {
    statusBusca.textContent = state.busca.carregando
      ? t('busca.buscando')
      : t('busca.contagem', { n: state.busca.resultados.length });
    statusBusca.classList.add('destaque');
  } else {
    statusBusca.textContent = '';
    statusBusca.classList.remove('destaque');
  }
}

// ---------- seleção ----------

// ---------- seleção por laço (arrastar no vazio) ----------

let lacoAtivo = false;

function iniciarLacoSelecao() {
  area.addEventListener('pointerdown', (evento) => {
    if (evento.button !== 0) return;
    if (evento.target.closest('[data-caminho], button, input, .alca')) return;

    const inicio = { x: evento.clientX, y: evento.clientY };
    const base = evento.ctrlKey || evento.metaKey ? new Set(state.selecao) : new Set();
    let mexeu = false;

    const caixa = document.createElement('div');
    caixa.className = 'laco-selecao';
    caixa.hidden = true;
    area.appendChild(caixa);

    const areaRect = () => area.getBoundingClientRect();

    const mover = (e) => {
      if (!mexeu) {
        if (Math.abs(e.clientX - inicio.x) < 4 && Math.abs(e.clientY - inicio.y) < 4) return;
        mexeu = true;
        lacoAtivo = true;
        caixa.hidden = false;
      }

      const limite = areaRect();
      const x1 = Math.max(limite.left, Math.min(inicio.x, e.clientX));
      const y1 = Math.max(limite.top, Math.min(inicio.y, e.clientY));
      const x2 = Math.min(limite.right, Math.max(inicio.x, e.clientX));
      const y2 = Math.min(limite.bottom, Math.max(inicio.y, e.clientY));

      caixa.style.left = `${x1 - limite.left}px`;
      caixa.style.top = `${y1 - limite.top + area.scrollTop}px`;
      caixa.style.width = `${Math.max(0, x2 - x1)}px`;
      caixa.style.height = `${Math.max(0, y2 - y1)}px`;

      const novos = new Set(base);
      for (const el of area.querySelectorAll('[data-caminho]')) {
        const r = el.getBoundingClientRect();
        const cruza = r.left < x2 && r.right > x1 && r.top < y2 && r.bottom > y1;
        if (cruza) novos.add(el.dataset.caminho);
      }

      state.selecao = novos;
      aplicarSelecao();
    };

    const soltar = () => {
      window.removeEventListener('pointermove', mover);
      window.removeEventListener('pointerup', soltar);
      window.removeEventListener('pointercancel', soltar);
      caixa.remove();

      if (mexeu) {
        // Evita que o clique seguinte (no vazio) limpe o que acabou de ser selecionado.
        setTimeout(() => { lacoAtivo = false; }, 0);
        preview.mostrar(selecionados()).catch(() => {});
      } else {
        lacoAtivo = false;
      }
    };

    window.addEventListener('pointermove', mover);
    window.addEventListener('pointerup', soltar);
    window.addEventListener('pointercancel', soltar);
  });
}

function aoClicar(evento) {
  if (lacoAtivo) return;

  const alvo = evento.target.closest('[data-caminho]');

  if (!alvo) {
    if (!evento.ctrlKey && !evento.metaKey) {
      limparSelecao();
      aplicarSelecao();
      preview.limpar();
    }
    return;
  }

  const caminho = alvo.dataset.caminho;
  const itens = itensAtuais();
  const indice = itens.findIndex((i) => (i.Caminho ?? i.caminho) === caminho);

  if (evento.ctrlKey || evento.metaKey) {
    if (state.selecao.has(caminho)) state.selecao.delete(caminho);
    else state.selecao.add(caminho);
    state.ancora = indice;
  } else if (evento.shiftKey && state.ancora !== null) {
    const inicio = Math.min(state.ancora, indice);
    const fim = Math.max(state.ancora, indice);
    for (let i = inicio; i <= fim; i++) {
      const item = itens[i];
      if (item) state.selecao.add(item.Caminho ?? item.caminho);
    }
  } else {
    state.selecao.clear();
    state.selecao.add(caminho);
    state.ancora = indice;
  }

  aplicarSelecao();
  prefetchSequencia(selecionados());
  preview.mostrar(selecionados()).catch(() => {});
}

function aplicarSelecao() {
  for (const el of area.querySelectorAll('[data-caminho]')) {
    el.classList.toggle('selecionado', state.selecao.has(el.dataset.caminho));
  }
  atualizarStatus();
}

function aoDuploClique(evento) {
  const alvo = evento.target.closest('[data-caminho]');
  if (!alvo) return;
  const item = itensAtuais().find((i) => (i.Caminho ?? i.caminho) === alvo.dataset.caminho);
  if (item) abrir(item);
}

function abrir(item) {
  if (item.Pasta ?? item.pasta) {
    navegar(item.Caminho ?? item.caminho);
  } else {
    call('fs.open', { caminho: item.Caminho ?? item.caminho })
      .catch((erro) => ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro'));
  }
}

// ---------- menu de contexto ----------

// Sequências são detectadas no host; guardamos o resultado por caminho para o menu
// já mostrar a contagem de quadros sem esperar o clique.
const sequencias = new Map();

function prefetchSequencia(lista) {
  if (lista.length !== 1) return;
  const item = lista[0];
  const caminho = item.Caminho ?? item.caminho;
  if (!caminho || !fmt.podeSerSequencia(item.Nome ?? item.nome) || sequencias.has(caminho)) return;

  sequencias.set(caminho, null);
  call('media.sequencia', { caminho })
    .then((dados) => sequencias.set(caminho, dados?.sequencia ?? false))
    .catch(() => sequencias.set(caminho, false));
}

function itensConverterPara(primeiro) {
  return dialogs.PRESETS_CONVERSAO.flatMap((grupo) => [
    { cabecalho: grupo.grupo },
    ...grupo.itens.map((preset) => {
      const indisponivel = dialogs.presetIndisponivel(preset.chave);
      return {
        rotulo: indisponivel ? `${preset.rotulo} — ${t('menu.indisponivelCurto')}` : preset.rotulo,
        dica: indisponivel ? t('menu.indisponivelMotivo') : '',
        desabilitado: indisponivel,
        acao: () => dialogs.converterVideo(primeiro, state.config, preset.chave),
      };
    }),
  ]);
}

async function aoMenuContexto(evento) {
  evento.preventDefault();
  const alvo = evento.target.closest('[data-caminho]');

  if (alvo && !state.selecao.has(alvo.dataset.caminho)) {
    state.selecao.clear();
    state.selecao.add(alvo.dataset.caminho);
    aplicarSelecao();
    preview.mostrar(selecionados()).catch(() => {});
  }

  const selecao = selecionados();
  const primeiro = selecao[0];
  const umSo = selecao.length === 1;
  const ehPasta = primeiro ? (primeiro.Pasta ?? primeiro.pasta) : false;
  const temVideo = selecao.some((i) => fmt.ehVideo(i.Nome ?? i.nome));

  prefetchSequencia(selecao);

  const itens = [];

  if (primeiro) {
    itens.push(
      { rotulo: t('menu.abrir'), acao: () => abrir(primeiro) },
      { rotulo: t('menu.abrirExplorer'), acao: () => call('fs.reveal', { caminho: primeiro.Caminho ?? primeiro.caminho }) },
      { separador: true },
      { rotulo: t('menu.copiar'), atalho: 'Ctrl+C', acao: copiar },
      { rotulo: t('menu.recortar'), atalho: 'Ctrl+X', acao: recortar },
      { rotulo: t('menu.renomear'), atalho: 'F2', desabilitado: !umSo, acao: renomearSelecionado },
      { rotulo: t('menu.excluir'), atalho: 'Del', perigo: true, acao: excluirSelecionado },
      { separador: true },
    );

    if (temVideo) {
      itens.push({ rotulo: t('menu.converter'), acao: () => dialogs.converterVideo(primeiro, state.config) });

      if (umSo) {
        itens.push({
          rotulo: t('menu.converterPara'),
          submenu: itensConverterPara(primeiro),
        });
      }
    }

    const caminhoPrimeiro = primeiro.Caminho ?? primeiro.caminho;
    const sequencia = sequencias.get(caminhoPrimeiro);
    const podeSequencia = umSo
      && fmt.podeSerSequencia(primeiro.Nome ?? primeiro.nome)
      && sequencia !== false;

    if (podeSequencia) {
      itens.push({
        rotulo: sequencia?.quadros
          ? t('menu.converterSequencia', { n: sequencia.quadros })
          : t('menu.converterSequenciaGenerica'),
        acao: () => dialogs.converterSequencia(primeiro, state.config),
      });
    }

    if (ehPasta && umSo) {
      itens.push(
        { rotulo: t('menu.registrarProjeto'), acao: () => dialogs.registrarProjeto(primeiro.Caminho ?? primeiro.caminho) },
        { rotulo: t('menu.criarPastasModelo'), acao: () => dialogs.pastasModelo(primeiro.Caminho ?? primeiro.caminho, recarregar) },
        { rotulo: t('menu.novo'), submenu: await submenuNovo(primeiro.Caminho ?? primeiro.caminho) },
      );
    }

    itens.push({ separador: true });
    itens.push({
      rotulo: t('menu.copiarCaminho'),
      acao: () => navigator.clipboard?.writeText(selecao.map((i) => i.Caminho ?? i.caminho).join('\n')),
    });
  } else {
    itens.push({ rotulo: t('menu.novaPasta'), acao: () => novaPasta() });
    itens.push({ rotulo: t('menu.colar'), atalho: 'Ctrl+V', acao: colar });
    itens.push({ separador: true });
    itens.push({ rotulo: t('menu.novoProjeto'), acao: () => dialogs.novoProjeto(state.caminho, criado) });

    if (state.caminho) {
      itens.push({ rotulo: t('menu.novo'), submenu: await submenuNovo(state.caminho) });
    }
  }

  ui.menuContexto(evento.clientX, evento.clientY, itens);
}

function criado(caminho) {
  recarregar();
  if (caminho) navegar(caminho);
}

// ---------- ações ----------

async function novaPasta(pai = state.caminho) {
  if (!pai) return;
  const nome = await dialogs.pedirTexto({
    titulo: t('menu.novaPasta'),
    rotulo: t('pasta.nome'),
    valor: '',
  });
  if (!nome) return;

  await criarPasta(pai, nome);
}

async function criarPasta(pai, nome) {
  try {
    const resultado = await call('fs.mkdir', { pai, nome });
    ui.toast(t('toast.pastaCriada', { nome: fmt.nomearArquivo(resultado.caminho) }), 'ok');
    recarregar();
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
}

/** Submenu "Novo": pastas do modelo que fazem sentido dentro desta pasta. */
async function submenuNovo(pasta) {
  const itens = [{ rotulo: t('menu.pastaEmBranco'), acao: () => novaPasta(pasta) }];

  let filhos = [];
  try {
    const resposta = await call('project.filhosModelo', { caminho: pasta });
    filhos = resposta?.filhos ?? [];
  } catch {
    filhos = [];
  }

  if (filhos.length) {
    itens.push({ separador: true });
    for (const filho of filhos) {
      itens.push({
        rotulo: filho.existe ? `${filho.nome} — ${t('menu.jaExiste')}` : filho.nome,
        desabilitado: filho.existe,
        acao: () => criarPasta(pasta, filho.nome),
      });
    }
  }

  return itens;
}

async function renomearSelecionado() {
  const [item] = selecionados();
  if (!item) return;

  const caminho = item.Caminho ?? item.caminho;
  const nomeAtual = item.Nome ?? item.nome;
  const novo = await dialogs.pedirTexto({
    titulo: t('menu.renomear'),
    rotulo: t('projeto.nome'),
    valor: nomeAtual,
  });

  if (!novo || novo === nomeAtual) return;

  try {
    await call('fs.rename', { caminho, novoNome: novo });
    ui.toast(t('toast.renomeado', { nome: novo }), 'ok');
    recarregar();
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
}

async function excluirSelecionado() {
  const selecao = selecionados();
  if (selecao.length === 0) return;

  const ok = await dialogs.confirmar({
    titulo: t('menu.excluir'),
    mensagem: selecao.length === 1
      ? fmt.nomearArquivo(selecao[0].Caminho ?? selecao[0].caminho)
      : `${selecao.length} itens`,
    perigo: true,
  });

  if (!ok) return;

  try {
    await call('fs.delete', { caminhos: selecao.map((i) => i.Caminho ?? i.caminho) });
    ui.toast(t('toast.excluido', { n: selecao.length }), 'ok');
    limparSelecao();
    preview.limpar();
    recarregar();
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
}

function copiar() {
  const selecao = selecionados();
  if (selecao.length === 0) return;
  state.clip = { caminhos: selecao.map((i) => i.Caminho ?? i.caminho), recortar: false };
  call('fs.clipboardSet', { caminhos: state.clip.caminhos }).catch(() => {});
  ui.toast(t('toast.copiado', { n: selecao.length }), 'ok', 2200);
}

function recortar() {
  const selecao = selecionados();
  if (selecao.length === 0) return;
  state.clip = { caminhos: selecao.map((i) => i.Caminho ?? i.caminho), recortar: true };
  call('fs.clipboardSet', { caminhos: state.clip.caminhos }).catch(() => {});
  ui.toast(t('toast.copiado', { n: selecao.length }), 'ok', 2200);
}

export async function colar() {
  if (!state.caminho) return;

  let caminhos = state.clip.caminhos;
  let recortarItens = state.clip.recortar;

  if (caminhos.length === 0) {
    try {
      const resposta = await call('fs.clipboardGet');
      caminhos = resposta?.caminhos ?? [];
    } catch {
      caminhos = [];
    }
    recortarItens = false;
  }

  if (caminhos.length === 0) {
    ui.toast(t('toast.nadaParaColar'), 'aviso');
    return;
  }

  await colarCaminhos(caminhos, recortarItens);
}

async function colarCaminhos(caminhos, recortarItens) {
  const destino = state.caminho;
  const opId = `colar-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  const videos = caminhos.filter((c) => fmt.ehVideo(c));
  const converterConfigurado = videos.length > 0 && state.config.ConverterVideoAoColar !== false;

  let converter = converterConfigurado;

  if (converterConfigurado && state.config.PerguntarAntesDeConverter !== false) {
    const escolha = await dialogs.confirmarConversao(videos, caminhos.length, state.config);
    if (!escolha) return;

    if (escolha.naoPerguntar) {
      state.config.PerguntarAntesDeConverter = false;
      call('settings.save', state.config).catch(() => {});
    }

    // "Colar original": o arquivo entra como está, sem reencode.
    converter = escolha.acao === 'converter';
  }

  try {
    if (converter) {
      const card = ui.progresso({ id: opId, titulo: t('progresso.convertendo'), subtitulo: '' });
      card.aoCancelar = () => call('media.cancel', { opId }).catch(() => {});

      try {
        const resultado = await call('media.paste', { origens: caminhos, destino, opId });
        card.fechar(400);

        if (resultado?.erros?.length) {
          ui.toast(resultado.erros[0], 'erro', 9000);
        }

        const convertidos = resultado?.convertidos?.length ?? 0;
        const copiados = resultado?.copiados?.length ?? 0;
        if (convertidos + copiados > 0) {
          ui.toast(t('toast.colado', { n: convertidos + copiados }), 'ok', 2600);
        }

        // Recortar + converter: o original sai depois que a conversao deu certo.
        if (recortarItens && convertidos > 0 && copiados === 0) {
          await call('fs.delete', { caminhos }).catch(() => {});
        }
      } finally {
        // Nunca deixa card pendurado, mesmo se a chamada falhar.
        card.fechar(600);
      }
    } else if (recortarItens) {
      const card = ui.progresso({ id: opId, titulo: t('progresso.movendo', { n: caminhos.length }) });
      card.aoCancelar = () => call('fs.cancelar', { opId }).catch(() => {});
      await call('fs.move', { origens: caminhos, destino, opId });
      card.fechar(0);
    } else {
      const card = ui.progresso({ id: opId, titulo: t('progresso.copiando', { n: caminhos.length }) });
      card.aoCancelar = () => call('fs.cancelar', { opId }).catch(() => {});
      await call('fs.copy', { origens: caminhos, destino, opId });
      card.fechar(0);
    }
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }

  state.clip = { caminhos: [], recortar: false };
  recarregar();
}

// ---------- busca ----------

function aoDigitarBusca() {
  const termo = buscaInput.value.trim();
  btnLimparBusca.hidden = termo.length === 0;

  clearTimeout(temporizadorBusca);
  temporizadorBusca = setTimeout(() => {
    if (termo.length === 0) {
      sairDaBusca();
    } else {
      iniciarBusca(termo);
    }
  }, 320);
}

async function iniciarBusca(termo) {
  if (state.busca.opId) {
    call('search.cancel', { opId: state.busca.opId }).catch(() => {});
  }

  const opId = `busca-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  state.busca = { ativa: true, termo, opId, resultados: [], pastas: 0, carregando: true };
  state.selecao.clear();
  area.scrollTop = 0;
  desenhar();
  atualizarStatus();

  try {
    await call('search.start', { raiz: state.caminho, texto: termo, opId });
  } catch (erro) {
    if (!String(erro.message).includes('cancelada')) {
      ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
    }
  } finally {
    if (state.busca.opId === opId) {
      state.busca.carregando = false;
      ordenarListas();
      desenhar();
    }
  }
}

function sairDaBusca({ silencioso = false } = {}) {
  if (state.busca.opId && !silencioso) {
    call('search.cancel', { opId: state.busca.opId }).catch(() => {});
  }

  const estavaAtiva = state.busca.ativa;
  state.busca = { ativa: false, termo: '', opId: null, resultados: [], pastas: 0, carregando: false };
  buscaInput.value = state.busca.termo;
  btnLimparBusca.hidden = true;

  if (estavaAtiva && !silencioso) {
    ordenarListas();
    desenhar();
  }
}

// ---------- visualização ----------

function alternarVisao() {
  state.visao = state.visao === 'grade' ? 'lista' : 'grade';
  localStorage.setItem('cvexplorer:visao', state.visao);
  document.getElementById('btn-visualizacao').classList.toggle('ativo', state.visao === 'lista');

  if (state.visao === 'grade') {
    cabecalhoEl.hidden = true;
  }

  area.scrollTop = 0;
  desenhar();
}

// ---------- teclado ----------

function aoTeclar(evento) {
  const alvo = evento.target;
  const digitando = alvo && (alvo.tagName === 'INPUT' || alvo.tagName === 'TEXTAREA' || alvo.isContentEditable);

  if (evento.key === 'F2' && !digitando) {
    evento.preventDefault();
    renomearSelecionado();
    return;
  }

  if (digitando) return;

  const ctrl = evento.ctrlKey || evento.metaKey;

  if (ctrl && evento.key.toLowerCase() === 'l') {
    evento.preventDefault();
    entrarEdicaoEndereco();
    return;
  }

  if (ctrl && evento.key.toLowerCase() === 'c') { evento.preventDefault(); copiar(); return; }
  if (ctrl && evento.key.toLowerCase() === 'x') { evento.preventDefault(); recortar(); return; }
  if (ctrl && evento.key.toLowerCase() === 'v') { evento.preventDefault(); colar(); return; }
  if (ctrl && evento.key.toLowerCase() === 'a') {
    evento.preventDefault();
    state.selecao = new Set(itensAtuais().map((i) => i.Caminho ?? i.caminho));
    aplicarSelecao();
    return;
  }
  if (ctrl && evento.key.toLowerCase() === 'f') {
    evento.preventDefault();
    buscaInput.focus();
    buscaInput.select();
    return;
  }
  if (evento.key === 'Delete') { evento.preventDefault(); excluirSelecionado(); return; }
  if (evento.key === 'Enter') {
    const [primeiro] = selecionados();
    if (primeiro) { evento.preventDefault(); abrir(primeiro); }
    return;
  }
  if (evento.key === 'Backspace') { evento.preventDefault(); subir(); return; }
  if (evento.key === 'Escape') {
    if (state.busca.ativa) { buscaInput.value = ''; sairDaBusca(); }
    else { limparSelecao(); aplicarSelecao(); preview.limpar(); }
    return;
  }

  if (evento.key.startsWith('Arrow')) {
    moverSelecao(evento);
    return;
  }

  // Type-ahead: digitar vai direto para o item que combina (como no Explorer).
  if (!ctrl && !evento.altKey && evento.key.length === 1 && evento.key !== ' ') {
    evento.preventDefault();
    tratarDigitacao(evento.key);
  }
}

function moverSelecao(evento) {
  const itens = itensAtuais();
  if (itens.length === 0) return;
  evento.preventDefault();

  const selecao = selecionados();
  const atual = selecao.length > 0
    ? itens.findIndex((i) => (i.Caminho ?? i.caminho) === (selecao[0].Caminho ?? selecao[0].caminho))
    : -1;

  let incremento = 1;
  if (state.visao === 'grade') {
    const largura = Math.max(96, state.grade);
    const colunas = Math.max(1, Math.floor((area.clientWidth - PAD * 2 - 8 + GAP) / (largura + GAP)));
    if (evento.key === 'ArrowUp') incremento = -colunas;
    if (evento.key === 'ArrowDown') incremento = colunas;
    if (evento.key === 'ArrowLeft') incremento = -1;
    if (evento.key === 'ArrowRight') incremento = 1;
  } else {
    if (evento.key === 'ArrowUp') incremento = -1;
    if (evento.key === 'ArrowDown') incremento = 1;
    if (evento.key === 'ArrowLeft') incremento = 0;
    if (evento.key === 'ArrowRight') incremento = 0;
  }

  const proximo = Math.max(0, Math.min(itens.length - 1, atual + incremento));
  const item = itens[proximo];
  if (!item) return;

  const caminho = item.Caminho ?? item.caminho;
  if (!evento.shiftKey) state.selecao.clear();
  state.selecao.add(caminho);
  state.ancora = proximo;

  aplicarSelecao();
  preview.mostrar(selecionados()).catch(() => {});

  const el = area.querySelector(`[data-caminho="${CSS.escape(caminho)}"]`);
  el?.scrollIntoView({ block: 'nearest' });
}

export function focoBusca() {
  buscaInput.focus();
  buscaInput.select();
}

// ---------- barra de endereço editável ----------

function iniciarEndereco() {
  areaEndereco.addEventListener('dblclick', (evento) => {
    if (!evento.target.closest('.migalha')) {
      entrarEdicaoEndereco();
    }
  });

  entradaEndereco.addEventListener('keydown', (evento) => {
    if (evento.key === 'Enter') {
      evento.preventDefault();
      irParaEndereco();
    } else if (evento.key === 'Escape') {
      evento.preventDefault();
      sairEdicaoEndereco();
    }
  });

  entradaEndereco.addEventListener('blur', sairEdicaoEndereco);
}

function entrarEdicaoEndereco() {
  if (!state.caminho) return;

  entradaEndereco.value = state.caminho;
  entradaEndereco.hidden = false;
  migalhas.hidden = true;
  entradaEndereco.focus();

  requestAnimationFrame(() => entradaEndereco.select());
}

function sairEdicaoEndereco() {
  entradaEndereco.hidden = true;
  migalhas.hidden = false;
}

async function irParaEndereco() {
  const bruto = entradaEndereco.value.trim().replace(/^"|"$/g, '');
  if (!bruto) {
    sairEdicaoEndereco();
    return;
  }

  try {
    const stat = await call('fs.stat', { caminho: bruto });
    if (!stat?.pasta) {
      ui.toast(t('endereco.invalido', { caminho: bruto }), 'aviso');
      return;
    }

    sairEdicaoEndereco();
    await navegar(bruto);
  } catch {
    ui.toast(t('endereco.invalido', { caminho: bruto }), 'aviso');
  }
}

// ---------- digitação rápida (type-ahead) ----------

function normalizarTexto(texto) {
  return (texto ?? '')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();
}

function tratarDigitacao(caractere) {
  const agora = Date.now();
  const novaSessao = agora - horaDigitacao > 900;

  bufferDigitacao = novaSessao ? caractere : bufferDigitacao + caractere;
  horaDigitacao = agora;
  mostrarPillDigitacao(bufferDigitacao);

  const itens = itensAtuais();
  if (itens.length === 0) return;

  const prefixo = normalizarTexto(bufferDigitacao);
  const atual = state.ancora ?? -1;
  // Primeira letra: avança para a próxima combinação. Refinando: mantém a atual.
  const inicio = atual < 0 ? 0 : (novaSessao ? atual + 1 : atual);

  for (let passo = 0; passo < itens.length; passo++) {
    const indice = (inicio + passo) % itens.length;
    const nome = normalizarTexto(itens[indice].Nome ?? itens[indice].nome);
    if (nome.startsWith(prefixo)) {
      selecionarIndice(indice);
      return;
    }
  }
}

function mostrarPillDigitacao(texto) {
  pillDigitacao.textContent = texto;
  pillDigitacao.hidden = false;

  clearTimeout(temporizadorDigitacao);
  temporizadorDigitacao = setTimeout(() => {
    pillDigitacao.hidden = true;
    bufferDigitacao = '';
  }, 1200);
}

function selecionarIndice(indice) {
  const itens = itensAtuais();
  const item = itens[indice];
  if (!item) return;

  const caminho = item.Caminho ?? item.caminho;
  state.selecao.clear();
  state.selecao.add(caminho);
  state.ancora = indice;

  aplicarSelecao();
  preview.mostrar(selecionados()).catch(() => {});

  area.querySelector(`[data-caminho="${CSS.escape(caminho)}"]`)
    ?.scrollIntoView({ block: 'nearest' });
}
