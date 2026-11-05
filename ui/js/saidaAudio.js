/**
 * Escolha da saída de áudio do vídeo (mute + dispositivo).
 *
 * `setSinkId` é o que permite mandar o som para uma placa específica — útil quando o app
 * está no monitor e o áudio precisa sair na interface de áudio. A escolha fica guardada e
 * é reaplicada a cada vídeo novo, porque o `sinkId` é propriedade do elemento.
 *
 * Sem a permissão de "seleção de alto-falante" o navegador esconde os nomes (e pode negar
 * a troca); nesse caso o seletor continua listando as saídas por número.
 */
import { t } from './i18n.js';

const CHAVE = 'cvexplorer:saidaAudio';

export function suportado() {
  return typeof HTMLMediaElement !== 'undefined' && 'setSinkId' in HTMLMediaElement.prototype;
}

export function escolhida() {
  try {
    return localStorage.getItem(CHAVE) ?? '';
  } catch {
    return '';
  }
}

function guardar(deviceId) {
  try {
    if (deviceId) localStorage.setItem(CHAVE, deviceId);
    else localStorage.removeItem(CHAVE);
  } catch {
    // sem localStorage: vale só nesta sessão
  }
}

export async function listar() {
  if (!navigator.mediaDevices?.enumerateDevices) return [];

  try {
    const dispositivos = await navigator.mediaDevices.enumerateDevices();
    // `default` e `communications` são apelidos das mesmas caixas que já aparecem com nome
    // próprio; listá-los dobraria as opções e confundiria.
    return dispositivos.filter(
      (d) => d.kind === 'audiooutput' && d.deviceId && d.deviceId !== 'default' && d.deviceId !== 'communications',
    );
  } catch {
    return [];
  }
}

/** Manda o som deste elemento para a saída escolhida ('' = padrão do sistema). */
export async function aplicar(video, deviceId = escolhida()) {
  if (!video || !suportado()) return false;

  try {
    await video.setSinkId(deviceId ?? '');
    return true;
  } catch {
    // Sem permissão ou dispositivo removido: segue na saída padrão.
    return false;
  }
}

/**
 * Monta o seletor de saída. A escolha guardada já é aplicada ao `video` informado, e a
 * lista se atualiza sozinha quando um dispositivo é conectado ou removido.
 *
 * @param {object} opcoes
 * @param {HTMLMediaElement} [opcoes.video] elemento que passa a usar a saída escolhida
 * @param {(deviceId: string) => void} [opcoes.aoTrocar] chamado quando o usuário troca
 * @returns {HTMLSelectElement}
 */
export function montarSeletor({ video, aoTrocar } = {}) {
  const seletor = document.createElement('select');
  seletor.className = 'saida-audio';
  seletor.title = t('player.saidaAudio');

  const preencher = async () => {
    const atual = escolhida();
    const dispositivos = await listar();

    seletor.replaceChildren();

    const padrao = document.createElement('option');
    padrao.value = '';
    padrao.textContent = t('player.saidaPadrao');
    seletor.appendChild(padrao);

    dispositivos.forEach((dispositivo, indice) => {
      // Sem permissão o navegador entrega o rótulo vazio: numera para não virar lista cega.
      const opcao = document.createElement('option');
      opcao.value = dispositivo.deviceId;
      opcao.textContent = dispositivo.label || t('player.saidaNumero', { numero: indice + 1 });
      seletor.appendChild(opcao);
    });

    // A escolha guardada pode ter desaparecido (placa desconectada): volta ao padrão.
    seletor.value = dispositivos.some((d) => d.deviceId === atual) ? atual : '';
    seletor.disabled = !suportado() || dispositivos.length === 0;
    seletor.title = suportado() ? t('player.saidaAudio') : t('player.saidaIndisponivel');

    if (video) await aplicar(video, seletor.value);
  };

  seletor.addEventListener('change', async () => {
    guardar(seletor.value);
    if (video) await aplicar(video, seletor.value);
    aoTrocar?.(seletor.value);
  });

  navigator.mediaDevices?.addEventListener?.('devicechange', () => {
    preencher().catch(() => {});
  });

  // O `setSinkId` pode ser recusado enquanto o elemento ainda não tem mídia carregada
  // (o seletor nasce antes do primeiro quadro). Reaplica quando os metadados chegam.
  if (video) {
    const reaplicar = () => {
      if (seletor.value !== video.sinkId) aplicar(video, seletor.value);
    };
    video.addEventListener('loadedmetadata', reaplicar);
    video.addEventListener('canplay', reaplicar);
  }

  preencher().catch(() => {});

  return seletor;
}
