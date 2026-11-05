import * as THREE from '../vendor/three/build/three.module.min.js';
import { GLTFLoader } from '../vendor/three/examples/jsm/loaders/GLTFLoader.js';
import { OBJLoader } from '../vendor/three/examples/jsm/loaders/OBJLoader.js';
import { STLLoader } from '../vendor/three/examples/jsm/loaders/STLLoader.js';
import { FBXLoader } from '../vendor/three/examples/jsm/loaders/FBXLoader.js';
import { USDZLoader } from '../vendor/three/examples/jsm/loaders/USDZLoader.js';
import { PLYLoader } from '../vendor/three/examples/jsm/loaders/PLYLoader.js';
import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';
import * as splats from './gaussianSplats.js';
import * as ambiente from './ambiente.js';
import * as controlesVideo from './controlesVideo.js';
import * as saidaAudio from './saidaAudio.js';

/**
 * Prévia rápida: uma janela mínima com o item escolhido (barra de espaço no explorador).
 * Imagem, vídeo, modelo 3D ou a mesma imagem aberta como ambiente lat-long.
 *
 * No 3D: arrastar com o esquerdo orbita, com o meio desloca e com o direito (ou a roda)
 * aproxima. A cena usa tone mapping ACES com exposição ajustável — é o que faz um HDR
 * aparecer como HDR em vez de estourar em branco.
 */
let atual = null;

const raiz = () => document.getElementById('previa-rapida-corpo');
const CHAVE_PREFERENCIAS = 'cvexplorer:previa3d';

let preferencias = { ambiente: 'estudio', exposicao: 1 };
try {
  const salvo = JSON.parse(localStorage.getItem(CHAVE_PREFERENCIAS) ?? 'null');
  if (salvo) preferencias = { ...preferencias, ...salvo };
} catch {
  // preferências antigas inválidas: segue no padrão
}

const salvarPreferencias = () => localStorage.setItem(CHAVE_PREFERENCIAS, JSON.stringify(preferencias));

export function iniciar(caminho) {
  if (!caminho) return;

  encerrar();
  const ext = fmt.extensao(caminho);
  const categoria = fmt.categoria(ext);

  atual = { caminho, ext, categoria, modo: categoria === 'video' ? 'video' : 'imagem' };

  const secao = document.getElementById('previa-rapida');
  if (secao) secao.hidden = false;

  document.title = `${fmt.nomearArquivo(caminho)} — CV Explorer`;
  const nome = document.getElementById('previa-rapida-nome');
  if (nome) nome.textContent = fmt.nomearArquivo(caminho);

  prepararBarra();
  prepararSom(null);

  if (categoria === 'modelo3d') {
    abrirModelo(caminho, ext).catch(falhou);
    return;
  }

  if (categoria === 'video') {
    mostrarVideo(caminho).catch(falhou);
    return;
  }

  abrirImagem(caminho).catch(falhou);
}

function encerrar() {
  if (atual?.limpar) {
    try {
      atual.limpar();
    } catch {
      // silencioso
    }
  }

  atual = null;
  raiz()?.replaceChildren();
}

function falhou(erro) {
  const aviso = document.createElement('div');
  aviso.className = 'previa-rapida-falha';
  aviso.textContent = t('previa.falhou', { mensagem: erro?.message ?? '—' });
  raiz()?.replaceChildren(aviso);
}

// ---- barra de controles --------------------------------------------------

/**
 * Saída de áudio do vídeo. Fica na linha do nome do arquivo (nada flutua por cima da
 * imagem) e só aparece quando há vídeo tocando.
 */
function prepararSom(video) {
  const caixa = document.getElementById('previa-rapida-som');
  if (!caixa) return;

  caixa.replaceChildren();
  caixa.hidden = !video;
  if (!video) return;

  caixa.append(saidaAudio.montarSeletor({ video }));
}

