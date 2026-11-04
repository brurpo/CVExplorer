import { t } from './i18n.js';

let contadorModal = 0;

export function toast(mensagem, tipo = 'info', duracao = 4600) {
  const area = document.getElementById('toasts');
  const el = document.createElement('div');
  el.className = `toast ${tipo}`;
  el.textContent = mensagem;
  area.appendChild(el);

  setTimeout(() => {
    el.style.transition = 'opacity 160ms, transform 160ms';
    el.style.opacity = '0';
    el.style.transform = 'translateY(4px)';
    setTimeout(() => el.remove(), 180);
  }, duracao);
}

/**
 * Modal genérica.
 * botoes: [{ texto, tipo: 'primario' | 'perigo' | '', acao: (fechar) => void, desabilitado }]
 */
export function modal({ titulo, descricao, corpo, botoes = [], largo = false, aoFechar }) {
  const id = `modal-${++contadorModal}`;
  const raiz = document.getElementById('modal-raiz');

  const anterior = raiz.firstElementChild;
  anterior?.remove();

  const dialogo = document.createElement('div');
  dialogo.className = `dialogo${largo ? ' largo' : ''}`;
  dialogo.setAttribute('role', 'dialog');
  dialogo.setAttribute('aria-modal', 'true');
  dialogo.dataset.modalId = id;

  const cabecalho = document.createElement('div');
  cabecalho.className = 'dialogo-header';
  const h = document.createElement('h2');
  h.className = 'dialogo-titulo';
  h.textContent = titulo;
  cabecalho.appendChild(h);
  if (descricao) {
    const p = document.createElement('p');
    p.className = 'dialogo-descricao';
    p.textContent = descricao;
    cabecalho.appendChild(p);
  }

  const conteudo = document.createElement('div');
  conteudo.className = 'dialogo-corpo';
  if (corpo) conteudo.appendChild(corpo);

  const rodape = document.createElement('div');
  rodape.className = 'dialogo-rodape';
  const espaco = document.createElement('div');
  espaco.className = 'espaco';
  rodape.appendChild(espaco);

  let fechado = false;
  const fechar = (resultado) => {
    if (fechado) return;
    fechado = true;
    raiz.hidden = true;
    raiz.replaceChildren();
    document.removeEventListener('keydown', aoTeclar, true);
    aoFechar?.(resultado);
  };

  for (const botao of botoes) {
    const el = document.createElement('button');
    el.className = `btn ${botao.tipo ?? ''}`.trim();
    el.textContent = botao.texto;
    el.disabled = Boolean(botao.desabilitado);
    el.addEventListener('click', () => {
      if (botao.acao) botao.acao(fechar);
      else fechar();
    });
    rodape.appendChild(el);
  }

  function aoTeclar(e) {
    if (e.key === 'Escape') {
      e.stopPropagation();
      e.preventDefault();
      fechar();
    } else if (e.key === 'Enter' && !e.shiftKey && document.activeElement?.tagName !== 'TEXTAREA') {
      const primario = rodape.querySelector('.btn.primario');
      if (primario && !primario.disabled) {
        e.stopPropagation();
        e.preventDefault();
        primario.click();
      }
    }
  }

  dialogo.append(cabecalho, conteudo, rodape);
  raiz.replaceChildren(dialogo);
  raiz.hidden = false;
  document.addEventListener('keydown', aoTeclar, true);

  raiz.onclick = (e) => {
    if (e.target === raiz) fechar();
  };

  const focavel = conteudo.querySelector('input, select, textarea, button');
  setTimeout(() => focavel?.focus(), 30);

  return { fechar, elemento: dialogo, corpo: conteudo };
}

export function campo(rotulo, controle, dica) {
  const div = document.createElement('div');
  div.className = 'campo';
  if (rotulo) {
    const label = document.createElement('label');
    label.textContent = rotulo;
    div.appendChild(label);
  }
  div.appendChild(controle);
  if (dica) {
    const d = document.createElement('span');
    d.className = 'dica';
    d.textContent = dica;
    div.appendChild(d);
  }
  return div;
}

export function input({ valor = '', placeholder = '', tipo = 'text' } = {}) {
  const el = document.createElement('input');
  el.type = tipo;
  el.value = valor;
  el.placeholder = placeholder;
  return el;
}

export function caixaAviso(texto, tipo = 'aviso') {
  const el = document.createElement('div');
  el.className = `aviso-caixa ${tipo}`;
  el.textContent = texto;
  return el;
}

