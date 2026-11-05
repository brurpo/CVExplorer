import { call } from './bridge.js';
import { t } from './i18n.js';

/**
 * Controles de vídeo desenhados por nós, para os fluxos do ffmpeg.
 *
 * O controle nativo do navegador não serve nesse caso: o fluxo sai por um pipe, sem
 * `Content-Length` nem `Accept-Ranges`, e o navegador o trata como transmissão ao vivo —
 * a duração estimada cresce sozinha e o cabeçote da barra fica pulando. Aqui a duração
 * vem do ffprobe e a busca reabre o fluxo no ponto pedido (all-intra, então é rápido).
 */

const ESPERA_OCULTAR = 2400;

function relogio(segundos) {
  const s = Math.max(0, Math.floor(segundos || 0));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const seg = s % 60;
  const dois = (n) => String(n).padStart(2, '0');
  return h > 0 ? `${h}:${dois(m)}:${dois(seg)}` : `${dois(m)}:${dois(seg)}`;
}

function botao(rotulo, titulo, aoClicar) {
  const el = document.createElement('button');
  el.type = 'button';
  el.className = 'player-btn';
  el.textContent = rotulo;
  el.title = titulo;
  el.addEventListener('click', (evento) => {
    evento.stopPropagation();
    aoClicar();
  });
  return el;
}

/**
 * @param {object} opcoes
 * @param {HTMLVideoElement} opcoes.video elemento de vídeo já no documento
 * @param {HTMLElement} opcoes.hospedeiro caixa onde os controles e o quadro flutuam
 * @param {string} opcoes.caminho arquivo de origem (para os quadros do arraste)
 * @param {number} opcoes.total duração real em segundos (ffprobe)
 * @param {() => number} opcoes.obterInicio instante do arquivo em que o fluxo começou
 * @param {(alvo: number, continuarTocando: boolean) => Promise<void>} opcoes.aoBuscar
 */
