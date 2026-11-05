import { call, on } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as explorer from './explorer.js';

/**
 * Abas do explorador. Cada aba guarda o lugar e o histórico; a interface do explorador
 * é única na janela, então trocar de aba salva o estado atual e restaura o da nova.
 *
 * Arraste:
 *  - arrastar abas para fora da janela -> vira outra janela (o host decide pelo cursor);
 *  - arrastar para dentro de outra janela -> as abas entram na barra dela;
 *  - se a janela ficar sem abas, ela fecha (é o "arrastar a janela inteira").
 */
let abas = [];
let ativaId = null;
const selecionadas = new Set();
let contador = 0;

const listaEl = () => document.getElementById('abas-lista');

function novoRegistro(caminho) {
  return {
    id: `aba-${++contador}`,
    caminho,
    historico: caminho ? [caminho] : [],
    indiceHistorico: caminho ? 0 : -1,
    selecao: [],
  };
}

function ativa() {
  return abas.find((a) => a.id === ativaId) ?? null;
}

function rotulo(caminho) {
  return fmt.nomearArquivo(caminho) || caminho || t('abas.nova');
}

export function iniciar({ caminho, extras = [] }) {
  const caminhos = [caminho, ...extras].filter(Boolean);
  abas = (caminhos.length ? caminhos : [null]).map(novoRegistro);
  ativaId = abas[0].id;
  selecionadas.clear();
  selecionadas.add(ativaId);

  document.getElementById('aba-nova')?.addEventListener('click', (evento) => {
    evento.stopPropagation();
    novaAba();
  });

  // A pasta mudou (navegação, busca, chip de projeto): atualiza o rótulo da aba.
  document.addEventListener('explorer:navegou', (evento) => {
    const registro = ativa();
    if (!registro) return;
    registro.caminho = evento.detail?.caminho ?? registro.caminho;
    render();
  });

  // Outra janela soltou abas aqui.
  on('abas.adotar', (dados) => adotar(dados?.abas ?? []));

  // Realce enquanto arrasta abas por cima desta janela.
  on('abas.arraste', (dados) => {
    document.body.classList.toggle('abas-alvo', Boolean(dados?.ativo && dados?.alvo));
  });

  render();
}

function render() {
  const lista = listaEl();
  if (!lista) return;

  lista.replaceChildren();
  for (const aba of abas) {
    const el = document.createElement('div');
    el.className = 'aba';
    el.dataset.aba = aba.id;
    el.title = aba.caminho ?? '';
    el.draggable = false;

    if (aba.id === ativaId) el.classList.add('ativa');
    if (selecionadas.has(aba.id)) el.classList.add('selecionada');

    const nome = document.createElement('span');
    nome.className = 'aba-nome';
    nome.textContent = rotulo(aba.caminho);
    el.appendChild(nome);

    const fechar = document.createElement('button');
    fechar.className = 'aba-fechar';
    fechar.title = t('abas.fechar');
    fechar.textContent = '×';
    fechar.addEventListener('pointerdown', (evento) => evento.stopPropagation());
    fechar.addEventListener('click', (evento) => {
      evento.stopPropagation();
      fecharAba(aba.id);
    });
    el.appendChild(fechar);

    el.addEventListener('pointerdown', (evento) => iniciarArraste(evento, aba));
    el.addEventListener('auxclick', (evento) => {
      if (evento.button === 1) {
        evento.preventDefault();
        fecharAba(aba.id);
      }
    });

    lista.appendChild(el);
  }
}

