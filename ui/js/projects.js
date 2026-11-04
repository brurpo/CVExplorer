import { call } from './bridge.js';
import { t } from './i18n.js';
import * as ui from './ui.js';
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

  for (const caminho of favoritos) {
    const nome = caminho.split(/[\\/]/).filter(Boolean).pop() ?? caminho;

    const botao = criarItem({
      rotulo: nome,
      detalhe: '',
      titulo: caminho,
      caminho,
      classe: 'favorito',
      aoClicar: () => navegar(caminho),
    });

    botao.addEventListener('contextmenu', (evento) => {
      evento.preventDefault();
      ui.menuContexto(evento.clientX, evento.clientY, [
        { rotulo: t('menu.abrir'), acao: () => navegar(caminho) },
        { rotulo: t('menu.abrirExplorer'), acao: () => call('fs.reveal', { caminho }) },
        { separador: true },
        { rotulo: t('lateral.removerFavorito'), perigo: true, acao: () => removerFavorito(caminho) },
      ]);
    });

    lista.appendChild(botao);
  }

  marcarAtivo();
}

async function adicionarFavorito() {
  const resultado = await call('fs.pickFolder', {
    titulo: t('lateral.adicionarFavorito'),
    inicial: state.caminho ?? '',
  });

  if (!resultado?.caminho) return;

  const favoritos = new Set(state.config.Favoritos ?? []);
  favoritos.add(resultado.caminho);
  state.config.Favoritos = [...favoritos];

  await call('settings.save', state.config).catch(() => {});
  await carregarFavoritos();
  ui.toast(t('lateral.favoritoAdicionado'), 'ok', 2400);
}

async function removerFavorito(caminho) {
  state.config.Favoritos = (state.config.Favoritos ?? []).filter((c) => c !== caminho);
  await call('settings.save', state.config).catch(() => {});
  await carregarFavoritos();
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

  for (const el of document.querySelectorAll('.side-item')) {
    const alvo = (el.dataset.caminho ?? '').toLowerCase();
    el.classList.toggle('ativo', Boolean(atual && alvo && atual.startsWith(alvo)));
  }
}
