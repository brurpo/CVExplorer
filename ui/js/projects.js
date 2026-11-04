import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as ui from './ui.js';
import { icone } from './icons.js';
import { state } from './state.js';

let aoNavegarExterno = () => {};

/**
 * Barra lateral no estilo do Explorer: Favoritos (pastas que o usuário fixa) e
 * Este Computador (unidades com letra e tipo). Projetos não entram aqui de
 * propósito — com milhares de projetos, quem diz onde você está é o chip do topo,
 * que resolve pela pasta atual.
 */
export function iniciarLateral({ aoNavegar }) {
  aoNavegarExterno = aoNavegar;

  document.getElementById('btn-add-favorito').addEventListener('click', adicionarFavorito);
  document.getElementById('btn-atualizar-drives').addEventListener('click', () => carregarDiscos(true));
}

export async function carregarTudo() {
  await Promise.all([carregarFavoritos(), carregarDiscos(false)]);
}

function vazio(texto) {
  const el = document.createElement('div');
  el.className = 'side-empty';
  el.textContent = texto;
  return el;
}

function criarItem({ rotulo, detalhe, titulo, caminho, classe, aoClicar }) {
  const botao = document.createElement('button');
  botao.className = `side-item${classe ? ' ' + classe : ''}`;
  botao.dataset.caminho = caminho;
  if (titulo) botao.title = titulo;

  const nome = document.createElement('span');
  nome.className = 'rotulo';
  nome.textContent = rotulo;
  botao.appendChild(nome);

  if (detalhe) {
    const det = document.createElement('span');
    det.className = 'detalhe';
    det.textContent = detalhe;
    botao.appendChild(det);
  }

  botao.addEventListener('click', aoClicar);
  return botao;
}

// ---------- favoritos ----------

async function carregarFavoritos() {
  const lista = document.getElementById('lista-favoritos');
  const favoritos = Array.isArray(state.config.Favoritos) ? state.config.Favoritos : [];
  lista.replaceChildren();

  if (favoritos.length === 0) {
    lista.appendChild(vazio(t('lateral.semFavoritos')));
    return;
  }

  // Um favorito pode ser pasta (navega) ou arquivo (abre no app padrão).
  const tipos = new Map();
  await Promise.all(favoritos.map(async (caminho) => {
    try {
      const info = await call('fs.stat', { caminho });
      tipos.set(caminho, Boolean(info?.pasta));
    } catch {
      tipos.set(caminho, true);
    }
  }));

  for (const caminho of favoritos) {
    const ehPasta = tipos.get(caminho) !== false;
    const nome = caminho.split(/[\\/]/).filter(Boolean).pop() ?? caminho;

    const botao = criarItem({
      rotulo: nome,
      detalhe: '',
      titulo: caminho,
      caminho,
      classe: ehPasta ? 'favorito' : 'favorito arquivo',
      aoClicar: () => (ehPasta ? navegar(caminho) : abrirArquivo(caminho)),
    });

    botao.prepend(iconeFavorito(nome, ehPasta));

    botao.addEventListener('contextmenu', (evento) => {
      evento.preventDefault();
      ui.menuContexto(evento.clientX, evento.clientY, [
        { rotulo: ehPasta ? t('menu.abrir') : t('lateral.abrirArquivo'), acao: () => (ehPasta ? navegar(caminho) : abrirArquivo(caminho)) },
        { rotulo: t('menu.abrirExplorer'), acao: () => call('fs.reveal', { caminho }) },
        { separador: true },
        { rotulo: t('lateral.removerFavorito'), perigo: true, acao: () => removerFavorito(caminho) },
      ]);
    });

    lista.appendChild(botao);
  }

  marcarAtivo();
}

function iconeFavorito(nome, ehPasta) {
  const span = document.createElement('span');
  span.className = 'side-icone';
  const ext = fmt.extensao(nome);
  span.innerHTML = icone(ehPasta ? 'pasta' : fmt.categoria(ext), ext);
  if (ehPasta) span.dataset.cat = 'pasta';
  return span;
}

function abrirArquivo(caminho) {
  call('fs.open', { caminho }).catch((erro) => {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  });
}

