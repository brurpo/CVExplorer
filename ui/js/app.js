import { call, on } from './bridge.js';
import { carregarIdioma, t } from './i18n.js';
import * as explorer from './explorer.js';
import * as lateral from './projects.js';
import * as projetoAtual from './projetoAtual.js';
import * as dialogs from './dialogs.js';
import * as preview from './preview.js';
import * as relatorio from './relatorio.js';
import * as previaRapida from './previaRapida.js';
import * as abas from './abas.js';
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
  projetoAtual.iniciarProjetoAtual();
  lateral.iniciarLateral({ aoNavegar: (caminho) => explorer.navegar(caminho) });

  // Janela de relatório ("pastas maiores"): cobre o explorador, que fica só de fundo.
  if (info.relatorio) {
    document.body.dataset.modo = 'relatorio';
    relatorio.iniciar(info.relatorio);
  }

  // Janela de prévia rápida (barra de espaço): só o item, sem nada em volta.
  if (info.previa) {
    document.body.dataset.modo = 'previa';
    previaRapida.iniciar(info.previa);
    on('previa.mostrar', (dados) => previaRapida.iniciar(dados?.caminho));
  }

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

  // ---- tema claro/escuro ----
  function aplicarTema(tema, salvar) {
    const claro = tema === 'claro';
    document.documentElement.dataset.tema = claro ? 'claro' : 'escuro';

    try {
      localStorage.setItem('cvexplorer:tema', claro ? 'claro' : 'escuro');
    } catch {
      // sem localStorage: só não persiste
    }

    if (salvar) {
      state.config.Tema = claro ? 'claro' : 'escuro';
      call('settings.save', state.config).catch(() => {});
    }
  }

  aplicarTema(config.Tema ?? 'escuro', false);

  document.getElementById('btn-tema').addEventListener('click', () => {
    const atual = document.documentElement.dataset.tema;
    aplicarTema(atual === 'claro' ? 'escuro' : 'claro', true);
  });

  // ---- barra de título própria ----
  const idJanela = info?.janela ?? null;

  const janelaAcao = (acao, extra = {}) =>
    call(`app.janela${acao}`, { janela: idJanela, ...extra }).catch(() => {});

  document.getElementById('btn-janela-min').addEventListener('click', (e) => {
    e.stopPropagation();
    janelaAcao('Minimizar');
  });

  const btnMax = document.getElementById('btn-janela-max');
  const definirMaximizada = (maximizada) => {
    document.body.classList.toggle('janela-maximizada', Boolean(maximizada));
    btnMax.title = maximizada ? t('janela.restaurar') : t('janela.maximizar');
    btnMax.innerHTML = maximizada
      ? '<svg viewBox="0 0 12 12"><rect x="2" y="4" width="6" height="6" rx="1"/><path d="M4.5 4V2.5h5.5v5.5H8"/></svg>'
      : '<svg viewBox="0 0 12 12"><rect x="2.5" y="2.5" width="7" height="7" rx="1"/></svg>';
  };

  btnMax.addEventListener('click', async (e) => {
    e.stopPropagation();
    try {
      const r = await call('app.janelaMaximizar', { janela: idJanela });
      definirMaximizada(r?.maximizada);
    } catch {
      // sem host: ignora
    }
  });

  document.getElementById('btn-janela-fechar').addEventListener('click', (e) => {
    e.stopPropagation();
    janelaAcao('Fechar');
  });

  // Arrastar só começa quando o ponteiro anda de verdade. Se o loop nativo do Windows
  // entrasse no pointerdown, ele engoliria o segundo clique e o duplo clique (maximizar)
  // nunca chegaria.
  const barra = document.getElementById('titlebar');
  barra.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || e.target.closest('button')) return;

    const inicio = { x: e.screenX, y: e.screenY };
    let pedido = false;

    const mover = (evento) => {
      if (pedido) return;
      if (Math.abs(evento.screenX - inicio.x) < 4 && Math.abs(evento.screenY - inicio.y) < 4) return;
      pedido = true;
      encerrar();
      janelaAcao('Arrastar');
    };

    const encerrar = () => {
      window.removeEventListener('pointermove', mover);
      window.removeEventListener('pointerup', encerrar);
      window.removeEventListener('pointercancel', encerrar);
    };

    window.addEventListener('pointermove', mover);
    window.addEventListener('pointerup', encerrar);
    window.addEventListener('pointercancel', encerrar);
  });
  barra.addEventListener('dblclick', (e) => {
    if (e.target.closest('button')) return;
    btnMax.click();
  });

  on('janela.estado', (dados) => definirMaximizada(dados?.maximizada));

  document.addEventListener('recarregar', () => explorer.recarregar());
  document.addEventListener('navegar', (evento) => explorer.navegar(evento.detail));

  document.getElementById('btn-nova-janela').addEventListener('click', async () => {
    await call('app.novaJanela', { caminho: state.caminho }).catch((erro) => {
      ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
    });
  });

  // Ctrl+N abre outra janela (como o Explorer); Ctrl+Shift+N cria projeto.
  document.addEventListener('keydown', (evento) => {
    if (!evento.ctrlKey) return;

    if (evento.key.toLowerCase() === 'n') {
      evento.preventDefault();
      if (evento.shiftKey) {
        document.getElementById('btn-novo').click();
      } else {
        document.getElementById('btn-nova-janela').click();
      }
      return;
    }

    // Abas: Ctrl+T nova, Ctrl+W fecha, Ctrl+Tab anda entre elas.
    if (evento.key.toLowerCase() === 't') {
      evento.preventDefault();
      abas.novaAba(state.caminho);
      return;
    }

    if (evento.key.toLowerCase() === 'w') {
      evento.preventDefault();
      abas.fecharAtiva();
      return;
    }

    if (evento.key === 'Tab') {
      evento.preventDefault();
      abas.proximaAba(evento.shiftKey ? -1 : 1);
      return;
    }

    if (evento.key === ',') {
      evento.preventDefault();
      document.getElementById('btn-config').click();
    }
  });

  on('update.available', (dados) => mostrarAtualizacao(dados));

  await lateral.carregarTudo();

  const inicial = info.relatorio || info.previa ? null : await caminhoInicial(config);
  if (inicial) {
    abas.iniciar({ caminho: inicial, extras: info.abasIniciais ?? [] });
    await explorer.navegar(inicial);
  }

  // Foco no conteudo: atalhos (Ctrl+C/V, setas, F2) funcionam sem clicar antes.
  document.getElementById('area-itens')?.focus({ preventScroll: true });
}

async function caminhoInicial(config) {
  // Janela nova ja nasce na pasta pedida (ou na pasta da janela de origem).
  try {
    const inicio = await call('app.inicio');
    if (inicio?.caminho) {
      const stat = await call('fs.stat', { caminho: inicio.caminho });
      if (stat?.pasta) return inicio.caminho;
    }
  } catch {
    // sem pasta inicial definida
  }

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

// Sem devtools no dia a dia: erro de JS aparece como aviso em vez de sumir no console.
window.addEventListener('unhandledrejection', (evento) => {
  const mensagem = evento.reason?.message ?? String(evento.reason ?? '');
  if (mensagem) ui.toast(`Erro: ${mensagem}`, 'erro', 7000);
});

window.addEventListener('error', (evento) => {
  if (evento.message) ui.toast(`Erro: ${evento.message}`, 'erro', 7000);
});