function iniciarArraste(evento, aba) {
  if (evento.button !== 0) return;

  // Ctrl alterna, Shift seleciona o intervalo (como nos itens do explorador).
  if (evento.ctrlKey || evento.metaKey) {
    if (selecionadas.has(aba.id)) selecionadas.delete(aba.id);
    else selecionadas.add(aba.id);
    render();
    return;
  }

  if (evento.shiftKey) {
    const indiceAtual = abas.findIndex((a) => a.id === ativaId);
    const indiceAlvo = abas.findIndex((a) => a.id === aba.id);
    if (indiceAtual >= 0 && indiceAlvo >= 0) {
      const [de, ate] = indiceAtual < indiceAlvo ? [indiceAtual, indiceAlvo] : [indiceAlvo, indiceAtual];
      selecionadas.clear();
      for (let i = de; i <= ate; i++) selecionadas.add(abas[i].id);
      render();
    }
    return;
  }

  if (!selecionadas.has(aba.id)) {
    selecionadas.clear();
    selecionadas.add(aba.id);
    render();
  }

  const inicio = { x: evento.clientX, y: evento.clientY };
  let arrastando = false;
  let movendo = false;

  const mover = (e) => {
    if (!arrastando) {
      if (Math.abs(e.clientX - inicio.x) < 6 && Math.abs(e.clientY - inicio.y) < 6) return;
      arrastando = true;
      document.body.classList.add('arrastando-abas');
      const caminhos = abas.filter((a) => selecionadas.has(a.id)).map((a) => a.caminho).filter(Boolean);
      call('abas.arrastarIniciar', { abas: caminhos }).catch(() => {});
    }

    movendo = true;
  };

  const soltar = async () => {
    encerrar();
    if (!arrastando) {
      // Clique simples: troca de aba (e mantém só ela selecionada).
      await ativar(aba.id);
      return;
    }

    if (!movendo) {
      call('abas.cancelar').catch(() => {});
      return;
    }

    let resposta;
    try {
      resposta = await call('abas.soltar');
    } catch {
      return;
    }

    if (resposta?.acao === 'mover' || resposta?.acao === 'destacar') {
      removerSelecionadas();
    }
  };

  const cancelar = () => {
    encerrar();
    if (arrastando) call('abas.cancelar').catch(() => {});
  };

  function encerrar() {
    window.removeEventListener('pointermove', mover);
    window.removeEventListener('pointerup', soltar);
    window.removeEventListener('pointercancel', cancelar);
    document.body.classList.remove('arrastando-abas');
  }

  window.addEventListener('pointermove', mover);
  window.addEventListener('pointerup', soltar);
  window.addEventListener('pointercancel', cancelar);
}

function removerSelecionadas() {
  const restantes = abas.filter((a) => !selecionadas.has(a.id));
  abas = restantes;
  selecionadas.clear();

  if (abas.length === 0) {
    // Todas as abas foram para outra janela: esta janela não tem mais o que mostrar.
    call('app.janelaFechar').catch(() => {});
    return;
  }

  if (!abas.some((a) => a.id === ativaId)) {
    ativaId = abas[0].id;
    selecionadas.add(ativaId);
    render();
    explorer.aplicarAba(ativa()).catch(() => {});
    return;
  }

  selecionadas.add(ativaId);
  render();
}

export async function ativar(id, { forcar = false } = {}) {
  if (!forcar && id === ativaId) {
    selecionadas.clear();
    selecionadas.add(id);
    render();
    return;
  }

  const atual = ativa();
  if (atual) Object.assign(atual, explorer.estadoAba());

  ativaId = id;
  selecionadas.clear();
  selecionadas.add(id);
  render();

  await explorer.aplicarAba(ativa());
}

export function novaAba(caminho) {
  const registro = novoRegistro(caminho ?? ativa()?.caminho ?? null);
  abas.push(registro);
  ativar(registro.id).catch(() => {});
}

export function fecharAba(id) {
  const indice = abas.findIndex((a) => a.id === id);
  if (indice < 0) return;

  abas.splice(indice, 1);
  selecionadas.delete(id);

  if (abas.length === 0) {
    call('app.janelaFechar').catch(() => {});
    return;
  }

  if (ativaId === id) {
    const vizinha = abas[Math.min(indice, abas.length - 1)];
    ativaId = vizinha.id;
    selecionadas.add(ativaId);
    render();
    explorer.aplicarAba(vizinha).catch(() => {});
    return;
  }

  render();
}

/** Abas recebidas de outra janela (arraste entre janelas). */
export function adotar(caminhos) {
  const novos = (caminhos ?? []).filter(Boolean);
  if (novos.length === 0) return;

  for (const caminho of novos) {
    abas.push(novoRegistro(caminho));
  }

  render();
}

export function proximaAba(direcao = 1) {
  if (abas.length < 2) return;
  const indice = abas.findIndex((a) => a.id === ativaId);
  const proximo = (indice + direcao + abas.length) % abas.length;
  ativar(abas[proximo].id).catch(() => {});
}

export function fecharAtiva() {
  if (ativaId) fecharAba(ativaId);
}

export function total() {
  return abas.length;
}