async function adicionarFavorito() {
  const resultado = await call('fs.pickFolder', {
    titulo: t('lateral.adicionarFavorito'),
    inicial: state.caminho ?? '',
  });

  if (!resultado?.caminho) return;
  await adicionarFavoritos([resultado.caminho]);
}

async function removerFavorito(caminho) {
  state.config.Favoritos = (state.config.Favoritos ?? []).filter((c) => c !== caminho);
  await call('settings.save', state.config).catch(() => {});
  await carregarFavoritos();
}

/** Área que aceita arraste para virar favorito. */
export function alvoFavoritos(x, y) {
  const secao = document.getElementById('secao-favoritos');
  if (!secao) return false;
  const alvo = document.elementFromPoint(x, y);
  return Boolean(alvo && secao.contains(alvo));
}

export function destacarFavoritos(ativo) {
  document.getElementById('secao-favoritos')?.classList.toggle('arraste-sobre', Boolean(ativo));
}

export async function adicionarFavoritos(caminhos) {
  const lista = caminhos.filter(Boolean);
  if (lista.length === 0) return;

  const favoritos = new Set(state.config.Favoritos ?? []);
  let novos = 0;

  for (const caminho of lista) {
    if (!favoritos.has(caminho)) {
      favoritos.add(caminho);
      novos++;
    }
  }

  if (novos === 0) {
    ui.toast(t('lateral.jaNosFavoritos'), 'info', 2200);
    return;
  }

  state.config.Favoritos = [...favoritos];
  await call('settings.save', state.config).catch(() => {});
  await carregarFavoritos();
  ui.toast(t('lateral.favoritosAdicionados', { n: novos }), 'ok', 2400);
}

// ---------- unidades ----------

async function carregarDiscos(mostrarErro) {
  const lista = document.getElementById('lista-drives');

  try {
    const unidades = await call('fs.roots');
    lista.replaceChildren();

    const pastasFixas = unidades.filter((u) => !['local', 'rede'].includes(u.tipo));
    const discos = unidades.filter((u) => ['local', 'rede'].includes(u.tipo));

    // Pastas adicionadas em Raizes aparecem como pastas, separadas das unidades.
    for (const pasta of pastasFixas) {
      lista.appendChild(criarItem({
        rotulo: pasta.nome || pasta.caminho,
        detalhe: '',
        titulo: pasta.caminho,
        caminho: pasta.caminho,
        classe: 'pasta-fixa',
        aoClicar: () => navegar(pasta.caminho),
      }));
    }

    for (const disco of discos) {
      const letra = disco.letra ? `${disco.letra}:` : '';
      const etiqueta = disco.nome && disco.nome !== disco.letra ? disco.nome : '';
      const tipo = disco.tipo === 'rede' ? t('lateral.rede') : t('lateral.local');

      lista.appendChild(criarItem({
        rotulo: etiqueta ? `${letra} ${etiqueta}` : (letra || disco.nome),
        detalhe: disco.tipo === 'rede' ? tipo : (disco.livreTexto ?? tipo),
        titulo: `${disco.caminho} — ${tipo}`,
        caminho: disco.caminho,
        classe: disco.tipo === 'rede' ? 'disco rede' : 'disco',
        aoClicar: () => navegar(disco.caminho),
      }));
    }

    if (!lista.children.length) {
      lista.appendChild(vazio(t('lateral.semUnidades')));
    }

    marcarAtivo();
  } catch (erro) {
    lista.replaceChildren(vazio(erro.message));
    if (mostrarErro) ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
}

// ---------- navegação ----------

function navegar(caminho) {
  aoNavegarExterno(caminho);
  setTimeout(marcarAtivo, 80);
}

function marcarAtivo() {
  const atual = (state.caminho ?? '').toLowerCase();
  const itens = [...document.querySelectorAll('.side-item')];

  // Só o caminho mais específico fica ativo: "C:\" não pode marcar junto com "C:\Users\x".
  let melhor = null;
  let tamanhoMelhor = -1;

  for (const el of itens) {
    const alvo = (el.dataset.caminho ?? '').toLowerCase();
    if (!alvo || !atual.startsWith(alvo)) continue;
    if (alvo.length > tamanhoMelhor) {
      tamanhoMelhor = alvo.length;
      melhor = el;
    }
  }

  for (const el of itens) {
    el.classList.toggle('ativo', el === melhor);
  }
}
