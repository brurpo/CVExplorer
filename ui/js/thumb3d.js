import { desenhar } from './thumb3dRender.js';

/**
 * Miniaturas de modelo 3D geradas no próprio app (three.js + WebGL).
 *
 * O render roda em Web Workers com OffscreenCanvas: o parse de um GLB de 200 MB
 * não congela a interface e dois modelos são renderizados ao mesmo tempo.
 * Exceção: FBX usa `document` para resolver texturas, então cai para a thread
 * da interface (o parse continua rápido; só não é paralelo).
 */
const EXTENSOES = ['obj', 'fbx', 'glb', 'gltf', 'stl', 'usdz'];

/** Formatos que o worker não consegue desenhar sozinho. */
const SOMENTE_THREAD = ['fbx'];

/** Acima disso o parse estoura memória do WebView com facilidade. */
export const TAMANHO_MAXIMO = 350 * 1024 * 1024;

const MAX_WORKERS = Math.min(3, Math.max(1, (navigator.hardwareConcurrency ?? 4) - 2));

let workers = null;
let proximoId = 1;
const pendentes = new Map();

export function suporta(ext) {
  return EXTENSOES.includes((ext ?? '').toLowerCase());
}

function criarWorkers() {
  const lista = [];
  for (let i = 0; i < MAX_WORKERS; i++) {
    const worker = new Worker('./js/thumb3dWorker.js', { type: 'module' });
    worker.onmessage = (evento) => {
      const { id, buffer, erro } = evento.data ?? {};
      const pendente = pendentes.get(id);
      if (!pendente) return;

      pendentes.delete(id);
      if (erro) {
        pendente.reject(new Error(erro));
      } else {
        pendente.resolve(buffer);
      }
    };
    worker.onerror = (evento) => console.error('worker 3d', evento.message);
    lista.push(worker);
  }

  return lista;
}

/** Escolhe o worker com menos trabalho pendente (fila curta e previsível). */
function proximoLivre() {
  workers ??= criarWorkers();

  let escolhido = workers[0];
  let menor = Infinity;

  for (const worker of workers) {
    let carga = 0;
    for (const pendente of pendentes.values()) {
      if (pendente.worker === worker) carga++;
    }
    if (carga < menor) {
      menor = carga;
      escolhido = worker;
    }
  }

  return escolhido;
}

function paraDataUrl(origem) {
  return new Promise((resolve, reject) => {
    const leitor = new FileReader();
    leitor.onload = () => resolve(leitor.result);
    leitor.onerror = () => reject(leitor.error ?? new Error('falha ao converter miniatura'));
    leitor.readAsDataURL(new Blob([origem], { type: 'image/webp' }));
  });
}

async function naThread(url, ext, tamanho) {
  const blob = await desenhar(url, ext, tamanho, (lado) => {
    const canvas = document.createElement('canvas');
    canvas.width = lado;
    canvas.height = lado;
    return canvas;
  });

  return paraDataUrl(await blob.arrayBuffer());
}

/**
 * Renderiza o modelo e devolve um data URL WebP.
 * @param {string} url URL servida pelo host (loopback) para o arquivo do modelo.
 * @param {string} ext extensão sem ponto.
 * @param {number} tamanho lado da imagem em pixels de CSS (já multiplicado pelo DPR).
 */
export async function gerar(url, ext, tamanho = 256) {
  const tipo = (ext ?? '').toLowerCase();

  if (SOMENTE_THREAD.includes(tipo)) {
    return naThread(url, tipo, tamanho);
  }

  try {
    const id = proximoId++;
    const worker = proximoLivre();

    const buffer = await new Promise((resolve, reject) => {
      pendentes.set(id, { resolve, reject, worker });
      worker.postMessage({ id, url, ext: tipo, tamanho });
    });

    return await paraDataUrl(buffer);
  } catch (erro) {
    // Ambiente sem OffscreenCanvas/WebGL no worker: repete na thread principal.
    if (/document is not defined|OffscreenCanvas|WebGL/i.test(erro?.message ?? '')) {
      return naThread(url, tipo, tamanho);
    }

    throw erro;
  }
}

export function encerrar() {
  if (!workers) return;
  workers.forEach((w) => w.terminate());
  workers = null;
  pendentes.clear();
}
