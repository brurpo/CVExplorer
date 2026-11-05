/**
 * Conversões base64 usadas pelas rotas que trafegam bytes (PDF).
 * O bridge é JSON: arquivo entra e sai em base64, e nós só traduzimos.
 */

export function deBase64(texto) {
  const bruto = atob(texto ?? '');
  const bytes = new Uint8Array(bruto.length);
  for (let i = 0; i < bruto.length; i++) {
    bytes[i] = bruto.charCodeAt(i);
  }
  return bytes;
}

/** Em blocos: `String.fromCharCode.apply` com o arquivo inteiro estoura a pilha. */
export function paraBase64(bytes) {
  const bloco = 0x8000;
  let saida = '';

  for (let i = 0; i < bytes.length; i += bloco) {
    saida += String.fromCharCode.apply(null, bytes.subarray(i, i + bloco));
  }

  return btoa(saida);
}
