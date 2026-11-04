const PASTA = '<path d="M3 7.5A2 2 0 0 1 5 5.5h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>';
// Página preenchida + vinco na cor do fundo: mesmo peso visual dos PNGs do acervo.
const DOCUMENTO =
  '<path d="M6 3.2h6.5L18 8.7V20.8H6z" fill="currentColor" stroke="none"/>'
  + '<path d="M12.5 3.2v5.5H18" fill="none" stroke="var(--surface-2)" stroke-width="1.5"/>';

/**
 * Ícones de tipo em PNG (256px, neutros, do acervo do BRP_CHROMA): em grade e em
 * lista aparecem nítidos em qualquer tamanho, inclusive com escala 150%.
 * Documento genérico fica no traço do próprio tema (o PNG do acervo é verde).
 */
const IMAGENS = {
  pasta: 'pasta.png',
  imagem: 'imagem.png',
  vetor: 'vetor.png',
  video: 'video.png',
  audio: 'audio.png',
  modelo3d: 'modelo3d.png',
  projeto: 'projeto.png',
};

// Desenho vetorial tem ícone próprio, mesmo estando na categoria "imagem".
const VETORES = ['ai', 'svg', 'eps'];

const SVG = {
  pasta: PASTA,
  documento: DOCUMENTO,
  outro: DOCUMENTO,
};

/**
 * @param {string} categoria categoria vinda de format.categoria()
 * @param {string} [ext] extensão, para casos como .ai/.svg
 */
export function icone(categoria, ext) {
  const chave = VETORES.includes(ext) ? 'vetor' : categoria;
  const arquivo = IMAGENS[chave];

  if (arquivo) {
    return `<img class="icone-tipo" src="./img/tipos/${arquivo}" alt="" draggable="false">`;
  }

  return `<svg viewBox="0 0 24 24">${SVG[chave] ?? DOCUMENTO}</svg>`;
}
