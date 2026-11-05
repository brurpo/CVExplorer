import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as thumbs from './thumbnails.js';

const painel = document.getElementById('preview');
const divisor = document.getElementById('divisor-preview');
const nomeEl = document.getElementById('preview-nome');
const corpoEl = document.getElementById('preview-corpo');
const metaEl = document.getElementById('preview-meta');

const IMAGENS = ['jpg', 'jpeg', 'png', 'gif', 'webp', 'bmp', 'svg', 'avif', 'tif', 'tiff', 'psd', 'psb', 'exr', 'tga', 'dpx'];

const LARGURA_PADRAO = 320;
const LARGURA_MINIMA = 240;
const CHAVE_ABERTO = 'cvexplorer:previewAberto';

/**
 * O painel fica sempre no lugar: abrir/fechar sozinho mudava a largura da grade e,
 * ao tentar dar duplo clique num item, o alvo se movia. Quem decide é o usuário
 * (botão da barra, o ⟩ do painel ou o divisor).
 */
export function alternar(forcar) {
  const abrir = forcar === undefined ? painel.hidden : Boolean(forcar);
  painel.hidden = !abrir;
  divisor.hidden = !abrir;
  document.getElementById('btn-preview')?.classList.toggle('ativo', abrir);

  try {
    localStorage.setItem(CHAVE_ABERTO, abrir ? '1' : '0');
  } catch {
    // sem localStorage: só não persiste
  }

  document.dispatchEvent(new CustomEvent('layout'));
  return abrir;
}

export function estaAberto() {
  return !painel.hidden;
}

/** Estado vazio: nada selecionado (o painel continua visível). */
export function limpar() {
  fecharJogador();
  corpoEl.classList.remove('com-player');
  nomeEl.textContent = '';
  nomeEl.title = '';
  corpoEl.replaceChildren(placeholder(t('preview.selecione')));
  metaEl.replaceChildren();
}

export async function mostrar(items) {
  if (items.length !== 1) {
    if (items.length > 1) {
      fecharJogador();
      corpoEl.classList.remove('com-player');
      nomeEl.textContent = `${items.length} itens`;
      nomeEl.title = '';
      corpoEl.replaceChildren(placeholder(t('preview.somenteUm')));
      metaEl.replaceChildren();
    } else {
      limpar();
    }
    return;
  }

  const item = items[0];
  const caminho = item.Caminho ?? item.caminho;
  const nome = item.Nome ?? item.nome;
  const ehPasta = item.Pasta ?? item.pasta;
  const ext = fmt.extensao(nome);

  nomeEl.textContent = nome;
  nomeEl.title = caminho;
  fecharJogador();
  corpoEl.classList.remove('com-player');
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
  fecharJogador();

  const poster = thumbs.emCache(item);

  const container = document.createElement('div');
  container.className = 'player';
  corpoEl.classList.add('com-player');

  const video = document.createElement('video');
  video.preload = 'auto';
  video.playsInline = true;
  if (poster) video.poster = poster;

  const controles = document.createElement('div');
  controles.className = 'player-controles';

  const botaoTocar = document.createElement('button');
  botaoTocar.className = 'player-btn';
  botaoTocar.title = t('player.tocar');
  botaoTocar.textContent = '▶';

  const barra = document.createElement('input');
  barra.type = 'range';
  barra.className = 'player-barra';
  barra.min = '0';
  barra.max = '1000';
  barra.value = '0';
  barra.step = '1';

  const tempo = document.createElement('span');
  tempo.className = 'player-tempo';
  tempo.textContent = '00:00 / 00:00';

  const externo = document.createElement('button');
  externo.className = 'player-btn';
  externo.title = t('preview.abrirPlayer');
  externo.textContent = '↗';
  externo.addEventListener('click', () => call('fs.open', { caminho }).catch(() => {}));

  controles.append(botaoTocar, barra, tempo, externo);
  container.append(video, controles);
  corpoEl.replaceChildren(container);

  jogador = {
    caminho,
    video,
    id: null,
    inicio: 0,
    duracao: 0,
    tocando: false,
    botao: botaoTocar,
    barra,
    tempo,
  };

  botaoTocar.addEventListener('click', () => {
    if (video.paused) {
      video.play().catch(() => {});
    } else {
      video.pause();
    }
  });

  video.addEventListener('play', () => atualizarBotao(true));
  video.addEventListener('pause', () => atualizarBotao(false));
  video.addEventListener('timeupdate', () => atualizarTempo());
  video.addEventListener('error', () => {
    corpoEl.replaceChildren(placeholder(t('preview.previaFalhou', { erro: 'fluxo de vídeo' })));
    corpoEl.appendChild(botaoExterno(caminho));
  });

  // Soltar a barra reinicia o fluxo no ponto pedido (o stream é gerado sob demanda).
  barra.addEventListener('change', async () => {
    if (!jogador.duracao) return;
    const alvo = (Number(barra.value) / 1000) * jogador.duracao;
    await abrirFluxo(caminho, alvo, true);
  });

  mostrarCarregando(t('preview.iniciando'));
  await abrirFluxo(caminho, 0, false);
}

