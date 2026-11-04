/**
 * Miniatura de PDF/.ai (Illustrator salvo como "PDF compatible") desenhada com pdf.js
 * dentro do WebView2 — sem Illustrator, sem Ghostscript e sem abrir programa externo.
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
 * @param {string} url URL do arquivo (servida pelo host em loopback).
 * @param {number} tamanho lado da imagem em pixels de CSS.
 */
export async function gerar(url, tamanho = 256) {
  const lib = await carregarPdfjs();
  const lado = Math.max(96, Math.min(512, Math.round(tamanho)));

  const documento = await lib.getDocument({
    url,
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