function prepararBarra() {
  const acoes = document.getElementById('previa-rapida-acoes');
  if (!acoes || !atual) return;

  const seletor = document.getElementById('previa-ambiente');
  const hdri = document.getElementById('previa-hdri');
  const exposicao = document.getElementById('previa-exposicao');
  const campoExposicao = document.getElementById('previa-exposicao-campo');
  const latlong = document.getElementById('previa-latlong');
  const dica = document.querySelector('.previa-rapida-dica');

  const eh3d = atual.categoria === 'modelo3d';
  const ehImagem = atual.categoria !== 'modelo3d' && atual.categoria !== 'video';

  if (seletor && seletor.options.length === 0) {
    for (const preset of ambiente.PRESETS) {
      const opcao = document.createElement('option');
      opcao.value = preset.chave;
      opcao.textContent = preset.rotulo;
      seletor.appendChild(opcao);
    }

    // HDRI carregado pelo usuário entra como opção extra.
    const opcao = document.createElement('option');
    opcao.value = 'hdri';
    opcao.textContent = 'HDRI carregado';
    opcao.hidden = true;
    seletor.appendChild(opcao);
  }

  if (seletor) {
    seletor.value = atual.hdriTextura ? 'hdri' : preferencias.ambiente;
    seletor.closest('.previa-campo').hidden = !eh3d;
  }

  if (hdri) hdri.hidden = !eh3d;
  if (campoExposicao) campoExposicao.hidden = !eh3d && !ehImagem;
  if (exposicao) exposicao.value = String(Math.round(preferencias.exposicao * 100));
  if (dica) dica.style.display = eh3d ? '' : 'none';

  acoes.hidden = !eh3d && !ehImagem;

  if (latlong) {
    latlong.hidden = !ehImagem;
    const emAmbiente = atual.modo === 'ambiente';
    latlong.classList.toggle('ativo', emAmbiente);
    latlong.textContent = emAmbiente ? t('previa.imagemNormal') : t('previa.latlong');
    latlong.onclick = () => {
      const caminho = atual?.caminho;
      if (!caminho) return;
      if (atual.modo === 'ambiente') abrirImagem(caminho).catch(falhou);
      else abrirAmbiente(caminho).catch(falhou);
    };
  }

  if (seletor) {
    seletor.onchange = () => {
      preferencias.ambiente = seletor.value;
      salvarPreferencias();
      atual?.aplicarAmbiente?.();
    };
  }

  if (hdri) {
    hdri.onclick = async () => {
      try {
        const escolha = await call('fs.pickFile', {
          titulo: t('previa.carregarHdri'),
          filtro: 'HDR/EXR (*.hdr;*.exr)|*.hdr;*.exr|Todos os arquivos (*.*)|*.*',
        });
        if (!escolha?.caminho) return;
        await atual?.carregarHdri?.(escolha.caminho, fmt.extensao(escolha.caminho));
        prepararBarra();
      } catch (erro) {
        falhou(erro);
      }
    };
  }

  if (exposicao) {
    exposicao.oninput = () => {
      preferencias.exposicao = Number(exposicao.value) / 100;
      salvarPreferencias();
      atual?.aplicarExposicao?.(preferencias.exposicao);
    };
  }
}

// ---- imagem (plana e como ambiente lat-long) -----------------------------

async function abrirImagem(caminho) {
  const ext = fmt.extensao(caminho);
  const resposta = await call('fs.preview', { caminho, max: 3200 });

  const img = document.createElement('img');
  img.className = 'previa-rapida-imagem';
  img.src = `data:${resposta.mime};base64,${resposta.dados}`;
  img.alt = fmt.nomearArquivo(caminho);
  raiz().replaceChildren(img);

  atual = { caminho, ext, categoria: fmt.categoria(ext), modo: 'imagem' };
  prepararBarra();
  return resposta;
}

async function abrirAmbiente(caminho) {
  const ext = fmt.extensao(caminho);
  const categoria = fmt.categoria(ext);

  // Guarda o que precisa antes de `iniciarCena` trocar o `atual`.
  const dados = ext === 'hdr' || ext === 'exr'
    ? null
    : await call('fs.preview', { caminho, max: 3200 });

  atual = { caminho, ext, categoria, modo: 'ambiente' };

  iniciarCena({
    semModelo: true,
    aoPreparar: async (contexto) => {
      let textura;
      if (ext === 'hdr' || ext === 'exr') {
        const { url } = await call('fs.arquivoUrl', { caminho });
        textura = await ambiente.carregarHdri(url, ext);
      } else {
        textura = await new THREE.TextureLoader().loadAsync(`data:${dados.mime};base64,${dados.dados}`);
      }

      contexto.definirPanorama(textura);
    },
  });

  prepararBarra();
}

