// Ponte com o host C#. Toda chamada vira { id, cmd, args } e volta por { id, ok, data }.
const pendentes = new Map();
const ouvintes = new Map();
let sequencia = 1;

function receber(mensagem) {
  const msg = mensagem.data;
  if (!msg || typeof msg !== 'object') return;

  if (msg.id && pendentes.has(msg.id)) {
    const { resolve, reject, timer } = pendentes.get(msg.id);
    pendentes.delete(msg.id);
    clearTimeout(timer);
    if (msg.ok) resolve(msg.data);
    else reject(new Error(msg.error || 'erro desconhecido'));
    return;
  }

  if (msg.evt) {
    const lista = ouvintes.get(msg.evt);
    if (lista) for (const cb of [...lista]) {
      try { cb(msg.data); } catch (erro) { console.error('ouvinte', msg.evt, erro); }
    }
  }
}

window.chrome?.webview?.addEventListener('message', receber);

export function call(cmd, args = {}) {
  return new Promise((resolve, reject) => {
    const id = sequencia++;
    // Operacoes longas (copiar/converter/buscar) podem levar minutos.
    const timer = setTimeout(() => {
      if (pendentes.has(id)) {
        pendentes.delete(id);
        reject(new Error(`tempo esgotado: ${cmd}`));
      }
    }, 30 * 60 * 1000);

    pendentes.set(id, { resolve, reject, timer });
    try {
      window.chrome.webview.postMessage({ id, cmd, args });
    } catch (erro) {
      pendentes.delete(id);
      clearTimeout(timer);
      reject(erro);
    }
  });
}

export function on(evt, cb) {
  if (!ouvintes.has(evt)) ouvintes.set(evt, new Set());
  ouvintes.get(evt).add(cb);
  return () => ouvintes.get(evt)?.delete(cb);
}
