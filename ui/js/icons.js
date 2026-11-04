const PASTA = '<path d="M3 7.5A2 2 0 0 1 5 5.5h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>';
const IMAGEM = '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8.5" cy="9.5" r="1.6"/><path d="M21 16l-5-5-9 9"/>';
const VIDEO = '<rect x="3" y="5" width="13" height="14" rx="2"/><path d="M16 10l5-3v10l-5-3z"/>';
const AUDIO = '<path d="M9 18V6l10-2v12"/><circle cx="6.5" cy="18" r="2.5"/><circle cx="16.5" cy="16" r="2.5"/>';
const CUBO = '<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9z"/><path d="M12 12l8-4.5M12 12v9M12 12L4 7.5"/>';
const DOC = '<path d="M6 3h7l5 5v13H6z"/><path d="M13 3v5h5"/>';
const PROJETO = '<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M3 9h18M9 9v11"/>';
const ARQUIVO = '<path d="M6 3h7l5 5v13H6z"/><path d="M13 3v5h5"/>';

const MAPA = {
  pasta: PASTA,
  imagem: IMAGEM,
  video: VIDEO,
  audio: AUDIO,
  modelo3d: CUBO,
  documento: DOC,
  projeto: PROJETO,
  outro: ARQUIVO,
};

export function icone(categoria) {
  return `<svg viewBox="0 0 24 24">${MAPA[categoria] ?? ARQUIVO}</svg>`;
}
