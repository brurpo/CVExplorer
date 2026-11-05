import { call } from './bridge.js';
import * as thumb3d from './thumb3d.js';
import * as thumbPdf from './thumbPdf.js';

const cache = new Map();
const falhas = new Set();
const fila = [];
let ativos = 0;
const MAX_SIMULTANEAS = 6;

// Pedidos em andamento por chave. A grade recria as telhas a cada rolagem, então
// o retorno precisa ir para a telha atual — não para a que pediu (que já morreu).
const pendentes = new Map();

// Render 3D usa GPU e parse pesado: poucos por vez para a interface continuar fluida.
let ativos3d = 0;
const MAX_3D = 2;
const fila3d = [];

// PDF/.ai: rasterizacao em software, com fila propria e curta.
let ativosPdf = 0;
const MAX_PDF = 3;
const filaPdf = [];

function chave(item) {
  return `${item.Caminho ?? item.caminho}|${item.Modificado ?? item.modificado}`;
}

export function emCache(item) {
  return cache.get(chave(item)) ?? null;
}

export function limparCache() {
  cache.clear();
  falhas.clear();
  pendentes.clear();
  fila.length = 0;
  fila3d.length = 0;
  filaPdf.length = 0;
}

/**
 * Pede a miniatura ao host e devolve a URL quando estiver pronta.
 * A geração é limitada a poucas em paralelo: o gargalo é o decoder, não a rede.
 * Se a mesma miniatura já está na fila, o pedido novo só assume o lugar do antigo
 * (a telha que vai receber a imagem é a que está na tela agora).
 */
export function solicitar(item, tamanho, aoPronto) {
  const k = chave(item);
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

  pendentes.set(k, { item, tamanho, aoPronto });
  fila.push(k);
  bombear();
}

function concluir(k, url) {
  const pedido = pendentes.get(k);
  pendentes.delete(k);
  cache.set(k, url);
  pedido?.aoPronto(url);
}

function registrarFalha(k) {
  pendentes.delete(k);
  falhas.add(k);
}

function bombear() {
  while (ativos < MAX_SIMULTANEAS && fila.length > 0) {
    const k = fila.shift();
    const pedido = pendentes.get(k);
    if (!pedido) continue;

    ativos++;
    call('thumb.request', {
      caminho: pedido.item.Caminho ?? pedido.item.caminho,
      tamanho: pedido.item.Tamanho ?? pedido.item.tamanho ?? 0,
      modificado: pedido.item.Modificado ?? pedido.item.modificado,
      size: pedido.tamanho,
    })
      .then((resposta) => {
        if (resposta?.pronta && resposta.url) {
          concluir(k, resposta.url);
          return;
        }

        // Modelo 3D ou PDF/.ai: o host entrega o arquivo e espera o WebP renderizado aqui.
        if (resposta?.renderizar && resposta.arquivoUrl) {
          const pedidoRender = {
            k,
            url: resposta.arquivoUrl,
            chave: resposta.chave,
            tipo: resposta.renderizar,
            ext: (pedido.item.Nome ?? pedido.item.nome ?? '').split('.').pop(),
            tamanho: pedido.tamanho,
          };

          if (resposta.renderizar === '3d') {
            fila3d.push(pedidoRender);
            bombear3d();
          } else {
            filaPdf.push(pedidoRender);
            bombearPdf();
          }
          return;
        }

        registrarFalha(k);
      })
      .catch(() => registrarFalha(k))
      .finally(() => {
        ativos--;
        bombear();
      });
  }
}

function bombear3d() {
  while (ativos3d < MAX_3D && fila3d.length > 0) {
    const pedido = fila3d.shift();
    ativos3d++;

    thumb3d
      .gerar(pedido.url, pedido.ext, pedido.tamanho)
      .then((dados) => call('thumb.salvar', { chave: pedido.chave, dados }))
      .then((resposta) => {
        if (resposta?.ok && resposta.url) concluir(pedido.k, resposta.url);
        else registrarFalha(pedido.k);
      })
      .catch(() => registrarFalha(pedido.k))
      .finally(() => {
        ativos3d--;
        bombear3d();
      });
  }
}

function bombearPdf() {
  while (ativosPdf < MAX_PDF && filaPdf.length > 0) {
    const pedido = filaPdf.shift();
    ativosPdf++;

    thumbPdf
      .gerar(pedido.url, pedido.tamanho)
      .then((dados) => call('thumb.salvar', { chave: pedido.chave, dados }))
      .then((resposta) => {
        if (resposta?.ok && resposta.url) concluir(pedido.k, resposta.url);
        else registrarFalha(pedido.k);
      })
      .catch(() => registrarFalha(pedido.k))
      .finally(() => {
        ativosPdf--;
        bombearPdf();
      });
  }
}
