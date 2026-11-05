import * as THREE from '../vendor/three/build/three.module.min.js';
import { GLTFLoader } from '../vendor/three/examples/jsm/loaders/GLTFLoader.js';
import { OBJLoader } from '../vendor/three/examples/jsm/loaders/OBJLoader.js';
import { STLLoader } from '../vendor/three/examples/jsm/loaders/STLLoader.js';
import { FBXLoader } from '../vendor/three/examples/jsm/loaders/FBXLoader.js';
import { USDZLoader } from '../vendor/three/examples/jsm/loaders/USDZLoader.js';
import { PLYLoader } from '../vendor/three/examples/jsm/loaders/PLYLoader.js';

/**
 * Desenho da miniatura 3D. Usado pelo worker (OffscreenCanvas) e, no caso do FBX
 * — que depende de `document` para texturas —, pela propria thread da interface.
 */

/**
 * DAE é carregado sob demanda: o ColladaLoader arrasta o TGALoader, que não é coisa de
 * worker. Import dinâmico mantém o grafo do worker limpo e só paga o custo no DAE.
 */
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
      const { ColladaLoader } = await import('../vendor/three/examples/jsm/loaders/ColladaLoader.js');
      return new ColladaLoader();
    }
    default:
      return null;
  }
}

function carregar(loader, url, ext) {
  return new Promise((resolve, reject) => {
    loader.load(
      url,
      (resultado) => {
        if (!resultado) {
          reject(new Error('arquivo sem geometria legível'));
          return;
        }

        if (ext === 'glb' || ext === 'gltf') {
          resolve(resultado.scene);
        } else if (ext === 'obj' || ext === 'fbx') {
          resolve(resultado);
        } else if (ext === 'stl' || ext === 'ply') {
          const geometria = resultado;
          geometria.computeVertexNormals?.();

          // PLY de gaussian splats é nuvem de pontos: desenha como tal, senão some.
          // O tamanho é em unidades de mundo já normalizadas (raio 1) e não sofre a
          // escala do grupo — por isso o valor fixo grande, ~2% do modelo.
          if (ext === 'ply' && !geometria.index) {
            resolve(new THREE.Points(
              geometria,
              new THREE.PointsMaterial({ size: 0.06, color: 0xcfd6de, sizeAttenuation: true }),
            ));
            return;
          }

          resolve(new THREE.Mesh(
            geometria,
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

function paraBlob(canvas) {
  if (typeof canvas.convertToBlob === 'function') {
    return canvas.convertToBlob({ type: 'image/webp', quality: 0.9 });
  }

  return new Promise((resolve, reject) => {
    canvas.toBlob(
      (blob) => (blob ? resolve(blob) : reject(new Error('falha ao gerar imagem'))),
      'image/webp',
      0.9,
    );
  });
}

/**
 * @param {string} url URL do arquivo do modelo.
 * @param {string} ext extensão sem ponto.
 * @param {number} tamanho lado em px.
 * @param {(lado:number) => any} criarCanvas OffscreenCanvas (worker) ou canvas do DOM.
 * @returns {Promise<Blob>}
 */
export async function desenhar(url, ext, tamanho, criarCanvas) {
  const loader = await criadorDeLoader(ext);
  if (!loader) {
    throw new Error(`sem loader para .${ext}`);
  }

  const objeto = await carregar(loader, url, ext);
  const lado = Math.max(96, Math.min(512, Math.round(tamanho)));

  const canvas = criarCanvas(lado);
  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: false });
  renderer.setPixelRatio(1);
  renderer.setSize(lado, lado, false);
  renderer.setClearColor(0x131417, 1);

  try {
    const cena = new THREE.Scene();
    cena.background = new THREE.Color(0x131417);
    cena.add(new THREE.AmbientLight(0xffffff, 1.4));

    const principal = new THREE.DirectionalLight(0xffffff, 2.2);
    principal.position.set(3, 5, 4);
    cena.add(principal);

    const preenchimento = new THREE.DirectionalLight(0x9fc4e8, 0.9);
    preenchimento.position.set(-4, -2, -3);
    cena.add(preenchimento);

    // Grupo externo: normaliza o modelo (centro na origem, raio 1) sem mexer nos
    // transforms internos do arquivo, que em FBX/GLB carregam escala e hierarquia.
    const grupo = new THREE.Group();
    grupo.add(objeto);
    cena.add(grupo);

    objeto.updateMatrixWorld(true);
    const caixa = new THREE.Box3().setFromObject(objeto, true);
    if (caixa.isEmpty()) {
      throw new Error('modelo vazio');
    }

    const centro = caixa.getCenter(new THREE.Vector3());
    const raio = caixa.getBoundingSphere(new THREE.Sphere()).radius || 1;

    objeto.position.sub(centro);
    grupo.scale.setScalar(1 / raio);
    grupo.updateMatrixWorld(true);

    const camera = new THREE.PerspectiveCamera(38, 1, 0.01, 100);
    camera.position.set(1.9, 1.35, 2.1);
    camera.lookAt(0, 0, 0);

    renderer.render(cena, camera);
    return await paraBlob(canvas);
  } finally {
    renderer.dispose();
    objeto.traverse?.((filho) => {
      filho.geometry?.dispose?.();
      const material = filho.material;
      if (Array.isArray(material)) {
        material.forEach((m) => m?.dispose?.());
      } else {
        material?.dispose?.();
      }
    });
  }
}
