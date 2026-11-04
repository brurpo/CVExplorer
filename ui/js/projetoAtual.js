import { call } from './bridge.js';
import { t } from './i18n.js';
import * as ui from './ui.js';
import * as dialogs from './dialogs.js';
import * as fmt from './format.js';

const botao = document.getElementById('chip-projeto');
const rotulo = document.getElementById('chip-projeto-nome');

let projeto = null;
let caminhoAtual = null;

export function iniciarProjetoAtual() {
  botao.addEventListener('click', () => {
    abrirMenu().catch((erro) => ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro'));
  });
  document.addEventListener('projetos:atualizar', () => atualizar(caminhoAtual));
}

/**
 * Projetos ficam em qualquer lugar: a cada navegacao subimos pelos pais
 * procurando o arquivo marcador. Sem marcador, a pasta simplesmente nao tem projeto.
 */
export async function atualizar(caminho) {
  caminhoAtual = caminho;
  try {
    projeto = await call('project.resolve', { caminho });
  } catch {
    projeto = null;
  }

  desenhar();
}

function desenhar() {
  if (projeto) {
    const relativo = caminhoAtual
      && caminhoAtual.toLowerCase().startsWith(projeto.Caminho.toLowerCase())
      ? caminhoAtual.slice(projeto.Caminho.length).replace(/^[\\/]+/, '')
      : '';

    rotulo.textContent = projeto.Nome;
    botao.classList.add('dentro');
    botao.title = relativo ? `${projeto.Nome} › ${relativo.replace(/\\/g, ' › ')}` : projeto.Nome;
  } else {
    rotulo.textContent = t('projeto.semProjeto');
    botao.classList.remove('dentro');
    botao.title = t('projeto.foraDeProjeto');
  }
}

function navegar(caminho) {
  document.dispatchEvent(new CustomEvent('navegar', { detail: caminho }));
}

async function abrirMenu() {
  const rect = botao.getBoundingClientRect();
  const itens = [];

  if (projeto) {
    const tipo = projeto.Marcador?.Tipo;
    itens.push({ cabecalho: tipo ? `${projeto.Nome} · ${tipo}` : projeto.Nome });
    itens.push({ rotulo: t('projeto.raiz'), acao: () => navegar(projeto.Caminho) });

    let atalhos = [];
    try {
      atalhos = (await call('project.shortcuts', { raiz: projeto.Caminho })) ?? [];
    } catch {
      atalhos = [];
    }

    let grupo = null;
    for (const atalho of atalhos) {
      if (atalho.Grupo !== grupo) {
        grupo = atalho.Grupo;
        itens.push({ cabecalho: grupo });
      }

      itens.push({
        rotulo: atalho.Nome,
        atalho: atalho.Caminho.toLowerCase() === (caminhoAtual ?? '').toLowerCase() ? 'aqui' : '',
        acao: () => navegar(atalho.Caminho),
      });
    }

    itens.push({ separador: true });
    itens.push({
      rotulo: t('projeto.abrirRaizExplorer'),
      acao: () => call('fs.reveal', { caminho: projeto.Caminho }),
    });
  } else {
    itens.push({ cabecalho: t('projeto.foraDeProjeto') });
    itens.push({ rotulo: t('projeto.definirAqui'), acao: definirAqui });
    itens.push({
      rotulo: t('projeto.criarAqui'),
      acao: () => dialogs.novoProjeto(caminhoAtual, (criado) => {
        if (criado) navegar(criado);
      }),
    });
    itens.push({
      rotulo: t('menu.novoProjeto'),
      acao: () => dialogs.novoProjeto(fmt.pastaDe(caminhoAtual ?? ''), (criado) => {
        if (criado) navegar(criado);
      }),
    });
  }

  ui.menuContexto(rect.left, rect.bottom + 6, itens);
}

async function definirAqui() {
  if (!caminhoAtual) return;
  await dialogs.registrarProjeto(caminhoAtual);
  await atualizar(caminhoAtual);
}
