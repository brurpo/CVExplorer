import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import { on } from './bridge.js';

const painel = document.getElementById('preview');
const nomeEl = document.getElementById('preview-nome');
const corpoEl = document.getElementById('preview-corpo');
const metaEl = document.getElementById('preview-meta');

const RENDERIZAVEIS = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'avif'];
const REPRODUZIVEIS = ['mp4', 'webm', 'm4v', 'ogv'];

let baseConteudo = null;
let baseAtual = null;

export function fechar() {
  painel.hidden = true;
  corpoEl.replaceChildren();
  metaEl.replaceChildren();
}

export async function fecharSeVazio() {
  fechar();
}

export async function mostrar(items) {
  if (items.length !== 1) {
    if (items.length > 1) {
      painel.hidden = false;
      nomeEl.textContent = t('preview.somenteUm');
      corpoEl.replaceChildren(placeholder(`${items.length} itens selecionados`));
      metaEl.replaceChildren();
    } else {
      fechar();
    }
    return;
  }

  const item = items[0];
  const caminho = item.Caminho ?? item.caminho;
  const nome = item.Nome ?? item.nome;
  const ext = fmt.extensao(nome);
  const cat = (item.Pasta ?? item.pasta) ? 'pasta' : fmt.categoria(ext);

  painel.hidden = false;
  nomeEl.textContent = nome;
  corpoEl.replaceChildren();
  metaEl.replaceChildren();

  if (cat === 'pasta') {
    corpoEl.appendChild(placeholder(t('preview.selecione')));
    return;
  }

  if (RENDERIZAVEIS.includes(ext) || REPRODUZIVEIS.includes(ext)) {
    const base = await garantirBase(caminho);
    if (base) {
      const url = `${base}/${encodeURIComponent(nome)}`;
      if (REPRODUZIVEIS.includes(ext)) {
        const video = document.createElement('video');
        video.src = url;
        video.controls = true;
        video.preload = 'metadata';
        corpoEl.appendChild(video);
      } else {
        const img = document.createElement('img');
        img.src = url;
        img.alt = nome;
        img.onerror = () => {
          corpoEl.replaceChildren(placeholder(t('preview.semPreview')));
        };
        corpoEl.appendChild(img);
      }
    } else {
      corpoEl.appendChild(placeholder(t('preview.semPreview')));
    }
  } else {
    corpoEl.appendChild(placeholder(t('preview.semPreview')));
  }

  await preencherMeta(item, caminho, ext);
}

function placeholder(texto) {
  const el = document.createElement('div');
  el.className = 'placeholder';
  el.textContent = texto;
  return el;
}

async function garantirBase(caminho) {
  const pasta = fmt.pastaDe(caminho);
  if (baseConteudo && baseAtual === pasta) return baseConteudo;

  try {
    const resposta = await call('fs.map', { caminho: pasta });
    baseConteudo = resposta?.urlBase ?? null;
    baseAtual = pasta;
    return baseConteudo;
  } catch {
    return null;
  }
}

async function preencherMeta(item, caminho, ext) {
  const linhas = [];
  const tamanho = item.Tamanho ?? item.tamanho;
  const modificado = item.Modificado ?? item.modificado;

  if (!(item.Pasta ?? item.pasta)) {
    linhas.push([t('preview.tamanho'), fmt.bytes(tamanho)]);
  }

  linhas.push([t('preview.modificado'), fmt.data(modificado)]);
  linhas.push([t('preview.tipo'), ext ? ext.toUpperCase() : '—']);
  linhas.push([t('preview.caminho'), caminho]);

  if (fmt.ehVideo(caminho)) {
    try {
      const info = await call('media.probe', { caminho });
      if (info?.info) {
        linhas.splice(1, 0, [t('preview.dimensoes'), `${info.info.Largura} × ${info.info.Altura}`]);
        linhas.splice(2, 0, [t('preview.duracao'), fmt.duracao(info.info.DuracaoSegundos)]);
        if (info.info.TemAlpha) linhas.push([t('video.alpha'), '—']);
      }
    } catch {
      // sem ffprobe: mostra o que ja temos
    }
  }

  for (const [rotulo, valor] of linhas) {
    const linha = document.createElement('div');
    linha.className = 'preview-linha';
    const a = document.createElement('span');
    a.textContent = rotulo;
    const b = document.createElement('span');
    b.textContent = valor ?? '—';
    linha.append(a, b);
    metaEl.appendChild(linha);
  }
}

export function iniciarPreview() {
  document.getElementById('preview-fechar').addEventListener('click', fechar);
  on('preview.atualizar', (items) => mostrar(items ?? []));
}
