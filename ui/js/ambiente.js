import * as THREE from '../vendor/three/build/three.module.min.js';

/**
 * Ambiente/iluminação da prévia 3D.
 *
 * Os presets são gerados na hora (nada é baixado, nada pesa no pacote): uma cena com
 * gradiente de céu e "softboxes" emissivos vira environment map pelo PMREM, que é o
 * mesmo tipo de luz que um HDRI dá — com a vantagem de ser instantâneo e offline.
 * Também dá para carregar um .hdr/.exr do usuário ou usar a própria imagem aberta como
 * ambiente lat-long (equiretangular).
 */
export const PRESETS = [
  { chave: 'nenhum', rotulo: 'Sem ambiente' },
  { chave: 'estudio', rotulo: 'Estúdio' },
  { chave: 'porDoSol', rotulo: 'Pôr do sol' },
  { chave: 'noite', rotulo: 'Noite' },
  { chave: 'neve', rotulo: 'Neve / nublado' },
];

const perfil = (chave) => {
  switch (chave) {
    case 'estudio':
      return { ceu: [0x1a1c20, 0x2a2d33], caixas: [[0xffffff, 8, [3, 4, 2], 3], [0xc8d6ff, 4, [-4, 2, -2], 2.5], [0xffffff, 3, [0, -3, 3], 2.5]] };
    case 'porDoSol':
      return { ceu: [0x2a1a12, 0x8a4a1e], caixas: [[0xffb27a, 9, [4, 1, 1], 4], [0x5a6bb0, 2, [-4, 3, -2], 3]] };
    case 'noite':
      return { ceu: [0x05070d, 0x121a2c], caixas: [[0x8098d0, 2, [3, 4, 1], 2], [0x405070, 1, [-3, 2, -3], 2]] };
    case 'neve':
      return { ceu: [0x9aa4ad, 0xd6dde4], caixas: [[0xffffff, 6, [2, 5, 2], 5], [0xe8f0ff, 5, [-3, 3, -2], 4]] };
    default:
      return null;
  }
};

/**
 * Gera o environment map de um preset. Devolve { textura, fundo } onde `fundo` é o mesmo
 * mapa servindo de céu, ou null quando não há ambiente.
 */
export function gerarPreset(renderizador, chave) {
  const definicao = perfil(chave);
  if (!definicao) {
    return { textura: null, fundo: null };
  }

  const cena = new THREE.Scene();
  cena.background = new THREE.Color(definicao.ceu[0]);

  // Esfera invertida com gradiente vertical: dá o "céu" do ambiente.
  const ceu = new THREE.Mesh(
    new THREE.SphereGeometry(50, 32, 16),
    new THREE.ShaderMaterial({
      side: THREE.BackSide,
      uniforms: {
        uTopo: { value: new THREE.Color(definicao.ceu[1]) },
        uBase: { value: new THREE.Color(definicao.ceu[0]) },
      },
      vertexShader: `
        varying float vAltura;
        void main() {
          vAltura = normalize(position).y;
          gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
        }
      `,
      fragmentShader: `
        uniform vec3 uTopo;
        uniform vec3 uBase;
        varying float vAltura;
        void main() {
          float t = clamp(vAltura * 0.5 + 0.5, 0.0, 1.0);
          gl_FragColor = vec4(mix(uBase, uTopo, pow(t, 1.4)), 1.0);
        }
      `,
    }),
  );
  cena.add(ceu);

  for (const [cor, intensidade, posicao, lado] of definicao.caixas) {
    const caixa = new THREE.Mesh(
      new THREE.PlaneGeometry(lado * 2, lado * 2),
      new THREE.MeshBasicMaterial({ color: new THREE.Color(cor).multiplyScalar(intensidade), side: THREE.DoubleSide }),
    );
    caixa.position.set(...posicao);
    caixa.lookAt(0, 0, 0);
    cena.add(caixa);
  }

  const gerador = new THREE.PMREMGenerator(renderizador);
  gerador.compileEquirectangularShader();
  const alvo = gerador.fromScene(cena, 0.04);
  gerador.dispose();

  ceu.geometry.dispose();
  ceu.material.dispose();
  for (const filho of cena.children) {
    filho.geometry?.dispose?.();
    filho.material?.dispose?.();
  }

  return { textura: alvo.texture, fundo: alvo.texture, alvo };
}

/** Carrega um HDRI do usuário (.hdr/.exr) e devolve a textura equiretangular. */
export async function carregarHdri(url, extensao) {
  if (extensao === 'exr') {
    const { EXRLoader } = await import('../vendor/three/examples/jsm/loaders/EXRLoader.js');
    const textura = await new EXRLoader().loadAsync(url);
    textura.mapping = THREE.EquirectangularReflectionMapping;
    return textura;
  }

  const { RGBELoader } = await import('../vendor/three/examples/jsm/loaders/RGBELoader.js');
  const textura = await new RGBELoader().loadAsync(url);
  textura.mapping = THREE.EquirectangularReflectionMapping;
  return textura;
}

/**
 * Usa uma imagem comum como ambiente lat-long. Para a luz, passa pelo PMREM (equiretangular);
 * para o fundo, a própria textura com mapeamento equiretangular — o three desenha o panorama
 * atrás de tudo e a câmera orbitando dá a sensação de estar dentro dele.
 */
export function ambienteDeImagem(renderizador, textura) {
  textura.mapping = THREE.EquirectangularReflectionMapping;

  const gerador = new THREE.PMREMGenerator(renderizador);
  const alvo = gerador.fromEquirectangular(textura);
  gerador.dispose();

  return { textura: alvo.texture, fundo: textura, alvo };
}
