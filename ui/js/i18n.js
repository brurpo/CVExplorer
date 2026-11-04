// Todo texto da interface vem daqui: nada de string solta no JS.
const IDIOMA_PADRAO = 'pt-BR';
let dicionario = {};

export async function carregarIdioma(idioma = IDIOMA_PADRAO) {
  const resposta = await fetch(`./i18n/${idioma}.json`, { cache: 'no-store' });
  if (!resposta.ok) throw new Error(`idioma nao encontrado: ${idioma}`);
  dicionario = await resposta.json();
  aplicarEstaticos();
  document.documentElement.lang = idioma;
}

export function t(chave, variaveis = {}) {
  let texto = dicionario[chave];
  if (texto === undefined) {
    console.warn('chave de idioma ausente:', chave);
    return chave;
  }

  for (const [nome, valor] of Object.entries(variaveis)) {
    texto = texto.split(`{${nome}}`).join(String(valor));
  }

  return texto;
}

function aplicarEstaticos() {
  for (const el of document.querySelectorAll('[data-i18n]')) {
    el.textContent = t(el.dataset.i18n);
  }

  for (const el of document.querySelectorAll('[data-i18n-placeholder]')) {
    el.placeholder = t(el.dataset.i18nPlaceholder);
  }

  for (const el of document.querySelectorAll('[data-i18n-title]')) {
    el.title = t(el.dataset.i18nTitle);
  }
}

export function idiomaAtual() {
  return document.documentElement.lang || IDIOMA_PADRAO;
}
