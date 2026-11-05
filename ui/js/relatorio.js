import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';

let pastaAtual = null;

/**
 * Janela de relatório: soma o tamanho de cada subpasta direta e mostra do maior
 * para o menor. A varredura acontece no host (fs.pastasMaiores), em paralelo.
 */
export function iniciar(pasta) {
  pastaAtual = pasta;

  const raiz = document.getElementById('relatorio');
  if (!raiz) return;

  raiz.hidden = false;
  document.getElementById('relatorio-nome').textContent = fmt.nomearArquivo(pasta) || pasta;
  document.getElementById('relatorio-nome').title = pasta;
  document.getElementById('relatorio-atualizar').addEventListener('click', () => carregar());

  document.addEventListener('keydown', (evento) => {
    if (evento.key === 'F5') {
      evento.preventDefault();
      carregar();
    }
  });

  carregar();
}

async function carregar() {
  const corpo = document.getElementById('relatorio-corpo');
  const resumo = document.getElementById('relatorio-resumo');

  resumo.textContent = t('relatorio.calculando');
  corpo.replaceChildren(spinner());

  let resposta;
  try {
    resposta = await call('fs.pastasMaiores', { caminho: pastaAtual });
  } catch (erro) {
    resumo.textContent = '';
    corpo.replaceChildren(aviso(t('erro.geral', { mensagem: erro.message })));
    return;
  }

  if (!resposta?.ok) {
    resumo.textContent = '';
    corpo.replaceChildren(aviso(resposta?.erro ?? t('relatorio.semPastas')));
    return;
  }

  const pastas = resposta.pastas ?? [];
  resumo.textContent = t('relatorio.resumo', {
    n: pastas.length,
    arquivos: resposta.arquivos,
    total: fmt.bytes(resposta.totalBytes),
  });

  if (pastas.length === 0) {
    corpo.replaceChildren(aviso(t('relatorio.semPastas')));
    return;
  }

  const maior = Math.max(...pastas.map((p) => p.bytes), 1);
  const total = resposta.totalBytes || 1;
  const fragmento = document.createDocumentFragment();

  pastas.forEach((pasta, indice) => {
    const linha = document.createElement('div');
    linha.className = 'relatorio-linha';
    linha.title = `${pasta.caminho}\n${t('relatorio.abrirDica')}`;

    const posicao = document.createElement('span');
    posicao.className = 'relatorio-posicao';
    posicao.textContent = String(indice + 1);

    const nome = document.createElement('span');
    nome.className = 'relatorio-pasta';
    nome.textContent = pasta.nome;

    const barra = document.createElement('span');
    barra.className = 'relatorio-barra';
    const preenchimento = document.createElement('i');
    preenchimento.style.width = `${Math.max(2, Math.round((pasta.bytes / maior) * 100))}%`;
    barra.appendChild(preenchimento);

    const tamanho = document.createElement('span');
    tamanho.className = 'relatorio-tamanho';
    tamanho.textContent = fmt.bytes(pasta.bytes);

    const parte = document.createElement('span');
    parte.className = 'relatorio-parte';
    parte.textContent = `${Math.round((pasta.bytes / total) * 100)}%`;

    const arquivos = document.createElement('span');
    arquivos.className = 'relatorio-arquivos';
    arquivos.textContent = t('relatorio.arquivos', { n: pasta.arquivos });

    linha.append(posicao, nome, barra, tamanho, parte, arquivos);
    linha.addEventListener('dblclick', () => {
      call('app.novaJanela', { caminho: pasta.caminho }).catch(() => {});
    });

    fragmento.appendChild(linha);
  });

  corpo.replaceChildren(fragmento);
}

function spinner() {
  const caixa = document.createElement('div');
  caixa.className = 'previa-preparando';
  const rotulo = document.createElement('div');
  rotulo.className = 'placeholder';
  rotulo.textContent = t('relatorio.calculando');
  const rodinha = document.createElement('div');
  rodinha.className = 'spinner';
  caixa.append(rodinha, rotulo);
  return caixa;
}

function aviso(texto) {
  const caixa = document.createElement('div');
  caixa.className = 'placeholder';
  caixa.textContent = texto;
  return caixa;
}

export function estaAtivo() {
  return Boolean(pastaAtual);
}
