import { call } from './bridge.js';
import { deBase64 } from './bytes.js';

/**
 * Miniatura de PDF/.ai (Illustrator salvo como "PDF compatible") desenhada com pdf.js
 * dentro do WebView2 — sem Illustrator, sem Ghostscript e sem abrir programa externo.
 *
 * Os bytes vêm pelo bridge (`pdf.dados`), não pela URL de loopback: aquela URL é de outra
 * origem e o `fetch` do pdf.js é barrado — era por isso que PDF ficava só com o ícone.
 */
let pdfjs = null;

async function carregarPdfjs() {
  if (pdfjs) {
    return pdfjs;
  }

  const modulo = await import('../vendor/pdfjs/pdf.min.mjs');
  modulo.GlobalWorkerOptions.workerSrc = './vendor/pdfjs/pdf.worker.min.mjs';
  pdfjs = modulo;
  return pdfjs;
}

/**
 * Renderiza a primeira pagina e devolve um data URL WebP.
 * @param {string} caminho caminho do PDF no disco.
 * @param {number} tamanho lado da imagem em pixels de CSS.
 */
export async function gerar(caminho, tamanho = 256) {
  const lib = await carregarPdfjs();
  const lado = Math.max(96, Math.min(512, Math.round(tamanho)));

  const resposta = await call('pdf.dados', { caminho });
  if (!resposta?.ok) {
    throw new Error(resposta?.erro ?? 'PDF indisponível');
  }

  const documento = await lib.getDocument({
    data: deBase64(resposta.dados),
    // Sem cmaps/extras: layout de logo/briefing nao precisa de fonte CJK embutida.
    useSystemFonts: true,
    disableAutoFetch: true,
    disableStream: true,
  }).promise;

  try {
    const pagina = await documento.getPage(1);
    const base = pagina.getViewport({ scale: 1 });
    const escala = Math.min(lado / base.width, lado / base.height);

    // Pixel ratio 1: o tamanho pedido ja vem multiplicado pelo devicePixelRatio.
    const viewport = pagina.getViewport({ scale: escala });

    const canvas = document.createElement('canvas');
    canvas.width = Math.max(1, Math.round(viewport.width));
    canvas.height = Math.max(1, Math.round(viewport.height));

    const contexto = canvas.getContext('2d', { alpha: false });
    contexto.fillStyle = '#ffffff';
    contexto.fillRect(0, 0, canvas.width, canvas.height);

    await pagina.render({ canvasContext: contexto, viewport }).promise;
    return canvas.toDataURL('image/webp', 0.9);
  } finally {
    documento.destroy();
  }
}