// ---- vídeo --------------------------------------------------------------

async function mostrarVideo(caminho) {
  const corpo = raiz();
  const video = document.createElement('video');
  video.className = 'previa-rapida-video';
  // Sem `autoplay`: cada reabertura do fluxo (busca, laço) decide se toca ou fica pausada.
  video.autoplay = false;
  // Só o arquivo original (Range no próprio arquivo) ganha o controle nativo: no fluxo
  // do ffmpeg a duração é desconhecida e a barra nativa se comporta como transmissão ao
  // vivo, com o cabeçote pulando. Nesse caso usamos os controles da casa.
  video.controls = false;
  video.loop = false;
  corpo.replaceChildren(video);

  let sessao = null;
  let base = 0; // instante do arquivo em que o fluxo atual começa
  let total = 0;
  let reabrindo = false;
  let fechado = false;
  let controles = null;

  const fecharSessao = () => {
    if (!sessao) return;
    call('media.playFechar', { id: sessao.id ?? undefined, midiaId: sessao.midiaId ?? undefined })
      .catch(() => {});
    sessao = null;
  };

  const tocar = async (inicio, continuar) => {
    fecharSessao();

    const resposta = await call('media.play', { caminho, inicio });
    if (!resposta?.ok) {
      throw new Error(resposta?.erro ?? '—');
    }

    sessao = resposta;
    base = resposta.nativo ? 0 : Number(resposta.inicio ?? inicio) || 0;
    total = total || resposta.duracao || 0;
    // O loop do elemento só serve para o arquivo original; o fluxo é sessão única e
    // releria a mesma URL já morta, então a volta é pedida de novo (evento `ended`).
    video.loop = Boolean(resposta.nativo);
    video.controls = Boolean(resposta.nativo);
    video.src = resposta.url;
    video.load();

    if (!continuar) return;

    // Autoplay com som pode ser barrado quando a janela nasce de um atalho de outra janela.
    // Nesse caso toca sem som (o usuário liga no controle) em vez de ficar parado.
    video.play().catch(() => {
      video.muted = true;
      video.play().catch(() => {});
    });
  };

  await tocar(0, true);

  // A escolha da saída de áudio fica no cabeçalho, junto do nome — vale para os dois
  // modos (controle nativo e barra da casa).
  prepararSom(video);

  if (!video.controls) {
    controles = controlesVideo.montarControles({
      video,
      hospedeiro: corpo,
      caminho,
      total,
      obterInicio: () => base,
      aoBuscar: (alvo, continuar) => tocar(alvo, continuar),
    });
  }

  video.addEventListener('ended', async () => {
    if (reabrindo || fechado) return;
    reabrindo = true;
    try {
      await tocar(0, true);
    } catch {
      // sem fluxo novo: mantém o último quadro em vez de derrubar a janela
    } finally {
      reabrindo = false;
    }
  });

  atual.limpar = () => {
    fechado = true;
    controles?.destruir();
    fecharSessao();
    video.pause();
    video.removeAttribute('src');
  };
}

// ---- modelo 3D ----------------------------------------------------------

