import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as ui from './ui.js';

const PERFIS_EDICAO = ['prores422lt', 'prores422', 'prores422hq', 'prores4444', 'dnxhr_lb', 'dnxhr_sq', 'dnxhr_hqx', 'proxy'];

const PERFIS = ['auto', ...PERFIS_EDICAO];

/**
 * Presets de entrega do menu de contexto ("Converter para"). A chave e a mesma
 * que o host usa em VideoPlanBuilder, que define codec, container e audio.
 */
export const PRESETS_CONVERSAO = [
  {
    grupo: 'MOV',
    itens: [
      { chave: 'prores422_mov', rotulo: 'ProRes 422' },
      { chave: 'prores4444_mov', rotulo: 'ProRes 4444 (alpha)' },
      { chave: 'dnxhr_sq_mov', rotulo: 'DNxHR SQ' },
      { chave: 'dnxhr_hq_mov', rotulo: 'DNxHR HQ' },
      { chave: 'dxv3', rotulo: 'DXV3' },
    ],
  },
  {
    grupo: 'MXF',
    itens: [
      { chave: 'dnxhr_sq_mxf', rotulo: 'DNxHR SQ' },
      { chave: 'dnxhr_hq_mxf', rotulo: 'DNxHR HQ' },
      { chave: 'xdcam', rotulo: 'XDCAM HD422 50 Mbps' },
    ],
  },
  {
    grupo: 'MP4',
    itens: [{ chave: 'h264', rotulo: 'H.264' }],
  },
];

const FPS_COMUNS = [23.976, 24, 25, 29.97, 30, 50, 59.94, 60];

function seletorPerfil(estimativas, valorInicial) {
  const nomes = new Map((estimativas ?? []).map((e) => [e.Perfil, e]));
  const seletor = document.createElement('select');

  const grupo = (rotulo) => {
    const el = document.createElement('optgroup');
    el.label = rotulo;
    seletor.appendChild(el);
    return el;
  };

  const auto = document.createElement('option');
  auto.value = 'auto';
  auto.textContent = t('video.perfis.auto');
  grupo(t('video.grupoAuto')).appendChild(auto);

  const edicao = grupo(t('video.grupoEdicao'));
  for (const valor of PERFIS_EDICAO) {
    const opcao = document.createElement('option');
    const tamanho = nomes.get(valor)?.Texto;
    opcao.value = valor;
    opcao.textContent = tamanho ? `${t(`video.perfis.${valor}`)} — ${tamanho}` : t(`video.perfis.${valor}`);
    edicao.appendChild(opcao);
  }

  // O que sobra das estimativas sao os presets de entrega (MOV/MXF/MP4), que o host nomeia.
  const entrega = grupo(t('video.grupoEntrega'));
  for (const [chave, dados] of nomes) {
    if (PERFIS_EDICAO.includes(chave)) continue;
    const opcao = document.createElement('option');
    opcao.value = chave;
    opcao.textContent = dados.Texto ? `${dados.Nome} — ${dados.Texto}` : dados.Nome;
    entrega.appendChild(opcao);
  }

  seletor.value = valorInicial ?? 'auto';
  return seletor;
}

export function pedirTexto({ titulo, rotulo, valor = '', dica }) {
  return new Promise((resolve) => {
    const entrada = ui.input({ valor });
    const corpo = document.createElement('div');
    corpo.appendChild(ui.campo(rotulo, entrada, dica));

    let resolvido = false;
    const concluir = (resultado, fechar) => {
      if (!resolvido) {
        resolvido = true;
        resolve(resultado);
      }
      fechar();
    };

    const m = ui.modal({
      titulo,
      corpo,
      botoes: [
        { texto: t('dialogo.cancelar'), acao: (f) => concluir(null, f) },
        { texto: t('dialogo.confirmar'), tipo: 'primario', acao: (f) => concluir(entrada.value.trim() || null, f) },
      ],
      aoFechar: () => { if (!resolvido) resolve(null); },
    });

    setTimeout(() => {
      entrada.focus();
      entrada.select();
    }, 40);

    return m;
  });
}

export function confirmar({ titulo, mensagem, textoConfirmar = t('dialogo.confirmar'), perigo = false }) {
  return new Promise((resolve) => {
    const corpo = document.createElement('div');
    const p = document.createElement('p');
    p.style.margin = '0';
    p.style.color = 'var(--text-2)';
    p.style.fontSize = '12.5px';
    p.textContent = mensagem;
    corpo.appendChild(p);

    let resolvido = false;
    const concluir = (valor, fechar) => {
      if (!resolvido) { resolvido = true; resolve(valor); }
      fechar();
    };

    ui.modal({
      titulo,
      corpo,
      botoes: [
        { texto: t('dialogo.cancelar'), acao: (f) => concluir(false, f) },
        { texto: textoConfirmar, tipo: perigo ? 'perigo' : 'primario', acao: (f) => concluir(true, f) },
      ],
      aoFechar: () => { if (!resolvido) resolve(false); },
    });
  });
}

