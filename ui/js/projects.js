import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as ui from './ui.js';
import * as dialogs from './dialogs.js';
import { state } from './state.js';

let aoNavegarExterno = () => {};

export function iniciarLateral({ aoNavegar }) {
  aoNavegarExterno = aoNavegar;

  document.getElementById('btn-projetos-atualizar').addEventListener('click', () => detectarProjetos(true));
  document.getElementById('btn-add-raiz').addEventListener('click', adicionarRaiz);
  document.addEventListener('projetos:atualizar', () => detectarProjetos(false));
}

export async function carregarTudo() {
  await carregarRaizes();
  if ((state.config.Raizes ?? []).length > 0) {
    await detectarProjetos(false);
  } else {
    const lista = document.getElementById('lista-projetos');
    lista.replaceChildren(vazio(t('lateral.semProjetos')));
  }
}

function vazio(texto) {
  const el = document.createElement('div');
  el.className = 'side-empty';
  el.textContent = texto;
  return el;
}

async function carregarRaizes() {
  const lista = document.getElementById('lista-raizes');
  try {
    const raizes = await call('fs.roots');
    lista.replaceChildren();

    if (!raizes?.length) {
      lista.appendChild(vazio(t('lateral.semRaizes')));
      return;
    }

    for (const raiz of raizes) {
      const botao = document.createElement('button');
      botao.className = 'side-item';
      botao.dataset.caminho = raiz.caminho;

      const rotulo = document.createElement('span');
      rotulo.className = 'rotulo';
      rotulo.textContent = raiz.nome || raiz.caminho;
      rotulo.title = raiz.caminho;

      const detalhe = document.createElement('span');
      detalhe.className = 'detalhe';
      detalhe.textContent = raiz.livreTexto ?? '';

      botao.append(rotulo, detalhe);
      botao.addEventListener('click', () => navegar(raiz.caminho));
      lista.appendChild(botao);
    }
  } catch (erro) {
    lista.replaceChildren(vazio(erro.message));
  }
}

async function detectarProjetos(mostrarToast) {
  const lista = document.getElementById('lista-projetos');
  const raizes = state.config.Raizes ?? [];

  if (raizes.length === 0) {
    lista.replaceChildren(vazio(t('lateral.semProjetos')));
    return;
  }

  lista.replaceChildren(vazio(t('lateral.detectando')));

  try {
    const projetos = await call('project.detect', { raizes, profundidade: 2 });
    lista.replaceChildren();

    if (!projetos?.length) {
      lista.appendChild(vazio(t('lateral.semProjetos')));
      if (mostrarToast) ui.toast(t('toast.semProjetos'), 'aviso');
      return;
    }

    for (const projeto of projetos) {
      const botao = document.createElement('button');
      botao.className = 'side-item';
      botao.dataset.caminho = projeto.Caminho;

      const rotulo = document.createElement('span');
      rotulo.className = 'rotulo';
      rotulo.textContent = projeto.Nome;
      rotulo.title = projeto.Caminho;

      const detalhe = document.createElement('span');
      detalhe.className = 'detalhe';
      detalhe.textContent = projeto.Registrado ? (projeto.Marcador?.Tipo ?? '') : t('lateral.naoRegistrado');

      botao.append(rotulo, detalhe);
      botao.addEventListener('click', () => navegar(projeto.Caminho));
      botao.addEventListener('contextmenu', (evento) => {
        evento.preventDefault();
        ui.menuContexto(evento.clientX, evento.clientY, [
          { rotulo: t('menu.abrir'), acao: () => navegar(projeto.Caminho) },
          { rotulo: t('menu.abrirExplorer'), acao: () => call('fs.reveal', { caminho: projeto.Caminho }) },
          { separador: true },
          {
            rotulo: t('menu.registrarProjeto'),
            desabilitado: projeto.Registrado,
            acao: async () => { await dialogs.registrarProjeto(projeto.Caminho); await detectarProjetos(false); },
          },
          { rotulo: t('menu.criarPastasModelo'), acao: () => dialogs.pastasModelo(projeto.Caminho, () => detectarProjetos(false)) },
        ]);
      });

      lista.appendChild(botao);
    }

    if (mostrarToast) ui.toast(t('toast.projetoDetectado', { n: projetos.length }), 'ok', 2600);
    marcarAtivo();
  } catch (erro) {
    lista.replaceChildren(vazio(erro.message));
  }
}

async function adicionarRaiz() {
  const resultado = await call('fs.pickFolder', { titulo: t('lateral.adicionarRaiz'), inicial: state.config.Raizes?.[0] ?? '' });
  if (!resultado?.caminho) return;

  const raizes = new Set(state.config.Raizes ?? []);
  raizes.add(resultado.caminho);
  state.config.Raizes = [...raizes];

  await call('settings.save', state.config).catch(() => {});
  await carregarTudo();
  await navegar(resultado.caminho);
}

function navegar(caminho) {
  aoNavegarExterno(caminho);
  setTimeout(marcarAtivo, 60);
}

function marcarAtivo() {
  for (const el of document.querySelectorAll('.side-item')) {
    const ativo = state.caminho && el.dataset.caminho
      && state.caminho.toLowerCase().startsWith(el.dataset.caminho.toLowerCase());
    el.classList.toggle('ativo', Boolean(ativo));
  }

  const botao = document.querySelector(`.side-item[data-caminho="${CSS.escape(state.caminho ?? '')}"]`);
  botao?.classList.add('ativo');
}

export function atualizarRaizes() {
  carregarRaizes();
}

export function formatarProjeto(projeto) {
  return `${projeto.Nome}${projeto.Registrado ? '' : ` · ${t('lateral.naoRegistrado')}`}`;
}

export { fmt };