async function abrirModelo(caminho, ext) {
  if (ext === 'ply') {
    // PLY pode ser malha ou gaussian splats: o cabeçalho decide.
    const { url } = await call('fs.arquivoUrl', { caminho });
    const bruto = await (await fetch(url)).arrayBuffer();

    if (splats.ehSplat(bruto)) {
      iniciarCena({
        aoPreparar: async (contexto) => {
          const nuvem = await splats.carregar(url);
          contexto.definirObjeto(nuvem.objeto);
          // O tamanho em pixels depende da altura real do buffer (devicePixelRatio incluso)
          // e a ordem dos splats depende da câmera: as duas coisas são acertadas antes de
          // cada desenho.
          const tamanho = new THREE.Vector2();
          const acertarTela = () => {
            contexto.renderizador.getDrawingBufferSize(tamanho);
            nuvem.atualizarTela(tamanho.x, tamanho.y);
          };
          acertarTela();
          contexto.aoRedimensionar(acertarTela);
          contexto.aoRenderizar(() => nuvem.atualizarCamera(contexto.camera));
          contexto.aoLimpar(() => nuvem.dispose());
        },
      });
      return;
    }

    iniciarCena({
      aoPreparar: async (contexto) => {
        const loader = new PLYLoader();
        const geometria = await loader.loadAsync(url);
        geometria.computeVertexNormals();
        const temCor = Boolean(geometria.getAttribute('color'));
        contexto.definirObjeto(new THREE.Mesh(
          geometria,
          new THREE.MeshStandardMaterial({
            color: temCor ? 0xffffff : 0xb9c0c9,
            vertexColors: temCor,
            metalness: 0.1,
            roughness: 0.7,
          }),
        ));
      },
    });
    return;
  }

  const arquivo = await call('modelo.arquivo', { caminho });
  if (!arquivo?.ok) {
    throw new Error(arquivo?.erro ?? t('previa.semModelo', { ext }));
  }

  iniciarCena({
    aoPreparar: async (contexto) => {
      const loader = await criadorDeLoader(arquivo.ext ?? ext);
      if (!loader) throw new Error(t('previa.semModelo', { ext: arquivo.ext ?? ext }));
      contexto.definirObjeto(await carregarModelo(loader, arquivo.url, arquivo.ext ?? ext));
    },
  });
}

/**
 * Cena única de modelos, splats e panorama: renderer com tone mapping, luz de estúdio,
 * órbita com os três botões e ambiente que pode ser trocado sem recarregar o modelo.
 */