const ROTULO_DECISAO = {
  substituir: 'conflito.substituir',
  manterAmbos: 'conflito.manterAmbos',
  pular: 'conflito.pular',
};

export function conflito(info) {
  return new Promise((resolve) => {
    const corpo = document.createElement('div');

    const pressa = document.createElement('div');
    pressa.style.display = 'grid';
    pressa.style.gridTemplateColumns = 'auto 1fr';
    pressa.style.gap = '4px 12px';
    pressa.style.fontSize = '11.5px';
    pressa.innerHTML = `
      <span style="color:var(--muted)">${t('conflito.origem')}</span><span style="color:var(--text-2);word-break:break-all">${escapar(info.Origem)}</span>
      <span style="color:var(--muted)">${t('conflito.destino')}</span><span style="color:var(--text-2);word-break:break-all">${escapar(info.Destino)}</span>
      <span style="color:var(--muted)">${t('preview.tamanho')}</span><span style="color:var(--text-2)">${fmt.bytes(info.TamanhoOrigem)} → ${fmt.bytes(info.TamanhoDestino)}</span>
    `;
    corpo.appendChild(pressa);

    const aplicar = document.createElement('label');
    aplicar.className = 'check-linha';
    const caixa = document.createElement('input');
    caixa.type = 'checkbox';
    aplicar.append(caixa, document.createTextNode(t('conflito.aplicarTodos')));
    corpo.appendChild(aplicar);

    let resolvido = false;
    const concluir = (escolha, fechar) => {
      if (!resolvido) {
        resolvido = true;
        resolve({ escolha, aplicarTodos: caixa.checked });
      }
      fechar();
    };

    ui.modal({
      titulo: t('conflito.titulo'),
      descricao: t('conflito.descricao'),
      corpo,
      botoes: [
        { texto: t('conflito.cancelar'), tipo: 'perigo', acao: (f) => concluir('cancelar', f) },
        { texto: t('conflito.pular'), acao: (f) => concluir('pular', f) },
        { texto: t('conflito.manterAmbos'), acao: (f) => concluir('manterAmbos', f) },
        { texto: t('conflito.substituir'), tipo: 'primario', acao: (f) => concluir('substituir', f) },
      ],
      aoFechar: () => { if (!resolvido) resolve({ escolha: 'cancelar', aplicarTodos: false }); },
    });
  });
}

// ---------- árvore de modelo ----------

function criarArvore(nos, selecionadas) {
  const raiz = document.createElement('div');
  raiz.className = 'arvore';

  const caixas = [];

  const montar = (lista, prefixo, nivel) => {
    for (const no of lista) {
      const caminho = prefixo ? `${prefixo}/${no.Nome}` : no.Nome;

      const linha = document.createElement('label');
      linha.className = 'arvore-no';
      linha.style.paddingLeft = `${nivel * 16 + 6}px`;

      const caixa = document.createElement('input');
      caixa.type = 'checkbox';
      caixa.dataset.caminho = caminho;
      caixa.checked = selecionadas.has(caminho);

      const texto = document.createElement('span');
      const partes = no.Nome.split('{QUADRO}');
      if (partes.length > 1) {
        texto.append(document.createTextNode(partes[0]));
        const token = document.createElement('span');
        token.className = 'token';
        token.textContent = '{QUADRO}';
        texto.appendChild(token);
        texto.append(document.createTextNode(partes.slice(1).join('')));
      } else {
        texto.textContent = no.Nome;
      }

      linha.append(caixa, texto);

      if (no.Filhos?.length) {
        const contagem = document.createElement('span');
        contagem.className = 'filhos';
        contagem.textContent = t('modelo.pastas', { n: no.Filhos.length });
        linha.appendChild(contagem);
      }

      raiz.appendChild(linha);
      caixas.push({ caixa, caminho });

      if (no.Filhos?.length) montar(no.Filhos, caminho, nivel + 1);
    }
  };

  montar(nos, '', 0);

  // Pai marcado marca os filhos; desmarcar o pai desmarca os descendentes.
  raiz.addEventListener('change', (evento) => {
    const caixa = evento.target;
    if (caixa.type !== 'checkbox') return;
    const caminho = caixa.dataset.caminho;

    for (const item of caixas) {
      if (item.caminho === caminho) continue;
      if (item.caminho.startsWith(`${caminho}/`)) item.caixa.checked = caixa.checked;
    }
  });

  return {
    elemento: raiz,
    selecionadas: () => caixas.filter((c) => c.caixa.checked).map((c) => c.caminho),
    marcarTodas(valor) {
      for (const item of caixas) item.caixa.checked = valor;
    },
    caixas,
  };
}

