import { call } from './bridge.js';

/**
 * Icones do Windows (os mesmos que o Explorer mostra): o host extrai em PNG de 256 px,
 * com transparencia, do tipo do arquivo ou de dentro dele (.exe, .lnk…). O desenho da
 * interface continua sendo o que aparece na hora — o icone real entra por cima quando
 * chega, e a miniatura, quando existir, tem a palavra final.
 */

// Icones que vem de dentro do arquivo: cada um e um pedido proprio.
const POR_ARQUIVO = ['exe', 'lnk', 'url', 'ico', 'msi', 'scr', 'cpl', 'dll', 'com', 'appref-ms'];

const cache = new Map();
const falhas = new Set();
const pendentes = new Map();
const fila = [];
let ativos = 0;
const MAX_SIMULTANEAS = 4;

function chave(item) {
  const caminho = item.Caminho ?? item.caminho ?? '';
  const ext = String(item.Ext ?? item.ext ?? (caminho.split('.').pop() || '')).toLowerCase();
  return POR_ARQUIVO.includes(ext) ? `${caminho}|${item.Modificado ?? item.modificado}` : ext;
}

export function emCache(item) {
  if (!item) return null;
  return cache.get(chave(item)) ?? null;
}

export function limparIcones() {
  cache.clear();
  falhas.clear();
  pendentes.clear();
  fila.length = 0;
}

export function solicitar(item, aoPronto) {
  const caminho = item?.Caminho ?? item?.caminho;
  if (!caminho) return;

  const k = chave(item);
  if (!k) return;

  const pronto = cache.get(k);
  if (pronto) {
    aoPronto(pronto);
    return;
  }

  if (falhas.has(k)) return;

  const emAndamento = pendentes.get(k);
  if (emAndamento) {
    emAndamento.aoPronto = aoPronto;
    return;
  }

  pendentes.set(k, { item, aoPronto });
  fila.push(k);
  bombear();
}

function bombear() {
  while (ativos < MAX_SIMULTANEAS && fila.length > 0) {
    const k = fila.shift();
    const pedido = pendentes.get(k);
    if (!pedido) continue;

    ativos++;
    call('icone.obter', {
      caminho: pedido.item.Caminho ?? pedido.item.caminho,
      tamanho: pedido.item.Tamanho ?? pedido.item.tamanho ?? 0,
      modificado: pedido.item.Modificado ?? pedido.item.modificado,
    })
      .then((resposta) => {
        pendentes.delete(k);

        if (!resposta?.url) {
          falhas.add(k);
          return;
        }

        cache.set(k, resposta.url);
        pedido.aoPronto(resposta.url);
      })
      .catch(() => {
        pendentes.delete(k);
        falhas.add(k);
      })
      .finally(() => {
        ativos--;
        bombear();
      });
  }
}