// ---------- menu de contexto ----------

let fecharMenuAtual = null;

export function menuContexto(x, y, itens) {
  fecharMenuAtual?.();

  const el = document.getElementById('menu-contexto');
  el.replaceChildren();

  for (const item of itens) {
    if (item.separador) {
      const sep = document.createElement('div');
      sep.className = 'menu-sep';
      el.appendChild(sep);
      continue;
    }

    const botao = document.createElement('button');
    botao.className = `menu-item${item.perigo ? ' perigo' : ''}`;
    botao.disabled = Boolean(item.desabilitado);

    const texto = document.createElement('span');
    texto.textContent = item.rotulo;
    botao.appendChild(texto);

    if (item.atalho) {
      const atalho = document.createElement('span');
      atalho.className = 'atalho';
      atalho.textContent = item.atalho;
      botao.appendChild(atalho);
    }

    botao.addEventListener('click', () => {
      fechar();
      item.acao?.();
    });

    el.appendChild(botao);
  }

  el.hidden = false;
  el.style.left = '0px';
  el.style.top = '0px';

  const rect = el.getBoundingClientRect();
  const left = Math.min(x, window.innerWidth - rect.width - 8);
  const top = Math.min(y, window.innerHeight - rect.height - 8);
  el.style.left = `${Math.max(6, left)}px`;
  el.style.top = `${Math.max(6, top)}px`;

  function fechar() {
    el.hidden = true;
    el.replaceChildren();
    document.removeEventListener('mousedown', aoClicarFora, true);
    document.removeEventListener('keydown', aoTeclar, true);
    window.removeEventListener('blur', fechar);
    fecharMenuAtual = null;
  }

  function aoClicarFora(e) {
    if (!el.contains(e.target)) fechar();
  }

  function aoTeclar(e) {
    if (e.key === 'Escape') fechar();
  }

  fecharMenuAtual = fechar;
  setTimeout(() => {
    document.addEventListener('mousedown', aoClicarFora, true);
    document.addEventListener('keydown', aoTeclar, true);
    window.addEventListener('blur', fechar);
  }, 0);
}

// ---------- cards de progresso ----------

const cards = new Map();

export function progresso({ id, titulo, subtitulo = '' }) {
  const area = document.getElementById('progressos');
  let card = cards.get(id);
  if (card) return card;

  const el = document.createElement('div');
  el.className = 'progresso-card';

  const topo = document.createElement('div');
  topo.className = 'progresso-topo';

  const tituloEl = document.createElement('span');
  tituloEl.className = 'titulo';
  tituloEl.textContent = titulo;

  const pctEl = document.createElement('span');
  pctEl.className = 'pct';
  pctEl.textContent = '0%';

  topo.append(tituloEl, pctEl);

  const barra = document.createElement('div');
  barra.className = 'barra';
  const preenchimento = document.createElement('i');
  barra.appendChild(preenchimento);

  const sub = document.createElement('div');
  sub.className = 'progresso-sub';
  const subTexto = document.createElement('span');
  subTexto.textContent = subtitulo;
  const cancelar = document.createElement('button');
  cancelar.className = 'mini-btn';
  cancelar.textContent = t('progresso.cancelar');
  sub.append(subTexto, cancelar);

  el.append(topo, barra, sub);
  area.appendChild(el);

  card = {
    el,
    aoCancelar: null,
    atualizar(percentual, texto) {
      const p = Math.max(0, Math.min(100, Math.round(percentual)));
      pctEl.textContent = `${p}%`;
      preenchimento.style.width = `${p}%`;
      if (texto !== undefined) subTexto.textContent = texto;
    },
    definirTitulo(texto) { tituloEl.textContent = texto; },
    fechar(atraso = 260) {
      setTimeout(() => el.remove(), atraso);
      cards.delete(id);
    },
  };

  cancelar.addEventListener('click', () => card.aoCancelar?.());
  cards.set(id, card);
  return card;
}

export function fecharTodosProgressos() {
  for (const [id, card] of cards) {
    card.fechar(0);
    cards.delete(id);
  }
}

/**
 * Atualiza um card existente e ignora se ele nao existe.
 * Evita recriar cards "fantasma" quando chega um evento atrasado de uma operacao ja concluida.
 */
export function atualizarProgresso(id, percentual, texto) {
  const card = cards.get(id);
  if (card) card.atualizar(percentual, texto);
}