let jogador = null;

function atualizarBotao(tocando) {
  if (!jogador) return;
  jogador.tocando = tocando;
  jogador.botao.textContent = tocando ? '❚❚' : '▶';
  jogador.botao.title = tocando ? t('player.pausar') : t('player.tocar');
}

function atualizarTempo() {
  if (!jogador?.video) return;
  const total = jogador.duracao || 0;
  const atual = Math.min(total, jogador.inicio + (jogador.video.currentTime || 0));
  jogador.tempo.textContent = `${relogio(atual)} / ${relogio(total)}`;
  if (total > 0) {
    jogador.barra.value = String(Math.round((atual / total) * 1000));
  }
}

function relogio(segundos) {
  const s = Math.max(0, Math.floor(segundos || 0));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const resto = s % 60;
  const mm = String(m).padStart(2, '0');
  const ss = String(resto).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

function mostrarCarregando(texto) {
  const caixa = document.createElement('div');
  caixa.className = 'previa-preparando';
  const rotulo = document.createElement('div');
  rotulo.className = 'placeholder';
  rotulo.textContent = texto;
  const spinner = document.createElement('div');
  spinner.className = 'spinner';
  caixa.append(spinner, rotulo);
  if (jogador?.video) {
    jogador.video.replaceWith(caixa);
    caixa.dataset.substituto = 'video';
  } else {
    corpoEl.replaceChildren(caixa);
  }
}

function restaurarVideo() {
  if (!jogador?.video) return;
  const substituto = corpoEl.querySelector('.previa-preparando');
  if (substituto) {
    substituto.replaceWith(jogador.video.closest('.player') ?? jogador.video);
  }
}

/** Pede ao host um fluxo novo (do zero ou a partir de um ponto) e liga no <video>. */
async function abrirFluxo(caminho, inicio, tocar) {
  if (jogador?.id) {
    call('media.playFechar', { id: jogador.id }).catch(() => {});
    jogador.id = null;
  }

  const jaTemPlayer = Boolean(jogador?.video?.isConnected);
  if (!jaTemPlayer && !jogador?.video) mostrarCarregando(t('preview.iniciando'));

  let resposta;
  try {
    resposta = await call('media.play', { caminho, inicio });
  } catch (erro) {
    corpoEl.replaceChildren(placeholder(t('preview.previaFalhou', { erro: erro.message })));
    corpoEl.appendChild(botaoExterno(caminho));
    return;
  }

  if (!resposta?.ok) {
    corpoEl.replaceChildren(placeholder(t('preview.previaFalhou', { erro: resposta?.erro ?? '—' })));
    corpoEl.appendChild(botaoExterno(caminho));
    return;
  }

  jogador.id = resposta.id;
  jogador.inicio = inicio;
  jogador.duracao = resposta.duracao;
  restaurarVideo();

  const video = jogador.video;
  video.src = resposta.url;
  video.load();
  atualizarTempo();

  video.addEventListener('loadeddata', () => {
    if (tocar) video.play().catch(() => {});
  }, { once: true });
}

function fecharJogador() {
  if (jogador?.id) {
    call('media.playFechar', { id: jogador.id }).catch(() => {});
  }

  if (jogador?.video) {
    jogador.video.pause();
    jogador.video.removeAttribute('src');
    jogador.video.load();
  }

  jogador = null;
}

function botaoExterno(caminho) {
  const abrir = document.createElement('button');
  abrir.className = 'btn';
  abrir.textContent = t('preview.abrirPlayer');
  abrir.style.marginTop = '10px';
  abrir.addEventListener('click', () => call('fs.open', { caminho }).catch(() => {}));
  return abrir;
}

function placeholder(texto) {
  const el = document.createElement('div');
  el.className = 'placeholder';
  el.textContent = texto;
  return el;
}

function rotuloTipoPreview(item, ext) {
  if (item.Pasta ?? item.pasta) return t('coluna.pasta');
  return ext ? ext.toUpperCase() : '—';
}

async function preencherMeta(item, caminho, ext) {
  const linhas = [];
  const tamanho = item.Tamanho ?? item.tamanho;
  const modificado = item.Modificado ?? item.modificado;

  if (!(item.Pasta ?? item.pasta)) {
    linhas.push([t('preview.tamanho'), fmt.bytes(tamanho)]);
  }

  linhas.push([t('preview.modificado'), fmt.data(modificado)]);

  const criado = item.Criacao ?? item.criacao;
  if (criado) linhas.push([t('coluna.criado'), fmt.data(criado)]);

  linhas.push([t('preview.tipo'), rotuloTipoPreview(item, ext)]);

  if (fmt.ehVideo(caminho)) {
    try {
      const info = await call('media.probe', { caminho });
      if (info?.info) {
        linhas.splice(1, 0, [t('preview.dimensoes'), `${info.info.Largura} × ${info.info.Altura}`]);
        linhas.splice(2, 0, [t('preview.duracao'), fmt.duracao(info.info.DuracaoSegundos)]);

        // Codec e o que ele carrega: é o que decide se dá para editar direto.
        const codec = [String(info.info.Codec ?? '').toUpperCase(), info.info.PixFmt]
          .filter(Boolean).join(' · ');
        if (codec) {
          linhas.push([t('preview.codec'), codec]);
        }
        linhas.push([t('preview.entrelacado'), info.info.Interlaced ? t('preview.sim') : t('preview.nao')]);
        linhas.push([t('preview.alpha'), info.info.TemAlpha ? t('preview.sim') : t('preview.nao')]);

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
  document.getElementById('preview-fechar').addEventListener('click', () => alternar(false));
  document.getElementById('btn-preview').addEventListener('click', () => alternar());

  let abrir = true;
  try {
    abrir = localStorage.getItem(CHAVE_ABERTO) !== '0';
  } catch {
    // sem localStorage: abre por padrão
  }

  alternar(abrir);
  iniciarDivisor();
}

// ---------- largura da prévia ----------

function larguraMaxima() {
  return Math.max(LARGURA_MINIMA, window.innerWidth - 520);
}

function aplicarLargura(valor) {
  const largura = Math.min(larguraMaxima(), Math.max(LARGURA_MINIMA, valor));
  document.documentElement.style.setProperty('--w-preview', `${Math.round(largura)}px`);
  return largura;
}

function iniciarDivisor() {
  try {
    const salva = Number(localStorage.getItem('cvexplorer:previewLargura'));
    if (Number.isFinite(salva) && salva > 0) {
      aplicarLargura(salva);
    }
  } catch {
    // preferencia corrompida: fica no padrao
  }

  divisor.addEventListener('pointerdown', (evento) => {
    if (evento.button !== 0) return;
    evento.preventDefault();
    evento.stopPropagation();

    const inicioX = evento.clientX;
    const larguraInicial = painel.getBoundingClientRect().width;
    let atual = larguraInicial;
    let arrastou = false;

    try { divisor.setPointerCapture(evento.pointerId); } catch { /* ponteiro sintetico */ }
    document.body.classList.add('redimensionando-preview');

    const mover = (e) => {
      const delta = inicioX - e.clientX;
      if (!arrastou && Math.abs(delta) < 2) return;
      arrastou = true;
      atual = aplicarLargura(larguraInicial + delta);
    };

    const soltar = () => {
      divisor.removeEventListener('pointermove', mover);
      divisor.removeEventListener('pointerup', soltar);
      divisor.removeEventListener('pointercancel', soltar);
      document.body.classList.remove('redimensionando-preview');
      try { divisor.releasePointerCapture(evento.pointerId); } catch { /* ja liberado */ }
      if (arrastou) {
        localStorage.setItem('cvexplorer:previewLargura', String(Math.round(atual)));
        document.dispatchEvent(new CustomEvent('layout'));
      }
    };

    divisor.addEventListener('pointermove', mover);
    divisor.addEventListener('pointerup', soltar);
    divisor.addEventListener('pointercancel', soltar);
  });

  divisor.addEventListener('dblclick', () => {
    localStorage.setItem('cvexplorer:previewLargura', String(LARGURA_PADRAO));
    aplicarLargura(LARGURA_PADRAO);
  });
}
