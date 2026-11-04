export const state = {
  caminho: null,
  itens: [],
  carregando: false,
  visao: 'grade',
  grade: 168,
  selecao: new Set(),
  ancora: null,
  historico: [],
  indiceHistorico: -1,
  config: {},
  info: {},
  busca: { ativa: false, termo: '', opId: null, resultados: [], pastas: 0, carregando: false },
  clip: { caminhos: [], recortar: false },
  operacoes: new Map(),

  /** Ordenação da lista/grade. */
  ordenacao: { campo: 'nome', desc: false },

  /** Largura das colunas redimensionáveis (px). */
  colunas: { nome: 340, tipo: 96, tamanho: 92, data: 132, criado: 132 },

  /** Arraste entre janelas / pastas. */
  arraste: { ativo: false, caminhos: [], dentroJanela: false },
};

const barramento = new EventTarget();

export function emitir(nome, dados) {
  barramento.dispatchEvent(new CustomEvent(nome, { detail: dados }));
}

export function ouvir(nome, cb) {
  barramento.addEventListener(nome, (e) => cb(e.detail));
}

export function itensAtuais() {
  return state.busca.ativa ? state.busca.resultados : state.itens;
}

export function selecionados() {
  return itensAtuais().filter((i) => state.selecao.has(i.Caminho ?? i.caminho));
}

export function limparSelecao() {
  state.selecao.clear();
  state.ancora = null;
}
