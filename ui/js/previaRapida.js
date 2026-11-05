import * as THREE from '../vendor/three/build/three.module.min.js';
import { GLTFLoader } from '../vendor/three/examples/jsm/loaders/GLTFLoader.js';
import { OBJLoader } from '../vendor/three/examples/jsm/loaders/OBJLoader.js';
import { STLLoader } from '../vendor/three/examples/jsm/loaders/STLLoader.js';
import { FBXLoader } from '../vendor/three/examples/jsm/loaders/FBXLoader.js';
import { USDZLoader } from '../vendor/three/examples/jsm/loaders/USDZLoader.js';
import { call } from './bridge.js';
import { t } from './i18n.js';
import * as fmt from './format.js';

/**
 * Prévia rápida: uma janela mínima com o item escolhido (barra de espaço no explorador).
 * Imagem, vídeo ou modelo 3D. No 3D: arrastar com o esquerdo orbita, com o meio desloca e
 * com o direito (ou a roda) aproxima — sem depender de OrbitControls, que não vem junto.
 */
let atual = null;

const raiz = () => document.getElementById('previa-rapida-corpo');

export function iniciar(caminho) {
  if (!caminho) return;

  encerrar();
  const ext = fmt.extensao(caminho);
  const categoria = fmt.categoria(ext);
  atual = { caminho };

  const secao = document.getElementById('previa-rapida');
  if (secao) secao.hidden = false;

  document.title = `${fmt.nomearArquivo(caminho)} — CV Explorer`;
  const nome = document.getElementById('previa-rapida-nome');
  if (nome) nome.textContent = fmt.nomearArquivo(caminho);

  // A dica de órbita só faz sentido no 3D.
  const dica = document.querySelector('.previa-rapida-dica');
  if (dica) dica.style.display = categoria === 'modelo3d' ? '' : 'none';

  if (categoria === 'modelo3d') {
    mostrar3d(caminho, ext).catch((erro) => falhou(erro));
    return;
  }

  if (categoria === 'video') {
    mostrarVideo(caminho).catch((erro) => falhou(erro));
    return;
  }

  mostrarImagem(caminho).catch((erro) => falhou(erro));
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

async function mostrarImagem(caminho) {
  const resposta = await call('fs.preview', { caminho, max: 3200 });
  const img = document.createElement('img');
  img.className = 'previa-rapida-imagem';
  img.src = `data:${resposta.mime};base64,${resposta.dados}`;
  img.alt = fmt.nomearArquivo(caminho);
  raiz().replaceChildren(img);
}

async function mostrarVideo(caminho) {
  const video = document.createElement('video');
  video.className = 'previa-rapida-video';
  video.controls = true;
  video.autoplay = true;
  raiz().replaceChildren(video);

  const resposta = await call('media.play', { caminho, inicio: 0 });
  if (!resposta?.ok) {
    throw new Error(resposta?.erro ?? '—');
  }

  video.src = resposta.url;
  video.play().catch(() => {});
  atual.limpar = () => {
    call('media.playFechar', { id: resposta.id ?? undefined, midiaId: resposta.midiaId ?? undefined })
      .catch(() => {});
    video.pause();
    video.removeAttribute('src');
  };
}

function criadorDeLoader(ext) {
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
    default:
      return null;
  }
}

