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

// Unidade local (dois discos empilhados) e unidade de rede (globo), para a lateral
// distinguir de relance o que está na máquina do que está no servidor.
const DISCO_LOCAL =
  '<rect x="3" y="4.5" width="18" height="6.4" rx="1.8"/>'
  + '<rect x="3" y="13.1" width="18" height="6.4" rx="1.8"/>'
  + '<circle cx="6.6" cy="7.7" r="0.9" fill="currentColor" stroke="none"/>'
  + '<circle cx="6.6" cy="16.3" r="0.9" fill="currentColor" stroke="none"/>';

const DISCO_REDE =
  '<circle cx="12" cy="12" r="8.6"/>'
  + '<path d="M3.6 9.2h16.8M3.6 14.8h16.8"/>'
  + '<path d="M12 3.4c2.7 2.6 2.7 14.6 0 17.2M12 3.4c-2.7 2.6-2.7 14.6 0 17.2"/>';

/** Ícone da unidade: rede ou local. */
export function iconeUnidade(tipo) {
  const desenho = tipo === 'rede' ? DISCO_REDE : DISCO_LOCAL;
  return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">${desenho}</svg>`;
}

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