export function montarControles({ video, hospedeiro, caminho, total, obterInicio, aoBuscar }) {
  const caixa = document.createElement('div');
  caixa.className = 'controles-video';

  const quadro = document.createElement('img');
  quadro.className = 'previa-quadro';
  quadro.alt = '';

  const barra = document.createElement('input');
  barra.type = 'range';
  barra.className = 'player-barra';
  barra.min = '0';
  barra.max = '1000';
  barra.step = '1';
  barra.value = '0';

  const tempo = document.createElement('span');
  tempo.className = 'player-tempo';
  tempo.textContent = `${relogio(0)} / ${relogio(total)}`;

  const tocar = botao('▶', t('player.tocar'), () => alternar());
  const som = botao('🔊', t('player.som'), () => {
    video.muted = !video.muted;
    atualizarSom();
  });
  const tela = botao('⛶', t('player.telaCheia'), () => {
    const alvo = hospedeiro ?? caixa.parentElement;
    if (document.fullscreenElement) document.exitFullscreen().catch(() => {});
    else alvo?.requestFullscreen?.().catch(() => {});
  });

  caixa.append(tocar, barra, tempo, som, tela);
  hospedeiro.append(quadro, caixa);

  let arrastando = false;
  let ocultarEm = 0;
  let quadroPendente = null;
  let quadroEmCurso = false;
  let destruido = false;

  function atualizarBotao() {
    const tocando = !video.paused;
    tocar.textContent = tocando ? '❚❚' : '▶';
    tocar.title = tocando ? t('player.pausar') : t('player.tocar');
  }

  function atualizarSom() {
    const semSom = video.muted || video.volume === 0;
    som.textContent = semSom ? '🔇' : '🔊';
    som.title = semSom ? t('player.semSom') : t('player.som');
  }

  function atualizar() {
    if (destruido || arrastando) return;

    const inicio = obterInicio();
    const totalAtual = total || video.duration || 0;
    const atual = Math.min(totalAtual, inicio + (video.currentTime || 0));
    tempo.textContent = `${relogio(atual)} / ${relogio(totalAtual)}`;
    if (totalAtual > 0) barra.value = String(Math.round((atual / totalAtual) * 1000));
  }

  function mostrar() {
    caixa.classList.remove('oculto');
    agendarOcultar();
  }

  function agendarOcultar() {
    clearTimeout(ocultarEm);
    if (video.paused) return;
    ocultarEm = setTimeout(() => {
      if (!video.paused && !arrastando) caixa.classList.add('oculto');
    }, ESPERA_OCULTAR);
  }

  function estaNoBuffer(segundos) {
    const trechos = video.buffered;
    if (!trechos) return false;

    for (let i = 0; i < trechos.length; i++) {
      if (segundos >= trechos.start(i) && segundos <= trechos.end(i) - 0.15) return true;
    }

    return false;
  }

  // Um quadro por vez, sempre o mais recente: durante o arraste só o último ponto importa.
  function pedirQuadro(segundos) {
    quadroPendente = segundos;
    if (quadroEmCurso) return;

    const buscar = async () => {
      quadroEmCurso = true;
      try {
        while (!destruido && quadroPendente !== null) {
          const alvo = quadroPendente;
          quadroPendente = null;

          const resposta = await call('media.quadro', { caminho, tempo: alvo, largura: 960 }).catch(() => null);
          if (destruido || !resposta?.ok) break;

          quadro.src = `data:${resposta.mime};base64,${resposta.dados}`;
          quadro.classList.add('visivel');
        }
      } finally {
        quadroEmCurso = false;
      }
    };

    buscar().catch(() => {});
  }

  function alternar() {
    if (video.paused) video.play().catch(() => {});
    else video.pause();
  }

  barra.addEventListener('input', () => {
    const totalAtual = total || video.duration || 0;
    if (!totalAtual) return;

    arrastando = true;
    const alvo = (Number(barra.value) / 1000) * totalAtual;
    tempo.textContent = `${relogio(alvo)} / ${relogio(totalAtual)}`;

    const deslocamento = alvo - obterInicio();
    if (deslocamento >= 0 && estaNoBuffer(deslocamento)) {
      video.currentTime = deslocamento;
      quadro.classList.remove('visivel');
      return;
    }

    pedirQuadro(alvo);
  });

  barra.addEventListener('change', async () => {
    const totalAtual = total || video.duration || 0;
    if (!totalAtual) return;

    arrastando = false;
    const alvo = (Number(barra.value) / 1000) * totalAtual;
    const deslocamento = alvo - obterInicio();

    if (deslocamento >= 0 && estaNoBuffer(deslocamento)) {
      video.currentTime = deslocamento;
      quadro.classList.remove('visivel');
      atualizar();
      return;
    }

    const continuar = !video.paused;
    try {
      await aoBuscar(alvo, continuar);
    } finally {
      quadro.classList.remove('visivel');
      atualizar();
    }
  });

  const aoTocar = () => atualizarBotao();
  const aoPausar = () => {
    atualizarBotao();
    caixa.classList.remove('oculto');
  };

  const ouvintes = [
    [video, 'click', alternar],
    [video, 'timeupdate', atualizar],
    [video, 'play', aoTocar],
    [video, 'pause', aoPausar],
    [video, 'volumechange', atualizarSom],
    [hospedeiro, 'mousemove', mostrar],
    [caixa, 'mouseenter', mostrar],
  ];
  for (const [el, evento, ouvinte] of ouvintes) el.addEventListener(evento, ouvinte);

  atualizarBotao();
  atualizarSom();
  atualizar();
  agendarOcultar();

  return {
    atualizar,
    mostrar,
    destruir() {
      destruido = true;
      clearTimeout(ocultarEm);
      for (const [el, evento, ouvinte] of ouvintes) el.removeEventListener(evento, ouvinte);
      caixa.remove();
      quadro.remove();
    },
  };
}
