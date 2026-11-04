import { call, on } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as ui from './ui.js';
import * as thumbs from './thumbnails.js';
import * as preview from './preview.js';
import * as dialogs from './dialogs.js';
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
  window.addEventListener('resize', () => {
    desenhar();
    if (state.caminho) atualizarMigalhas(state.caminho);
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
    if (!state.caminho || !dados?.caminhos?.length) return;
    await colarCaminhos(dados.caminhos, false);
  });

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

// ---------- navegação ----------

export async function navegar(caminho, { historico = true } = {}) {
  if (!caminho) return;

  sairDaBusca({ silencioso: true });
  state.caminho = caminho;
  state.itens = [];
  state.carregando = true;
  limparSelecao();
  preview.fechar();
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
  } else {
    comparacao = ORDENADOR.compare(nomeA, nomeB);
  }

  if (comparacao === 0) {
    comparacao = ORDENADOR.compare(nomeA, nomeB);
  }

  return desc ? -comparacao : comparacao;
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

function aplicarColunas() {
  // Na raiz: vale para o cabecalho (fora do scroller) e para as linhas.
  const raiz = document.documentElement;
  raiz.style.setProperty('--w-tam', `${state.colunas.tamanho}px`);
  raiz.style.setProperty('--w-data', `${state.colunas.data}px`);
}

function garantirAlca(coluna) {
  const alca = document.createElement('div');
  alca.className = 'alca';
  alca.title = t('coluna.redimensionar');

  // Captura de ponteiro: os eventos continuam chegando mesmo se o mouse
  // sair do cabecalho durante o arraste.
  alca.addEventListener('pointerdown', (evento) => {
    evento.preventDefault();
    evento.stopPropagation();

    const inicioX = evento.clientX;
    const larguraInicial = state.colunas[coluna];
    try { alca.setPointerCapture(evento.pointerId); } catch { /* ponteiro sintetico */ }
    document.body.classList.add('redimensionando');

    const mover = (e) => {
      const largura = Math.max(56, Math.min(460, larguraInicial + (e.clientX - inicioX)));
      state.colunas[coluna] = Math.round(largura);
      aplicarColunas();
    };

    const soltar = () => {
      alca.removeEventListener('pointermove', mover);
      alca.removeEventListener('pointerup', soltar);
      alca.removeEventListener('pointercancel', soltar);
      document.body.classList.remove('redimensionando');
      try { alca.releasePointerCapture(evento.pointerId); } catch { /* ja liberado */ }
      localStorage.setItem('cvexplorer:colunas', JSON.stringify(state.colunas));
      desenhar();
    };

    alca.addEventListener('pointermove', mover);
    alca.addEventListener('pointerup', soltar);
    alca.addEventListener('pointercancel', soltar);
  });

  alca.addEventListener('click', (e) => e.stopPropagation());
  return alca;
}

function carregarPreferencias() {
  try {
    const colunas = JSON.parse(localStorage.getItem('cvexplorer:colunas') ?? 'null');
    if (colunas && typeof colunas.tamanho === 'number' && typeof colunas.data === 'number') {
      state.colunas = {
        tamanho: Math.min(460, Math.max(56, colunas.tamanho)),
        data: Math.min(460, Math.max(56, colunas.data)),
      };
    }

    const ordenacao = JSON.parse(localStorage.getItem('cvexplorer:ordenacao') ?? 'null');
    if (ordenacao && ['nome', 'tamanho', 'modificado'].includes(ordenacao.campo)) {
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
    { campo: 'nome', texto: t('coluna.nome') },
    { campo: 'tamanho', texto: t('preview.tamanho'), redimensionavel: 'tamanho' },
    { campo: 'modificado', texto: t('preview.modificado'), redimensionavel: 'data' },
  ];

  cabecalhoEl.replaceChildren();

  for (const coluna of colunas) {
    if (!coluna.campo) {
      cabecalhoEl.appendChild(document.createElement('span'));
      continue;
    }

    const celula = document.createElement('button');
    celula.className = 'cabecalho-celula';
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
    iconeEl.innerHTML = icone(cat);
    thumb.appendChild(iconeEl);

    if (usarMiniatura) {
      thumbs.solicitar(item, Math.round(largura * (window.devicePixelRatio || 1)), (url) => {
        if (!telha.isConnected) return;
        const img = document.createElement('img');
        img.src = url;
        img.alt = '';
        thumb.replaceChildren(img);
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
    iconeEl.innerHTML = icone(ehPasta ? 'pasta' : fmt.categoria(ext));

    const nomeEl = document.createElement('span');
    nomeEl.className = 'row-nome';
    nomeEl.textContent = nome;
    nomeEl.title = caminho;

    const tamanhoEl = document.createElement('span');
    tamanhoEl.className = 'row-tamanho';
    tamanhoEl.textContent = ehPasta ? '' : fmt.bytes(item.Tamanho ?? item.tamanho);

    const dataEl = document.createElement('span');
    dataEl.className = 'row-data';
    dataEl.textContent = fmt.dataCurta(item.Modificado ?? item.modificado);

    linha.append(iconeEl, nomeEl, tamanhoEl, dataEl);
    fragmento.appendChild(linha);
  }

  area.querySelectorAll('.tile,.row').forEach((el) => el.remove());
  area.appendChild(fragmento);
}

function atualizarStatus() {
  const itens = itensAtuais();
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

function aoClicar(evento) {
  const alvo = evento.target.closest('[data-caminho]');

  if (!alvo) {
    if (!evento.ctrlKey && !evento.metaKey) {
      limparSelecao();
      aplicarSelecao();
      preview.fechar();
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

function aoMenuContexto(evento) {
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
    }

    if (ehPasta && umSo) {
      itens.push(
        { rotulo: t('menu.registrarProjeto'), acao: () => dialogs.registrarProjeto(primeiro.Caminho ?? primeiro.caminho) },
        { rotulo: t('menu.criarPastasModelo'), acao: () => dialogs.pastasModelo(primeiro.Caminho ?? primeiro.caminho, recarregar) },
      );
    }

    itens.push({ separador: true });
    itens.push({
      rotulo: t('menu.copiarCaminho'),
      acao: () => navigator.clipboard?.writeText(selecao.map((i) => i.Caminho ?? i.caminho).join('\n')),
    });
  } else {
    itens.push({ rotulo: t('menu.novaPasta'), acao: novaPasta });
    itens.push({ rotulo: t('menu.colar'), atalho: 'Ctrl+V', acao: colar });
    itens.push({ separador: true });
    itens.push({ rotulo: t('menu.novoProjeto'), acao: () => dialogs.novoProjeto(state.caminho, criado) });
  }

  ui.menuContexto(evento.clientX, evento.clientY, itens);
}

function criado(caminho) {
  recarregar();
  if (caminho) navegar(caminho);
}

// ---------- ações ----------

async function novaPasta() {
  if (!state.caminho) return;
  const nome = await dialogs.pedirTexto({
    titulo: t('menu.novaPasta'),
    rotulo: t('projeto.nome'),
    valor: '',
  });
  if (!nome) return;

  try {
    const resultado = await call('fs.mkdir', { pai: state.caminho, nome });
    ui.toast(t('toast.pastaCriada', { nome: fmt.nomearArquivo(resultado.caminho) }), 'ok');
    recarregar();
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
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
    preview.fechar();
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
  const temVideo = caminhos.some((c) => fmt.ehVideo(c));
  const converter = temVideo && state.config.ConverterVideoAoColar !== false;

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
    else { limparSelecao(); aplicarSelecao(); preview.fechar(); }
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
