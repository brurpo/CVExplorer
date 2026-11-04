import { call } from './bridge.js';

const cache = new Map();
const falhas = new Set();
const fila = [];
let ativos = 0;
const MAX_SIMULTANEAS = 6;

function chave(item) {
  return `${item.Caminho ?? item.caminho}|${item.Modificado ?? item.modificado}`;
}

export function emCache(item) {
  return cache.get(chave(item)) ?? null;
}

export function limparCache() {
  cache.clear();
  falhas.clear();
  fila.length = 0;
}

/**
 * Pede a miniatura ao host e devolve a URL quando estiver pronta.
 * A geracao e limitada a poucas em paralelo: o gargalo e o decoder, nao a rede.
 */
export function solicitar(item, tamanho, aoPronto) {
  const k = chave(item);
  const pronto = cache.get(k);
  if (pronto) {
    aoPronto(pronto);
    return;
  }

  if (falhas.has(k)) return;

  if (fila.some((p) => p.k === k)) return;
  fila.push({ k, item, tamanho, aoPronto });
  bombear();
}

function bombear() {
  while (ativos < MAX_SIMULTANEAS && fila.length > 0) {
    const pedido = fila.shift();
    ativos++;
    call('thumb.request', {
      caminho: pedido.item.Caminho ?? pedido.item.caminho,
      tamanho: pedido.item.Tamanho ?? pedido.item.tamanho ?? 0,
      modificado: pedido.item.Modificado ?? pedido.item.modificado,
      size: pedido.tamanho,
    })
      .then((resposta) => {
        if (resposta?.pronta && resposta.url) {
          cache.set(pedido.k, resposta.url);
          pedido.aoPronto(resposta.url);
        } else {
          falhas.add(pedido.k);
        }
      })
      .catch(() => falhas.add(pedido.k))
      .finally(() => {
        ativos--;
        bombear();
      });
  }
}