async function carregarModelo() {
  const modelo = await call('template.get');
  const selecionadas = new Set(modelo.caminhos.filter((c) => (c.match(/\//g) || []).length === 0));
  return { modelo, selecionadas };
}

// ---------- novo projeto ----------

export async function novoProjeto(pastaBase, aoCriar) {
  const { modelo, selecionadas } = await carregarModelo();

  let pastaEscolhida = pastaBase || '';

  const corpo = document.createElement('div');

  const nome = ui.input({ placeholder: 'Ex.: PROGRAMA X' });
  const tipo = document.createElement('select');
  for (const valor of ['PACOTE_GRAFICO', 'CHAMADAS', 'ABERTURA', 'EDITORIA', 'LIVRE']) {
    const opcao = document.createElement('option');
    opcao.value = valor;
    opcao.textContent = valor;
    tipo.appendChild(opcao);
  }

  const cliente = ui.input({ placeholder: '' });

  const pasta = ui.input({ valor: pastaEscolhida });
  const escolher = document.createElement('button');
  escolher.className = 'btn';
  escolher.textContent = t('dialogo.escolher');
  escolher.addEventListener('click', async () => {
    const resultado = await call('fs.pickFolder', { titulo: t('projeto.pasta'), inicial: pasta.value });
    if (resultado?.caminho) pasta.value = resultado.caminho;
  });

  const linhaPasta = document.createElement('div');
  linhaPasta.style.display = 'flex';
  linhaPasta.style.gap = '8px';
  linhaPasta.appendChild(pasta);
  pasta.style.flex = '1';
  linhaPasta.appendChild(escolher);

  const quadros = ui.input({ placeholder: 'CHAMADA 01, CHAMADA 02' });

  const arvore = criarArvore(modelo.arvore, selecionadas);

  const linhaBotoes = document.createElement('div');
  linhaBotoes.style.display = 'flex';
  linhaBotoes.style.gap = '8px';
  const marcarTudo = document.createElement('button');
  marcarTudo.className = 'btn';
  marcarTudo.textContent = t('modelo.tudo');
  marcarTudo.addEventListener('click', () => arvore.marcarTodas(true));
  const desmarcar = document.createElement('button');
  desmarcar.className = 'btn';
  desmarcar.textContent = t('modelo.nada');
  desmarcar.addEventListener('click', () => arvore.marcarTodas(false));
  linhaBotoes.append(marcarTudo, desmarcar);

  const blocoArvore = document.createElement('div');
  blocoArvore.className = 'campo';
  const rotuloArvore = document.createElement('label');
  rotuloArvore.textContent = t('projeto.modelo');
  blocoArvore.append(rotuloArvore, linhaBotoes, arvore.elemento);

  corpo.append(
    ui.campo(t('projeto.nome'), nome),
    ui.campo(t('projeto.pasta'), linhaPasta),
    ui.campo(t('projeto.tipo'), tipo),
    ui.campo(t('projeto.cliente'), cliente),
    ui.campo(t('projeto.quadros'), quadros, t('projeto.quadrosDica')),
    blocoArvore,
  );

  const criar = async (fechar) => {
    if (!nome.value.trim()) {
      ui.toast(t('projeto.nomeObrigatorio'), 'aviso');
      nome.focus();
      return;
    }
    if (!pasta.value.trim()) {
      ui.toast(t('projeto.pastaObrigatoria'), 'aviso');
      pasta.focus();
      return;
    }

    const nomesQuadros = quadros.value.split(',').map((q) => q.trim()).filter(Boolean);
    const escolhidas = arvore.selecionadas();
    const precisaQuadro = escolhidas.some((c) => c.includes('{QUADRO}'));
    if (precisaQuadro && nomesQuadros.length === 0) {
      ui.toast(t('projeto.semQuadros'), 'aviso');
      quadros.focus();
      return;
    }

    try {
      const criada = await call('fs.mkdir', { pai: pasta.value, nome: nome.value });
      const caminho = criada.caminho;

      await call('project.register', {
        caminho,
        nome: nome.value.trim(),
        tipo: tipo.value,
        cliente: cliente.value.trim(),
      });

      const resultado = await call('project.materialize', {
        caminho,
        selecionadas: escolhidas,
        quadros: nomesQuadros,
      });

      ui.toast(t('projeto.criado', { nome: nome.value.trim() }), 'ok');
      if (resultado?.criadas?.length) {
        ui.toast(t('projeto.pastasCriadas', { n: resultado.criadas.length, existentes: resultado.jaExistiam.length }), 'info', 5000);
      }

      document.dispatchEvent(new CustomEvent('projetos:atualizar'));
      fechar();
      aoCriar?.(caminho);
    } catch (erro) {
      ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
    }
  };

  ui.modal({
    titulo: t('projeto.novoTitulo'),
    descricao: t('projeto.novoDescricao'),
    corpo,
    largo: true,
    botoes: [
      { texto: t('dialogo.cancelar') },
      { texto: t('dialogo.criar'), tipo: 'primario', acao: criar },
    ],
  });
}

export async function pastasModelo(caminho, aoConcluir) {
  const { modelo, selecionadas } = await carregarModelo();
  const arvore = criarArvore(modelo.arvore, selecionadas);
  const quadros = ui.input({ placeholder: 'CHAMADA 01' });

  const corpo = document.createElement('div');
  corpo.append(
    ui.campo(t('projeto.quadros'), quadros, t('projeto.quadrosDica')),
    arvore.elemento,
  );

  ui.modal({
    titulo: t('projeto.pastasTitulo'),
    descricao: t('projeto.pastasDescricao'),
    corpo,
    largo: true,
    botoes: [
      { texto: t('dialogo.cancelar') },
      {
        texto: t('dialogo.criar'),
        tipo: 'primario',
        acao: async (fechar) => {
          const nomes = quadros.value.split(',').map((q) => q.trim()).filter(Boolean);
          const escolhidas = arvore.selecionadas();
          if (escolhidas.some((c) => c.includes('{QUADRO}')) && nomes.length === 0) {
            ui.toast(t('projeto.semQuadros'), 'aviso');
            return;
          }

          try {
            const resultado = await call('project.materialize', {
              caminho,
              selecionadas: escolhidas,
              quadros: nomes,
            });
            ui.toast(t('projeto.pastasCriadas', {
              n: resultado.criadas.length,
              existentes: resultado.jaExistiam.length,
            }), 'ok', 5000);
            fechar();
            aoConcluir?.();
          } catch (erro) {
            ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
          }
        },
      },
    ],
  });
}

export async function registrarProjeto(caminho) {
  const nomeSugerido = fmt.nomearArquivo(caminho);
  const nome = await pedirTexto({ titulo: t('projeto.registrarTitulo'), rotulo: t('projeto.nome'), valor: nomeSugerido });
  if (!nome) return;

  try {
    await call('project.register', { caminho, nome });
    ui.toast(t('projeto.registrado', { nome }), 'ok');
    document.dispatchEvent(new CustomEvent('projetos:atualizar'));
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
}

// ---------- conversão de vídeo ----------

/**
 * Pergunta "converter?" ao colar vídeos. Retorna { acao: 'converter' | 'original', naoPerguntar }
 * ou null se o usuário fechar o diálogo (cancela o colar inteiro).
 * Vídeos que já estão em codec de edição não entram na conta: esses são copiados de qualquer forma.
 */
export async function confirmarConversao(videos, totalItens = videos.length, config = {}) {
  const sondagens = await Promise.all(
    videos.slice(0, 24).map(async (caminho) => {
      try {
        return { caminho, dados: await call('media.probe', { caminho, perfil: config?.PerfilVideoPadrao ?? 'auto' }) };
      } catch {
        return { caminho, dados: null };
      }
    }),
  );

  const converter = sondagens.filter((s) => s.dados?.info && !s.dados.jaEditavel);

  if (converter.length === 0) {
    // Nada a converter (já é ProRes/DNxHR ou não deu para ler): segue o colar normal.
    if (sondagens.some((s) => !s.dados && videos.includes(s.caminho))) {
      ui.toast(t('video.semFfmpeg'), 'aviso', 6000);
    }
    return { acao: 'original', naoPerguntar: false };
  }

  const principal = converter[0];
  const info = principal.dados.info;
  const plano = principal.dados.plano;
  const totalOriginal = converter.length;
  const estimadoTotal = converter.reduce((soma, s) => soma + (s.dados.plano.TamanhoEstimadoBytes ?? 0), 0);

  const corpo = document.createElement('div');

  const detalhes = document.createElement('div');
  detalhes.style.display = 'grid';
  detalhes.style.gridTemplateColumns = 'auto 1fr';
  detalhes.style.gap = '5px 14px';
  detalhes.style.fontSize = '12px';
  detalhes.innerHTML = `
    <span style="color:var(--muted)">${t('video.origem')}</span><span style="color:var(--text-2)">${escapar(info.Codec)} · ${info.Largura} × ${info.Altura} · ${Number(info.Fps).toFixed(2)} fps · ${info.DezBits ? '10+' : '8'} bits${info.BitrateVideoMbps ? ` · ${Number(info.BitrateVideoMbps).toFixed(0)} Mbps` : ''}</span>
    <span style="color:var(--muted)">${t('video.duracao')}</span><span style="color:var(--text-2)">${info.duracaoTexto}</span>
    <span style="color:var(--muted)">${t('video.pastaDestino')}</span><span style="color:var(--text-2)">${escapar(plano.NomeExibicao)}${estimadoTotal > 0 ? ` · ~${fmt.bytes(estimadoTotal)}` : ''}</span>
  `;

  const avisos = document.createElement('div');
  avisos.style.display = 'flex';
  avisos.style.flexDirection = 'column';
  avisos.style.gap = '6px';
  if (plano.Motivo) avisos.appendChild(ui.caixaAviso(plano.Motivo, 'info'));
  for (const aviso of plano.avisos ?? []) avisos.appendChild(ui.caixaAviso(aviso, 'aviso'));

  const naoPerguntar = document.createElement('label');
  naoPerguntar.className = 'check-linha';
  const caixa = document.createElement('input');
  caixa.type = 'checkbox';
  naoPerguntar.append(caixa, document.createTextNode(t('video.naoPerguntar')));

  corpo.append(
    detalhes,
    ui.caixaAviso(t('video.confirmarDescricao'), 'info'),
    avisos,
    naoPerguntar,
  );

  return new Promise((resolve) => {
    let resolvido = false;
    const concluir = (acao, fechar) => {
      if (!resolvido) {
        resolvido = true;
        resolve({ acao, naoPerguntar: caixa.checked });
      }
      fechar();
    };

    ui.modal({
      titulo: t('video.confirmarTitulo'),
      descricao: totalOriginal > 1 || totalItens > 1
        ? t('video.confirmarVarios', { n: totalOriginal })
        : fmt.nomearArquivo(principal.caminho),
      corpo,
      botoes: [
        { texto: t('video.colarOriginal'), acao: (f) => concluir('original', f) },
        { texto: t('video.converter'), tipo: 'primario', acao: (f) => concluir('converter', f) },
      ],
      aoFechar: () => { if (!resolvido) resolve(null); },
    });
  });
}

export async function converterVideo(item, config, perfilInicial = null) {
  const caminho = item.Caminho ?? item.caminho;
  let info;

  try {
    info = await call('media.probe', { caminho, perfil: perfilInicial ?? config?.PerfilVideoPadrao ?? 'auto' });
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
    return;
  }

  if (!info?.info) {
    ui.toast(t('video.semFfmpeg'), 'erro', 7000);
    return;
  }

  const corpo = document.createElement('div');
  const tamanhoOriginal = item.Tamanho ?? item.tamanho ?? 0;

  const detalhes = document.createElement('div');
  detalhes.style.display = 'grid';
  detalhes.style.gridTemplateColumns = 'auto 1fr';
  detalhes.style.gap = '5px 14px';
  detalhes.style.fontSize = '12px';
  detalhes.innerHTML = `
    <span style="color:var(--muted)">${t('video.resolucao')}</span><span style="color:var(--text-2)">${info.info.Largura} × ${info.info.Altura} · ${Number(info.info.Fps).toFixed(2)} fps · ${info.info.DezBits ? '10+' : '8'} bits · ${escapar(info.info.PixFmt)}</span>
    <span style="color:var(--muted)">${t('video.duracao')}</span><span style="color:var(--text-2)">${info.info.duracaoTexto}</span>
    <span style="color:var(--muted)">${t('video.origem')}</span><span style="color:var(--text-2)">${escapar(info.info.Codec)}${info.info.BitrateVideoMbps ? ` · ${Number(info.info.BitrateVideoMbps).toFixed(0)} Mbps` : ''}${info.info.FaixasAudio > 1 ? ` · ${info.info.FaixasAudio} faixas de áudio` : ''}${info.info.TemAlpha ? ` · ${t('video.alpha')}` : ''}${info.info.Hdr ? ` · ${t('video.hdr')}` : ''}</span>
    <span style="color:var(--muted)">${t('video.arquivoAtual')}</span><span style="color:var(--text-2)">${fmt.bytes(tamanhoOriginal)}</span>
  `;

  const perfil = seletorPerfil(info.estimativas, perfilInicial ?? config?.PerfilVideoPadrao ?? 'auto');

  const estimativa = document.createElement('div');
  estimativa.style.fontSize = '12px';
  estimativa.style.color = 'var(--text-2)';

  const avisos = document.createElement('div');
  avisos.style.display = 'flex';
  avisos.style.flexDirection = 'column';
  avisos.style.gap = '6px';

  const atualizarPlano = async () => {
    try {
      const resposta = await call('media.probe', { caminho, perfil: perfil.value });
      const diferenca = tamanhoOriginal > 0 && resposta.plano.TamanhoEstimadoBytes > 0
        ? ` · ${Math.round((resposta.plano.TamanhoEstimadoBytes / tamanhoOriginal) * 100)}% do original`
        : '';
      estimativa.textContent = `${resposta.plano.NomeExibicao} · ${resposta.plano.estimadoTexto}${diferenca}`;

      avisos.replaceChildren();
      if (resposta.plano.Motivo) {
        avisos.appendChild(ui.caixaAviso(resposta.plano.Motivo, 'info'));
      }
      for (const aviso of resposta.plano.avisos ?? []) {
        avisos.appendChild(ui.caixaAviso(aviso, 'aviso'));
      }
    } catch {
      estimativa.textContent = '';
    }
  };

  perfil.addEventListener('change', atualizarPlano);
  await atualizarPlano();

  corpo.append(
    detalhes,
    ui.campo(t('video.perfil'), perfil),
    ui.campo(t('video.estimativa'), estimativa),
    ui.campo(t('video.avisos'), avisos),
    ui.caixaAviso(t('video.descricao'), 'info'),
  );

  ui.modal({
    titulo: t('video.titulo'),
    descricao: fmt.nomearArquivo(caminho),
    corpo,
    botoes: [
      { texto: t('dialogo.cancelar') },
      {
        texto: t('video.converter'),
        tipo: 'primario',
        acao: async (fechar) => {
          fechar();
          await executarConversao(caminho, perfil.value);
        },
      },
    ],
  });
}

async function executarConversao(caminho, perfil) {
  const opId = `conv-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  const destino = fmt.pastaDe(caminho);
  const card = ui.progresso({ id: opId, titulo: t('progresso.convertendo'), subtitulo: fmt.nomearArquivo(caminho) });
  card.aoCancelar = () => call('media.cancel', { opId }).catch(() => {});

  try {
    const resultado = await call('media.convert', { caminho, perfil, destino, opId });
    card.fechar(400);

    if (resultado?.ok) {
      ui.toast(t('video.concluido', {
        arquivo: fmt.nomearArquivo(resultado.saida),
        perfil: resultado.perfil ?? perfil,
      }), 'ok', 6000);
      document.dispatchEvent(new CustomEvent('recarregar'));
    } else {
      ui.toast(t('video.erro', { arquivo: fmt.nomearArquivo(caminho), erro: resultado?.erro ?? '—' }), 'erro', 8000);
    }
  } catch (erro) {
    card.fechar(400);
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
}

// ---------- conversão de sequência de imagens ----------

/**
 * Sequência de imagens (render, EXR de composição, DPX) vira um único vídeo.
 * O fps é escolhido aqui: o arquivo não carrega essa informação.
 */
export async function converterSequencia(item, config, perfilInicial = null) {
  const caminho = item.Caminho ?? item.caminho;

  let dados;
  try {
    dados = await call('media.sequencia', {
      caminho,
      perfil: perfilInicial ?? config?.PerfilVideoPadrao ?? 'auto',
    });
  } catch (erro) {
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
    return;
  }

  if (!dados?.sequencia) {
    ui.toast(t('video.semSequencia'), 'aviso', 6000);
    return;
  }

  const corpo = document.createElement('div');

  const detalhes = document.createElement('div');
  detalhes.style.display = 'grid';
  detalhes.style.gridTemplateColumns = 'auto 1fr';
  detalhes.style.gap = '5px 14px';
  detalhes.style.fontSize = '12px';

  const faixa = dados.sequencia.primeiro === dados.sequencia.ultimo
    ? String(dados.sequencia.primeiro).padStart(dados.sequencia.digitos, '0')
    : `${String(dados.sequencia.primeiro).padStart(dados.sequencia.digitos, '0')} – ${dados.sequencia.ultimo}`;

  detalhes.innerHTML = `
    <span style="color:var(--muted)">${t('video.sequenciaQuadros')}</span><span style="color:var(--text-2)">${dados.sequencia.quadros} (${escapar(dados.sequencia.nomeBase)}_${escapar(faixa)}${escapar(dados.sequencia.extensao)})</span>
    <span style="color:var(--muted)">${t('video.resolucao')}</span><span style="color:var(--text-2)">${dados.info.Largura} × ${dados.info.Altura} · ${dados.info.DezBits ? '10+' : '8'} bits · ${escapar(dados.info.PixFmt)}${dados.info.TemAlpha ? ` · ${t('video.alpha')}` : ''}</span>
    <span style="color:var(--muted)">${t('video.arquivoAtual')}</span><span style="color:var(--text-2)">${fmt.bytes(dados.sequencia.bytes)}</span>
  `;

  const fps = document.createElement('select');
  const sugestao = Math.round(dados.info.Fps) || 24;
  for (const valor of FPS_COMUNS) {
    const opcao = document.createElement('option');
    opcao.value = String(valor);
    opcao.textContent = `${valor} fps`;
    fps.appendChild(opcao);
  }
  fps.value = String(FPS_COMUNS.includes(sugestao) ? sugestao : 24);

  const perfil = seletorPerfil(dados.estimativas, perfilInicial ?? config?.PerfilVideoPadrao ?? 'auto');

  const estimativa = document.createElement('div');
  estimativa.style.fontSize = '12px';
  estimativa.style.color = 'var(--text-2)';

  const avisos = document.createElement('div');
  avisos.style.display = 'flex';
  avisos.style.flexDirection = 'column';
  avisos.style.gap = '6px';

  const atualizarPlano = async () => {
    try {
      const resposta = await call('media.sequencia', {
        caminho,
        perfil: perfil.value,
        fps: Number(fps.value),
      });

      if (!resposta?.plano) {
        estimativa.textContent = '';
        return;
      }

      estimativa.textContent = `${resposta.plano.NomeExibicao} · ${resposta.plano.estimadoTexto} · ${resposta.info.duracaoTexto}`;

      avisos.replaceChildren();
      if (resposta.plano.Motivo) avisos.appendChild(ui.caixaAviso(resposta.plano.Motivo, 'info'));
      if (dados.sequencia.comFalhas) {
        avisos.appendChild(ui.caixaAviso(t('video.sequenciaFalhas'), 'aviso'));
      }
      for (const aviso of resposta.plano.avisos ?? []) avisos.appendChild(ui.caixaAviso(aviso, 'aviso'));
    } catch {
      estimativa.textContent = '';
    }
  };

  perfil.addEventListener('change', atualizarPlano);
  fps.addEventListener('change', atualizarPlano);
  await atualizarPlano();

  corpo.append(
    detalhes,
    ui.campo(t('video.fps'), fps, t('video.fpsDica')),
    ui.campo(t('video.perfil'), perfil),
    ui.campo(t('video.estimativa'), estimativa),
    ui.campo(t('video.avisos'), avisos),
    ui.caixaAviso(t('video.sequenciaDescricao'), 'info'),
  );

  ui.modal({
    titulo: t('video.sequenciaTitulo'),
    descricao: `${dados.sequencia.nomeBase}${dados.sequencia.extensao}`,
    corpo,
    botoes: [
      { texto: t('dialogo.cancelar') },
      {
        texto: t('video.converter'),
        tipo: 'primario',
        acao: async (fechar) => {
          fechar();
          await executarConversaoSequencia(caminho, perfil.value, Number(fps.value));
        },
      },
    ],
  });
}

async function executarConversaoSequencia(caminho, perfil, fps) {
  const opId = `seq-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`;
  const destino = fmt.pastaDe(caminho);
  const card = ui.progresso({
    id: opId,
    titulo: t('progresso.convertendoSequencia'),
    subtitulo: fmt.nomearArquivo(caminho),
  });
  card.aoCancelar = () => call('media.cancel', { opId }).catch(() => {});

  try {
    const resultado = await call('media.convertSequencia', { caminho, perfil, fps, destino, opId });
    card.fechar(400);

    if (resultado?.ok) {
      ui.toast(t('video.concluido', {
        arquivo: fmt.nomearArquivo(resultado.saida),
        perfil: resultado.perfil ?? perfil,
      }), 'ok', 6000);
      document.dispatchEvent(new CustomEvent('recarregar'));
    } else {
      ui.toast(t('video.erro', { arquivo: fmt.nomearArquivo(caminho), erro: resultado?.erro ?? '—' }), 'erro', 8000);
    }
  } catch (erro) {
    card.fechar(400);
    ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
  }
}

onProgressoConversao();

function onProgressoConversao() {
  import('./bridge.js').then(({ on }) => {
    on('media.event', (dados) => {
      if (!dados || dados.Tipo !== 'convertendo') return;
      ui.atualizarProgresso(
        dados.OperacaoId,
        dados.Percentual,
        `${dados.Arquivo}${dados.Mensagem ? ' · ' + dados.Mensagem : ''}`,
      );
    });
  });
}

// ---------- configurações ----------

export async function abrirConfiguracoes(config, info, aoSalvar) {
  const corpo = document.createElement('div');

  const raizes = Array.isArray(config.Raizes) ? [...config.Raizes] : [];
  const listaRaizes = document.createElement('div');
  listaRaizes.className = 'lista-resultados';

  const redesenharRaizes = () => {
    listaRaizes.replaceChildren();
    if (raizes.length === 0) {
      const vazio = document.createElement('div');
      vazio.className = 'side-empty';
      vazio.textContent = t('lateral.semRaizes');
      listaRaizes.appendChild(vazio);
      return;
    }

    raizes.forEach((raiz, indice) => {
      const linha = document.createElement('div');
      linha.className = 'resultado-item';
      const texto = document.createElement('span');
      texto.textContent = raiz;
      texto.style.flex = '1';
      texto.style.overflow = 'hidden';
      texto.style.textOverflow = 'ellipsis';
      const remover = document.createElement('button');
      remover.className = 'mini-btn';
      remover.textContent = '×';
      remover.title = t('config.remover');
      remover.addEventListener('click', () => {
        raizes.splice(indice, 1);
        redesenharRaizes();
      });
      linha.append(texto, remover);
      listaRaizes.appendChild(linha);
    });
  };

  redesenharRaizes();

  const adicionarRaiz = document.createElement('button');
  adicionarRaiz.className = 'btn';
  adicionarRaiz.textContent = t('config.adicionarRaiz');
  adicionarRaiz.addEventListener('click', async () => {
    const resultado = await call('fs.pickFolder', { titulo: t('config.raizes'), inicial: raizes[0] ?? '' });
    if (resultado?.caminho && !raizes.includes(resultado.caminho)) {
      raizes.push(resultado.caminho);
      redesenharRaizes();
    }
  });

  const blocoRaizes = document.createElement('div');
  blocoRaizes.className = 'campo';
  const rotuloRaizes = document.createElement('label');
  rotuloRaizes.textContent = t('config.raizes');
  const dicaRaizes = document.createElement('span');
  dicaRaizes.className = 'dica';
  dicaRaizes.textContent = t('config.raizesDica');
  blocoRaizes.append(rotuloRaizes, dicaRaizes, listaRaizes, adicionarRaiz);

  const ffmpeg = ui.input({ valor: config.CaminhoFfmpeg ?? '', placeholder: 'C:\\ferramentas\\ffmpeg\\bin' });
  const estadoFfmpeg = ui.caixaAviso(
    info?.ffmpeg?.encontrado
      ? t('config.ffmpegOk', { caminho: info.ffmpeg.caminho })
      : t('config.ffmpegFaltando'),
    info?.ffmpeg?.encontrado ? 'info' : 'erro',
  );

  const miniatura = ui.input({ tipo: 'number', valor: String(config.TamanhoMiniatura ?? 256) });
  const grade = ui.input({ tipo: 'number', valor: String(config.TamanhoGrade ?? 168) });
  const paralelismo = ui.input({ tipo: 'number', valor: String(config.ParalelismoBusca ?? 8) });

  const perfil = document.createElement('select');
  for (const valor of PERFIS) {
    const opcao = document.createElement('option');
    opcao.value = valor;
    opcao.textContent = t(`video.perfis.${valor}`);
    perfil.appendChild(opcao);
  }
  perfil.value = config.PerfilVideoPadrao ?? 'auto';

  const criarCheck = (rotulo, marcado) => {
    const label = document.createElement('label');
    label.className = 'check-linha';
    const caixa = document.createElement('input');
    caixa.type = 'checkbox';
    caixa.checked = Boolean(marcado);
    label.append(caixa, document.createTextNode(rotulo));
    return { label, caixa };
  };

  const ocultos = criarCheck(t('config.ocultos'), config.MostrarOcultos);
  const converterColar = criarCheck(t('config.converterColar'), config.ConverterVideoAoColar);
  const perguntarConversao = criarCheck(t('config.perguntarConversao'), config.PerguntarAntesDeConverter !== false);
  const verificar = criarCheck(t('config.verificarAtualizacoes'), config.VerificarAtualizacoes);

  const linhaDupla1 = document.createElement('div');
  linhaDupla1.className = 'linha-dupla';
  linhaDupla1.append(
    ui.campo(t('config.miniatura'), miniatura),
    ui.campo(t('config.grade'), grade),
  );

  const abrirDados = document.createElement('button');
  abrirDados.className = 'btn';
  abrirDados.textContent = t('config.abrirDados');
  abrirDados.addEventListener('click', () => call('app.abrirPastaDados').catch(() => {}));

  corpo.append(
    blocoRaizes,
    ui.campo(t('config.ffmpeg'), ffmpeg, t('config.ffmpegDica')),
    estadoFfmpeg,
    linhaDupla1,
    ui.campo(t('config.paralelismo'), paralelismo),
    ui.campo(t('config.perfilPadrao'), perfil),
    ocultos.label,
    converterColar.label,
    perguntarConversao.label,
    verificar.label,
    abrirDados,
  );

  ui.modal({
    titulo: t('config.titulo'),
    corpo,
    largo: true,
    botoes: [
      { texto: t('dialogo.cancelar') },
      {
        texto: t('dialogo.salvar'),
        tipo: 'primario',
        acao: async (fechar) => {
          const atualizado = {
            ...config,
            Raizes: raizes,
            CaminhoFfmpeg: ffmpeg.value.trim() || null,
            TamanhoMiniatura: Number(miniatura.value) || 256,
            TamanhoGrade: Number(grade.value) || 168,
            ParalelismoBusca: Number(paralelismo.value) || 8,
            PerfilVideoPadrao: perfil.value,
            MostrarOcultos: ocultos.caixa.checked,
            ConverterVideoAoColar: converterColar.caixa.checked,
            PerguntarAntesDeConverter: perguntarConversao.caixa.checked,
            VerificarAtualizacoes: verificar.caixa.checked,
          };

          try {
            await call('settings.save', atualizado);
            ui.toast(t('config.salvo'), 'ok');
            fechar();
            aoSalvar?.(atualizado);
          } catch (erro) {
            ui.toast(t('erro.geral', { mensagem: erro.message }), 'erro');
          }
        },
      },
    ],
  });
}

function escapar(texto) {
  return String(texto ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}
