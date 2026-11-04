import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as thumbs from './thumbnails.js';

const painel = document.getElementById('preview');
const nomeEl = document.getElementById('preview-nome');
const corpoEl = document.getElementById('preview-corpo');
const metaEl = document.getElementById('preview-meta');

const IMAGENS = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'avif', 'tif', 'tiff', 'psd', 'psb', 'exr', 'tga', 'dpx'];

export function fechar() {
  painel.hidden = true;
  corpoEl.replaceChildren();
  metaEl.replaceChildren();
}

export async function mostrar(items) {
  if (items.length !== 1) {
    if (items.length > 1) {
      painel.hidden = false;
      nomeEl.textContent = `${items.length} itens`;
      corpoEl.replaceChildren(placeholder(t('preview.somenteUm')));
      metaEl.replaceChildren();
    } else {
      fechar();
    }
    return;
  }

  const item = items[0];
  const caminho = item.Caminho ?? item.caminho;
  const nome = item.Nome ?? item.nome;
  const ehPasta = item.Pasta ?? item.pasta;
  const ext = fmt.extensao(nome);

  painel.hidden = false;
  nomeEl.textContent = nome;
  nomeEl.title = caminho;
  corpoEl.replaceChildren();
  metaEl.replaceChildren();

  if (ehPasta) {
    corpoEl.appendChild(placeholder(t('preview.selecione')));
    return;
  }

  const carregando = document.createElement('div');
  carregando.className = 'spinner';
  corpoEl.appendChild(carregando);

  if (IMAGENS.includes(ext)) {
    await mostrarImagem(caminho, nome);
  } else if (fmt.ehVideo(nome)) {
    await mostrarVideo(item, caminho, nome);
  } else {
    corpoEl.replaceChildren(placeholder(t('preview.semPreview')));
  }

  await preencherMeta(item, caminho, ext);
}

async function mostrarImagem(caminho, nome) {
  try {
    // O host devolve bytes já redimensionados: funciona com PSD, EXR, TIFF, SVG...
    const resposta = await call('fs.preview', { caminho, max: 1800 });
    if (!resposta?.dados) {
      corpoEl.replaceChildren(placeholder(t('preview.semPreview')));
      return;
    }

    const img = document.createElement('img');
    img.src = `data:${resposta.mime};base64,${resposta.dados}`;
    img.alt = nome;
    corpoEl.replaceChildren(img);
  } catch {
    corpoEl.replaceChildren(placeholder(t('preview.semPreview')));
  }
}

async function mostrarVideo(item, caminho, nome) {
  // O poster vem da mesma cadeia de miniaturas (rápida e normalmente já em cache).
  const emCache = thumbs.emCache(item);
  const aplicar = (url) => {
    const img = document.createElement('img');
    img.src = url;
    img.alt = nome;
    corpoEl.replaceChildren(img);
  };

  if (emCache) {
    aplicar(emCache);
  } else {
    thumbs.solicitar(item, 512, (url) => {
      if (!painel.hidden) aplicar(url);
    });
  }

  const abrir = document.createElement('button');
  abrir.className = 'btn';
  abrir.textContent = t('preview.abrirPlayer');
  abrir.style.marginTop = '10px';
  abrir.addEventListener('click', () => call('fs.open', { caminho }).catch(() => {}));

  const rodapePreview = document.createElement('div');
  rodapePreview.style.display = 'flex';
  rodapePreview.style.flexDirection = 'column';
  rodapePreview.style.alignItems = 'center';
  rodapePreview.append(abrir);

  setTimeout(() => {
    if (!painel.hidden) corpoEl.appendChild(rodapePreview);
  }, 0);
}

function placeholder(texto) {
  const el = document.createElement('div');
  el.className = 'placeholder';
  el.textContent = texto;
  return el;
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

  if (fmt.ehVideo(caminho)) {
    try {
      const info = await call('media.probe', { caminho });
      if (info?.info) {
        linhas.splice(1, 0, [t('preview.dimensoes'), `${info.info.Largura} × ${info.info.Altura}`]);
        linhas.splice(2, 0, [t('preview.duracao'), fmt.duracao(info.info.DuracaoSegundos)]);
        if (info.info.BitrateVideoMbps) {
          linhas.splice(3, 0, [t('preview.bitrate'), `${Number(info.info.BitrateVideoMbps).toFixed(0)} Mbps`]);
        }
      }
    } catch {
      // sem ffprobe: mostra o que já temos
    }
  }

  linhas.push([t('preview.caminho'), caminho]);

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
}
