import { call, on } from './bridge.js';
import { carregarIdioma, t } from './i18n.js';
import * as explorer from './explorer.js';
import * as lateral from './projects.js';
import * as dialogs from './dialogs.js';
import * as preview from './preview.js';
import * as ui from './ui.js';
import { state } from './state.js';

async function iniciar() {
  try {
    await carregarIdioma('pt-BR');
  } catch (erro) {
    console.error('Falha ao carregar textos', erro);
  }

  const info = await call('app.info');
  const config = await call('settings.get');
  state.info = info;
  state.config = config;

  const visaoSalva = localStorage.getItem('cvexplorer:visao');
  if (visaoSalva === 'lista' || visaoSalva === 'grade') state.visao = visaoSalva;
  state.grade = config.TamanhoGrade ?? 168;

  document.getElementById('btn-visualizacao').classList.toggle('ativo', state.visao === 'lista');
  document.getElementById('status-versao').textContent = `v${info.versao}`;

  explorer.iniciarExplorer();
  preview.iniciarPreview();
  lateral.iniciarLateral({ aoNavegar: (caminho) => explorer.navegar(caminho) });

  document.getElementById('btn-novo').addEventListener('click', () => {
    dialogs.novoProjeto(state.caminho, (criado) => {
      lateral.carregarTudo();
      if (criado) explorer.navegar(criado);
    });
  });

  document.getElementById('btn-config').addEventListener('click', async () => {
    await dialogs.abrirConfiguracoes(state.config, state.info, async (novo) => {
      state.config = novo;
      state.grade = novo.TamanhoGrade ?? state.grade;
      await lateral.carregarTudo();
      explorer.recarregar();
    });
  });

  document.addEventListener('recarregar', () => explorer.recarregar());
  document.addEventListener('navegar', (evento) => explorer.navegar(evento.detail));

  // Ctrl+N cria projeto; Ctrl+, abre configurações (atalhos de app).
  document.addEventListener('keydown', (evento) => {
    if (!evento.ctrlKey) return;
    if (evento.key.toLowerCase() === 'n') {
      evento.preventDefault();
      document.getElementById('btn-novo').click();
    }
    if (evento.key === ',') {
      evento.preventDefault();
      document.getElementById('btn-config').click();
    }
  });

  on('update.available', (dados) => mostrarAtualizacao(dados));

  await lateral.carregarTudo();

  const inicial = await caminhoInicial(config);
  if (inicial) {
    await explorer.navegar(inicial);
  }

  // Foco no conteudo: atalhos (Ctrl+C/V, setas, F2) funcionam sem clicar antes.
  document.getElementById('area-itens')?.focus({ preventScroll: true });
}

async function caminhoInicial(config) {
  if (config.UltimaPasta) {
    try {
      const stat = await call('fs.stat', { caminho: config.UltimaPasta });
      if (stat?.pasta) return config.UltimaPasta;
    } catch {
      // pasta sumiu (rede offline, por exemplo): cai para a raiz
    }
  }

  const raizes = config.Raizes ?? [];
  if (raizes.length > 0) return raizes[0];

  const roots = await call('fs.roots').catch(() => []);
  return roots?.[0]?.caminho ?? null;
}

function mostrarAtualizacao({ versao }) {
  const pill = document.getElementById('btn-atualizacao');
  pill.hidden = false;
  pill.textContent = t('atualizacao.pill', { versao: versao ?? '' });

  pill.addEventListener('click', async () => {
    pill.disabled = true;
    ui.toast(t('toast.atualizacaoBaixando'), 'info', 3000);
    try {
      await call('updates.check', { baixar: true });
      ui.toast(t('toast.atualizado'), 'ok', 6000);
      await call('updates.apply');
    } catch (erro) {
      ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
      pill.disabled = false;
    }
  });
}

iniciar().catch((erro) => {
  console.error(erro);
  document.body.innerHTML = `<div style="padding:24px;color:#e5484d;font-family:Consolas,monospace">${erro.message}</div>`;
});
