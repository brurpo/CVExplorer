import { desenhar } from './thumb3dRender.js';

/**
 * Render das miniaturas 3D fora da thread da interface.
 * Recebe { id, url, ext, tamanho } e devolve { id, buffer } com o WebP.
 */
self.onmessage = async (evento) => {
  const { id, url, ext, tamanho } = evento.data ?? {};

  try {
    const blob = await desenhar(url, ext, tamanho, (lado) => new OffscreenCanvas(lado, lado));
    const buffer = await blob.arrayBuffer();
    self.postMessage({ id, buffer }, [buffer]);
  } catch (erro) {
    self.postMessage({ id, erro: erro?.message ?? String(erro) });
  }
};