function iniciarCena({ aoPreparar, semModelo = false }) {
  const corpo = document.getElementById('previa-rapida-corpo');
  const canvas = document.createElement('canvas');
  canvas.className = 'previa-rapida-canvas';
  corpo.replaceChildren(canvas);

  const largura = corpo.clientWidth || 720;
  const altura = corpo.clientHeight || 520;

  const renderizador = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderizador.setPixelRatio(window.devicePixelRatio || 1);
  renderizador.setSize(largura, altura, false);
  renderizador.setClearColor(0x121316, 1);
  renderizador.outputColorSpace = THREE.SRGBColorSpace;
  renderizador.toneMapping = THREE.ACESFilmicToneMapping;
  renderizador.toneMappingExposure = preferencias.exposicao;

  const cena = new THREE.Scene();
  cena.background = new THREE.Color(0x121316);

  const ambienteLuz = new THREE.AmbientLight(0xffffff, 0.35);
  const principal = new THREE.DirectionalLight(0xffffff, 1.5);
  principal.position.set(3, 5, 4);
  const preenchimento = new THREE.DirectionalLight(0x9fc4e8, 0.5);
  preenchimento.position.set(-4, -2, -3);
  cena.add(ambienteLuz, principal, preenchimento);

  const camera = new THREE.PerspectiveCamera(40, largura / altura, 0.0001, 10000);

  // A órbita é um quaternion, não azimute/elevação: com ângulos esféricos a câmera
  // atravessa o polo e o mundo "vira" de lado. Com o giro acumulado (yaw no eixo
  // vertical do mundo + pitch no eixo horizontal da tela) a rotação continua de onde
  // parou e o modelo pode ficar de cabeça para baixo.
  const orbita = {
    rotacao: new THREE.Quaternion().setFromEuler(new THREE.Euler(-0.35, 0.7, 0, 'YXZ')),
    alvo: new THREE.Vector3(),
  };
  const eixoY = new THREE.Vector3(0, 1, 0);
  const eixoX = new THREE.Vector3(1, 0, 0);
  let raioModelo = 1;
  let distancia = 3.2 * raioModelo;
  let panorama = false;

  const aplicarCamera = () => {
    const d = panorama ? 0.0001 : distancia;
    camera.position.copy(orbita.alvo).add(new THREE.Vector3(0, 0, d).applyQuaternion(orbita.rotacao));
    // O "cima" da câmera acompanha o giro: sem isso o `lookAt` força o horizonte a
    // ficar nivelado e o modelo nunca aparece invertido.
    camera.up.copy(eixoY).applyQuaternion(orbita.rotacao);
    camera.lookAt(orbita.alvo);
    camera.updateMatrixWorld();
    for (const acao of antesDoDesenho) acao();
    renderizador.render(cena, camera);
  };

  const redimensionar = () => {
    const l = corpo.clientWidth || largura;
    const a = corpo.clientHeight || altura;
    renderizador.setSize(l, a, false);
    camera.aspect = l / a;
    camera.updateProjectionMatrix();
    aplicarCamera();
  };

  window.addEventListener('resize', redimensionar);

  // ---- mouse: esquerdo orbita, meio desloca, direito/roda aproxima ----
  let botao = -1;
  let anterior = { x: 0, y: 0 };

  canvas.addEventListener('pointerdown', (evento) => {
    botao = evento.button;
    anterior = { x: evento.clientX, y: evento.clientY };
    canvas.setPointerCapture?.(evento.pointerId);
    evento.preventDefault();
  });

  canvas.addEventListener('pointermove', (evento) => {
    if (botao < 0) return;

    const dx = evento.clientX - anterior.x;
    const dy = evento.clientY - anterior.y;
    anterior = { x: evento.clientX, y: evento.clientY };

    if (botao === 0) {
      // Yaw no eixo vertical do mundo, pitch no eixo horizontal da tela, sem limite.
      const giroY = new THREE.Quaternion().setFromAxisAngle(eixoY, -dx * 0.008);
      const giroX = new THREE.Quaternion().setFromAxisAngle(eixoX, -dy * 0.008);
      orbita.rotacao.premultiply(giroY).multiply(giroX);
    } else if (botao === 1) {
      const escala = Math.max(distancia, raioModelo) * 0.0016;
      const direita = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 0);
      const cima = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 1);
      orbita.alvo.addScaledVector(direita, -dx * escala);
      orbita.alvo.addScaledVector(cima, dy * escala);
    } else if (!panorama) {
      distancia = Math.max(raioModelo * 0.05, distancia + dy * raioModelo * 0.006);
    }

    aplicarCamera();
  });

  const soltar = (evento) => {
    botao = -1;
    canvas.releasePointerCapture?.(evento.pointerId);
  };

  canvas.addEventListener('pointerup', soltar);
  canvas.addEventListener('pointerleave', soltar);
  canvas.addEventListener('wheel', (evento) => {
    evento.preventDefault();
    if (!panorama) {
      distancia = Math.max(raioModelo * 0.05, Math.min(raioModelo * 50, distancia * (1 + Math.sign(evento.deltaY) * 0.12)));
      aplicarCamera();
    }
  }, { passive: false });
  canvas.addEventListener('mousedown', (evento) => evento.preventDefault());
  canvas.addEventListener('contextmenu', (evento) => evento.preventDefault());

  const alvos = [];
  const limpezas = [];
  const antesDoDesenho = [];
  let ambienteGerado = null;
  let texturaPanorama = null;

  const contexto = {
    renderizador,
    canvas,
    camera,
    definirObjeto(objeto) {
      const grupo = new THREE.Group();
      grupo.add(objeto);
      cena.add(grupo);
      alvos.push(grupo);

      objeto.updateMatrixWorld(true);
      const caixa = new THREE.Box3().setFromObject(objeto, true);
      if (!caixa.isEmpty()) {
        const centro = caixa.getCenter(new THREE.Vector3());
        raioModelo = caixa.getBoundingSphere(new THREE.Sphere()).radius || 1;
        objeto.position.sub(centro);
        distancia = raioModelo * 2.4;
      }

      aplicarCamera();
    },
    definirPanorama(textura) {
      texturaPanorama = textura;
      panorama = true;
      distancia = 0.0001;
      camera.fov = 75;
      camera.updateProjectionMatrix();
      cena.background = textura;
      cena.remove(principal, preenchimento, ambienteLuz);
      aplicarCamera();
    },
    definirAmbiente(gerado) {
      if (ambienteGerado?.alvo) ambienteGerado.alvo.dispose();
      ambienteGerado = gerado;
      cena.environment = gerado.textura;
      aplicarCamera();
    },
    aoRedimensionar(acao) {
      const ouvinte = () => acao();
      window.addEventListener('resize', ouvinte);
      limpezas.push(() => window.removeEventListener('resize', ouvinte));
      acao();
    },
    /** Gancho antes de cada desenho (splats: reordenar pela câmera). */
    aoRenderizar(acao) {
      antesDoDesenho.push(acao);
    },
    aoLimpar(acao) {
      limpezas.push(acao);
    },
  };

  atual.aplicarExposicao = (valor) => {
    renderizador.toneMappingExposure = valor;
    aplicarCamera();
  };

  atual.aplicarAmbiente = () => {
    if (panorama || semModelo) {
      // Panorama já traz a própria luz na textura.
      if (texturaPanorama) {
        contexto.definirAmbiente(ambiente.ambienteDeImagem(renderizador, texturaPanorama));
      }
      return;
    }

    if (ambienteGerado?.alvo) ambienteGerado.alvo.dispose();
    ambienteGerado = null;

    if (preferencias.ambiente === 'nenhum') {
      cena.environment = null;
      cena.background = new THREE.Color(0x121316);
      aplicarCamera();
      return;
    }

    if (preferencias.ambiente === 'hdri' && atual?.hdriTextura) {
      contexto.definirAmbiente(ambiente.ambienteDeImagem(renderizador, atual.hdriTextura));
      return;
    }

    const gerado = ambiente.gerarPreset(renderizador, preferencias.ambiente);
    if (gerado.textura) {
      // O preset ilumina o modelo; o fundo continua o escuro do app (o cubemap difuso
      // do PMREM não serve como céu — vira uma mancha).
      contexto.definirAmbiente(gerado);
    }
  };

  atual.carregarHdri = async (caminho, ext) => {
    const { url } = await call('fs.arquivoUrl', { caminho });
    atual.hdriTextura = await ambiente.carregarHdri(url, ext);
    preferencias.ambiente = 'hdri';
    salvarPreferencias();

    const seletor = document.getElementById('previa-ambiente');
    if (seletor) {
      const opcao = seletor.querySelector('option[value="hdri"]');
      if (opcao) opcao.hidden = false;
      seletor.value = 'hdri';
    }

    contexto.definirAmbiente(ambiente.ambienteDeImagem(renderizador, atual.hdriTextura));
    if (!panorama) cena.background = atual.hdriTextura;
  };

  atual.limpar = () => {
    window.removeEventListener('resize', redimensionar);
    limpezas.forEach((acao) => {
      try {
        acao();
      } catch {
        // silencioso
      }
    });
    if (ambienteGerado?.alvo) ambienteGerado.alvo.dispose();
    renderizador.dispose();
    alvos.forEach((grupo) => {
      grupo.traverse?.((filho) => {
        filho.geometry?.dispose?.();
        const material = filho.material;
        if (Array.isArray(material)) material.forEach((m) => m?.dispose?.());
        else material?.dispose?.();
      });
    });
  };

  aplicarCamera();
  atual.aplicarAmbiente();

  Promise.resolve(aoPreparar(contexto))
    .then(() => aplicarCamera())
    .catch(falhou);
}