async function mostrar3d(caminho, ext) {
  const loader = criadorDeLoader(ext);
  if (!loader) {
    throw new Error(t('previa.semModelo', { ext }));
  }

  const { url } = await call('fs.arquivoUrl', { caminho });
  const objeto = await carregarModelo(loader, url, ext);

  const alvo = raiz();
  const canvas = document.createElement('canvas');
  canvas.className = 'previa-rapida-canvas';
  alvo.replaceChildren(canvas);

  const largura = alvo.clientWidth || 720;
  const altura = alvo.clientHeight || 520;

  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio || 1);
  renderer.setSize(largura, altura, false);
  renderer.setClearColor(0x121316, 1);

  const cena = new THREE.Scene();
  cena.background = new THREE.Color(0x121316);
  cena.add(new THREE.AmbientLight(0xffffff, 1.5));

  const principal = new THREE.DirectionalLight(0xffffff, 2.2);
  principal.position.set(3, 5, 4);
  cena.add(principal);

  const preenchimento = new THREE.DirectionalLight(0x9fc4e8, 0.9);
  preenchimento.position.set(-4, -2, -3);
  cena.add(preenchimento);

  const grupo = new THREE.Group();
  grupo.add(objeto);
  cena.add(grupo);

  objeto.updateMatrixWorld(true);
  const caixa = new THREE.Box3().setFromObject(objeto, true);
  if (caixa.isEmpty()) {
    throw new Error(t('previa.semGeometria'));
  }

  const centro = caixa.getCenter(new THREE.Vector3());
  const raio = caixa.getBoundingSphere(new THREE.Sphere()).radius || 1;
  objeto.position.sub(centro);

  const camera = new THREE.PerspectiveCamera(40, largura / altura, 0.001, 1000);

  // Câmera orbital: distância, azimute e elevação. O alvo é o centro do modelo.
  const orbita = { distancia: 3.2, azimute: 0.7, elevacao: 0.42, alvo: new THREE.Vector3(0, 0, 0) };
  const aplicarCamera = () => {
    const d = orbita.distancia * raio;
    camera.position.set(
      orbita.alvo.x + d * Math.cos(orbita.elevacao) * Math.sin(orbita.azimute),
      orbita.alvo.y + d * Math.sin(orbita.elevacao),
      orbita.alvo.z + d * Math.cos(orbita.elevacao) * Math.cos(orbita.azimute),
    );
    camera.lookAt(orbita.alvo);
    renderer.render(cena, camera);
  };

  const desenhar = () => {
    const l = alvo.clientWidth || largura;
    const a = alvo.clientHeight || altura;
    renderer.setSize(l, a, false);
    camera.aspect = l / a;
    camera.updateProjectionMatrix();
    aplicarCamera();
  };

  let botao = -1;
  let anterior = { x: 0, y: 0 };

  const aoPressionar = (evento) => {
    botao = evento.button;
    anterior = { x: evento.clientX, y: evento.clientY };
    canvas.setPointerCapture?.(evento.pointerId);
    evento.preventDefault();
  };

  const aoMover = (evento) => {
    if (botao < 0) return;

    const dx = evento.clientX - anterior.x;
    const dy = evento.clientY - anterior.y;
    anterior = { x: evento.clientX, y: evento.clientY };

    if (botao === 0) {
      orbita.azimute -= dx * 0.008;
      orbita.elevacao = Math.max(-1.5, Math.min(1.5, orbita.elevacao + dy * 0.008));
    } else if (botao === 1) {
      // Pan: desloca o alvo no plano da tela.
      const escala = orbita.distancia * raio * 0.0016;
      const direita = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 0);
      const cima = new THREE.Vector3().setFromMatrixColumn(camera.matrix, 1);
      orbita.alvo.addScaledVector(direita, -dx * escala);
      orbita.alvo.addScaledVector(cima, dy * escala);
    } else {
      orbita.distancia = Math.max(0.15, orbita.distancia + dy * 0.006);
    }

    aplicarCamera();
  };

  const aoSoltar = (evento) => {
    botao = -1;
    canvas.releasePointerCapture?.(evento.pointerId);
  };

  const aoRolar = (evento) => {
    evento.preventDefault();
    orbita.distancia = Math.max(0.15, Math.min(20, orbita.distancia * (1 + Math.sign(evento.deltaY) * 0.12)));
    aplicarCamera();
  };

  // O botão do meio não pode virar autoscroll enquanto se desloca o modelo.
  const semAutoscroll = (evento) => evento.preventDefault();

  canvas.addEventListener('pointerdown', aoPressionar);
  canvas.addEventListener('pointermove', aoMover);
  canvas.addEventListener('pointerup', aoSoltar);
  canvas.addEventListener('pointerleave', aoSoltar);
  canvas.addEventListener('wheel', aoRolar, { passive: false });
  canvas.addEventListener('mousedown', semAutoscroll);
  canvas.addEventListener('contextmenu', (evento) => evento.preventDefault());

  const redimensionar = () => desenhar();
  window.addEventListener('resize', redimensionar);

  atual.limpar = () => {
    window.removeEventListener('resize', redimensionar);
    renderer.dispose();
    objeto.traverse?.((filho) => {
      filho.geometry?.dispose?.();
      const material = filho.material;
      if (Array.isArray(material)) material.forEach((m) => m?.dispose?.());
      else material?.dispose?.();
    });
  };

  desenhar();
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
