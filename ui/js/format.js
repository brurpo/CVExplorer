const formatadorBytes = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 });
const formatadorData = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit',
});
const formatadorHora = new Intl.DateTimeFormat('pt-BR', {
  day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit',
});

export function bytes(valor) {
  if (!valor || valor < 0) return '—';
  const unidades = ['B', 'KB', 'MB', 'GB', 'TB'];
  let v = valor;
  let i = 0;
  while (v >= 1024 && i < unidades.length - 1) { v /= 1024; i++; }
  return `${formatadorBytes.format(v)} ${unidades[i]}`;
}

export function data(valor) {
  if (!valor) return '—';
  const d = valor instanceof Date ? valor : new Date(valor);
  if (Number.isNaN(d.getTime())) return '—';
  return formatadorData.format(d);
}

export function dataCurta(valor) {
  if (!valor) return '—';
  const d = valor instanceof Date ? valor : new Date(valor);
  if (Number.isNaN(d.getTime())) return '—';
  return formatadorHora.format(d);
}

export function duracao(segundos) {
  if (!segundos || segundos <= 0) return '—';
  const s = Math.round(segundos);
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const seg = s % 60;
  return h > 0
    ? `${h}:${String(m).padStart(2, '0')}:${String(seg).padStart(2, '0')}`
    : `${m}:${String(seg).padStart(2, '0')}`;
}

export function extensao(nome) {
  const ponto = nome.lastIndexOf('.');
  return ponto > 0 ? nome.slice(ponto + 1).toLowerCase() : '';
}

export function numero(valor) {
  return new Intl.NumberFormat('pt-BR').format(valor ?? 0);
}

export const CATEGORIAS = {
  imagem: ['jpg', 'jpeg', 'png', 'tif', 'tiff', 'psd', 'psb', 'ai', 'svg', 'exr', 'gif', 'bmp', 'webp', 'tga', 'dpx', 'heic', 'jxl'],
  video: ['mp4', 'mov', 'mxf', 'avi', 'mkv', 'wmv', 'webm', 'm4v', 'mpg', 'mpeg', 'mts', 'm2ts', 'r3d', 'braw'],
  audio: ['wav', 'mp3', 'aif', 'aiff', 'flac', 'm4a', 'ogg', 'aac'],
  modelo3d: ['blend', 'obj', 'fbx', 'usd', 'usda', 'usdc', 'usdz', 'glb', 'gltf', 'ma', 'mb', 'c4d', 'abc', 'stl'],
  documento: ['pdf', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'txt', 'rtf', 'csv', 'md'],
  projeto: ['aep', 'prproj', 'drp', 'nk'],
};

export function categoria(ext) {
  for (const [nome, lista] of Object.entries(CATEGORIAS)) {
    if (lista.includes(ext)) return nome;
  }
  return 'outro';
}

export function ehVideo(nomeOuExt) {
  const ext = nomeOuExt.includes('.') ? extensao(nomeOuExt) : nomeOuExt;
  return CATEGORIAS.video.includes(ext);
}

/** Extensões que podem formar sequência (as mesmas do host, em ImageSequence.cs). */
export const EXTENSOES_SEQUENCIA = ['png', 'jpg', 'jpeg', 'tif', 'tiff', 'exr', 'dpx', 'tga', 'bmp', 'hdr'];

export function podeSerSequencia(nome) {
  return EXTENSOES_SEQUENCIA.includes(extensao(nome));
}

export function nomearArquivo(caminho) {
  const partes = caminho.split(/[\\/]/);
  return partes[partes.length - 1] || caminho;
}

export function pastaDe(caminho) {
  const partes = caminho.split(/[\\/]/);
  partes.pop();
  return partes.join('\\');
}