async function criadorDeLoader(ext) {
  switch (ext) {
    case 'glb':
    case 'gltf':
      return new GLTFLoader();
    case 'obj':
      return new OBJLoader();
    case 'stl':
      return new STLLoader();
    case 'fbx':
      return new FBXLoader();
    case 'usdz':
      return new USDZLoader();
    case 'ply':
      return new PLYLoader();
    case 'dae': {
      // Sob demanda: o ColladaLoader arrasta o TGALoader junto.
      const { ColladaLoader } = await import('../vendor/three/examples/jsm/loaders/ColladaLoader.js');
      return new ColladaLoader();
    }
    default:
      return null;
  }
}

function carregarModelo(loader, url, ext) {
  return new Promise((resolve, reject) => {
    loader.load(
      url,
      (resultado) => {
        if (!resultado) {
          reject(new Error(t('previa.semGeometria')));
        } else if (ext === 'glb' || ext === 'gltf') {
          resolve(resultado.scene);
        } else if (ext === 'stl') {
          resultado.computeVertexNormals();
          resolve(new THREE.Mesh(
            resultado,
            new THREE.MeshStandardMaterial({ color: 0xb9c0c9, metalness: 0.1, roughness: 0.7 }),
          ));
        } else {
          resolve(resultado.scene ?? resultado);
        }
      },
      undefined,
      (erro) => reject(erro instanceof Error ? erro : new Error('falha ao ler o modelo')),
    );
  });
}
