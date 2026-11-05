import * as THREE from '../vendor/three/build/three.module.min.js';

/**
 * Gaussian Splats (.ply com as propriedades f_dc_/opacity/scale_/rot_).
 *
 * three.js não traz renderizador de splats, então o parse e o desenho são daqui: cada
 * splat vira um quad cuja largura em pixels vem da escala do PLY projetada pela
 * perspectiva (`escala * focal / profundidade`), com o mesmo perfil gaussiano do splat
 * original.
 *
 * Alpha blending só fica certo na ordem certa, então a nuvem é **reordenada por
 * profundidade a cada mudança de câmera**: counting sort de 12 bits sobre a projeção no
 * eixo de visão, escrevendo só um atributo de índice (a posição/cor mora em texturas e
 * não é copiada). Sem isso os splats de trás aparecem por cima dos da frente.
 */
const SH_C0 = 0.28209479177387814;

/**
 * Quantos desvios-padrão cabem no quad do ponto. O splat só "existe" até ~2,5σ; desenhar
 * 1σ (o que o tamanho bruto daria) deixa os pontos minúsculos e o modelo vira uma nuvem
 * rala.
 */
const EXTENSAO = 5;

/** Splats por linha da textura de dados. */
const LARGURA_TEXTURA = 2048;

/** Baldes do counting sort: 4096 é o suficiente para não aparecer emenda de ordem. */
const BALDES = 4096;

/** Lê o cabeçalho e diz se o PLY é de splats (tem f_dc_0) em vez de malha. */
export function ehSplat(buffer) {
  const inicio = new TextDecoder().decode(new Uint8Array(buffer.slice(0, Math.min(buffer.byteLength, 4096))));
  return inicio.includes('f_dc_0') && inicio.includes('scale_0');
}

export async function carregar(url) {
  const resposta = await fetch(url);
  if (!resposta.ok) {
    throw new Error(`não consegui baixar o PLY (${resposta.status})`);
  }

  const dados = lerPly(await resposta.arrayBuffer());
  return criarNuvem(dados);
}

function criarNuvem(dados) {
  const n = dados.quantidade;
  const alturaTextura = Math.ceil(n / LARGURA_TEXTURA);
  const total = LARGURA_TEXTURA * alturaTextura;

  // Duas texturas RGBA32F: posição+escala e cor+alfa. A reordenação mexe só no índice,
  // então nada disso é copiado quando a câmera gira.
  const texPosicao = new Float32Array(total * 4);
  const texCor = new Float32Array(total * 4);

  for (let i = 0; i < n; i++) {
    texPosicao[i * 4] = dados.posicoes[i * 3];
    texPosicao[i * 4 + 1] = dados.posicoes[i * 3 + 1];
    texPosicao[i * 4 + 2] = dados.posicoes[i * 3 + 2];
    texPosicao[i * 4 + 3] = dados.escalas[i];

    texCor[i * 4] = dados.cores[i * 3];
    texCor[i * 4 + 1] = dados.cores[i * 3 + 1];
    texCor[i * 4 + 2] = dados.cores[i * 3 + 2];
    texCor[i * 4 + 3] = dados.alphas[i];
  }

  const texturaPosicao = new THREE.DataTexture(texPosicao, LARGURA_TEXTURA, alturaTextura, THREE.RGBAFormat, THREE.FloatType);
  const texturaCor = new THREE.DataTexture(texCor, LARGURA_TEXTURA, alturaTextura, THREE.RGBAFormat, THREE.FloatType);
  for (const textura of [texturaPosicao, texturaCor]) {
    textura.minFilter = THREE.NearestFilter;
    textura.magFilter = THREE.NearestFilter;
    textura.generateMipmaps = false;
    textura.needsUpdate = true;
  }

  const ordem = new Float32Array(n);
  for (let i = 0; i < n; i++) ordem[i] = i;

  const quad = new THREE.PlaneGeometry(2, 2);
  const geometria = new THREE.InstancedBufferGeometry();
  geometria.index = quad.index;
  geometria.setAttribute('position', quad.getAttribute('position'));
  const atributoOrdem = new THREE.InstancedBufferAttribute(ordem, 1);
  atributoOrdem.setUsage(THREE.DynamicDrawUsage);
  geometria.setAttribute('aIx', atributoOrdem);
  geometria.instanceCount = n;

  const material = new THREE.ShaderMaterial({
    glslVersion: THREE.GLSL3,
    uniforms: {
      uPosicao: { value: texturaPosicao },
      uCor: { value: texturaCor },
      uLarguraTextura: { value: LARGURA_TEXTURA },
      uTela: { value: new THREE.Vector2(900, 900) },
    },
    vertexShader: `
      in float aIx;
      uniform sampler2D uPosicao;
      uniform sampler2D uCor;
      uniform float uLarguraTextura;
      uniform vec2 uTela;
      out vec3 vCor;
      out float vAlfa;
      out vec2 vCanto;

      void main() {
        int i = int(aIx + 0.5);
        int largura = int(uLarguraTextura);
        ivec2 uv = ivec2(i % largura, i / largura);
        vec4 p = texelFetch(uPosicao, uv, 0);
        vec4 c = texelFetch(uCor, uv, 0);

        vCor = c.rgb;
        vAlfa = c.a;
        vCanto = position.xy;

        vec4 visto = modelViewMatrix * vec4(p.xyz, 1.0);
        vec4 clip = projectionMatrix * visto;

        // Focal em pixels vem da própria matriz de projeção: mudar FOV (modelo x
        // panorama) ou zoom não exige recalcular nada na CPU.
        float focal = 0.5 * uTela.y * projectionMatrix[1][1];
        float sigma = p.w * focal / max(-visto.z, 0.0001);
        float lado = clamp(${EXTENSAO.toFixed(1)} * sigma, 1.0, 4096.0);

        // Quad em pixels convertido para clip space (por isso o *clip.w).
        vec2 desloc = position.xy * vec2(lado / uTela.x, lado / uTela.y);
        gl_Position = clip;
        gl_Position.xy += desloc * clip.w;
      }
    `,
    fragmentShader: `
      in vec3 vCor;
      in float vAlfa;
      in vec2 vCanto;
      out vec4 fragColor;

      void main() {
        // vCanto vai de -1 a 1 na borda do quad, que cobre EXTENSAO/2 sigma por lado.
        float r2 = dot(vCanto, vCanto);
        if (r2 > 1.0) discard;

        // alfa = exp(-0.5 * (r * 2,5)^2)
        float peso = exp(-${((EXTENSAO * EXTENSAO) / 8).toFixed(4)} * r2);
        if (peso < 0.004) discard;

        fragColor = vec4(vCor, vAlfa * peso);
      }
    `,
    transparent: true,
    depthWrite: false,
    depthTest: true,
  });

  const malha = new THREE.Mesh(geometria, material);
  malha.frustumCulled = false;

  const grupo = new THREE.Group();
  grupo.add(malha);

  const caixa = new THREE.Box3().setFromBufferAttribute(new THREE.Float32BufferAttribute(dados.posicoes, 3));
  const centro = caixa.getCenter(new THREE.Vector3());
  const raio = Math.max(caixa.getBoundingSphere(new THREE.Sphere()).radius, 0.001);

  // ---- ordem por profundidade ---------------------------------------------

  const profundidades = new Float32Array(n);
  const contagem = new Uint32Array(BALDES + 1);
  const eixoVisao = new THREE.Vector3();
  const ultimaCamera = { x: NaN, y: NaN, z: NaN, dx: NaN, dy: NaN, dz: NaN };

  function aplicarOrdem() {
    const { x: cx, y: cy, z: cz } = ultimaCamera;
    const { x: dx, y: dy, z: dz } = ultimaCamera;
    const posicoes = dados.posicoes;

    let menor = Infinity;
    let maior = -Infinity;

    for (let i = 0; i < n; i++) {
      const d = (posicoes[i * 3] - cx) * dx + (posicoes[i * 3 + 1] - cy) * dy + (posicoes[i * 3 + 2] - cz) * dz;
      profundidades[i] = d;
      if (d < menor) menor = d;
      if (d > maior) maior = d;
    }

    contagem.fill(0);
    const escala = maior - menor > 1e-9 ? (BALDES - 1) / (maior - menor) : 0;
    for (let i = 0; i < n; i++) {
      // Balde 0 = mais longe: desenhar de trás para frente é o que o alpha blending pede.
      contagem[((maior - profundidades[i]) * escala) | 0]++;
    }

    let soma = 0;
    for (let b = 0; b < BALDES; b++) {
      const quantidade = contagem[b];
      contagem[b] = soma;
      soma += quantidade;
    }

    for (let i = 0; i < n; i++) {
      ordem[contagem[((maior - profundidades[i]) * escala) | 0]++] = i;
    }

    atributoOrdem.needsUpdate = true;
  }

  /**
   * Reordena quando a câmera mudou de verdade: o desenho é síncrono, então basta não
   * repetir a ordenação em eventos de mouse que não mudam a vista.
   */
  function atualizarCamera(camera) {
    if (!camera || n === 0) return;

    camera.getWorldDirection(eixoVisao);
    const p = camera.position;
    const mudou = Number.isNaN(ultimaCamera.x)
      || Math.abs(p.x - ultimaCamera.x) + Math.abs(p.y - ultimaCamera.y)
        + Math.abs(p.z - ultimaCamera.z) + Math.abs(eixoVisao.x - ultimaCamera.dx)
        + Math.abs(eixoVisao.y - ultimaCamera.dy) + Math.abs(eixoVisao.z - ultimaCamera.dz) > 1e-5;

    if (!mudou) return;

    ultimaCamera.x = p.x;
    ultimaCamera.y = p.y;
    ultimaCamera.z = p.z;
    ultimaCamera.dx = eixoVisao.x;
    ultimaCamera.dy = eixoVisao.y;
    ultimaCamera.dz = eixoVisao.z;

    aplicarOrdem();
  }

  return {
    objeto: grupo,
    centro,
    raio,
    /** Tamanho do buffer de desenho em pixels (a focal em pixels sai daí, na GPU). */
    atualizarTela(larguraPx, alturaPx) {
      material.uniforms.uTela.value.set(larguraPx, alturaPx);
    },
    atualizarCamera,
    dispose() {
      quad.dispose();
      geometria.dispose();
      material.dispose();
      texturaPosicao.dispose();
      texturaCor.dispose();
    },
  };
}


// ---- leitura do PLY ------------------------------------------------------

const TIPOS = {
  char: { tamanho: 1, ler: (v, o) => v.getInt8(o) },
  uchar: { tamanho: 1, ler: (v, o) => v.getUint8(o) },
  int8: { tamanho: 1, ler: (v, o) => v.getInt8(o) },
  uint8: { tamanho: 1, ler: (v, o) => v.getUint8(o) },
  short: { tamanho: 2, ler: (v, o) => v.getInt16(o, true) },
  ushort: { tamanho: 2, ler: (v, o) => v.getUint16(o, true) },
  int16: { tamanho: 2, ler: (v, o) => v.getInt16(o, true) },
  uint16: { tamanho: 2, ler: (v, o) => v.getUint16(o, true) },
  int: { tamanho: 4, ler: (v, o) => v.getInt32(o, true) },
  uint: { tamanho: 4, ler: (v, o) => v.getUint32(o, true) },
  int32: { tamanho: 4, ler: (v, o) => v.getInt32(o, true) },
  uint32: { tamanho: 4, ler: (v, o) => v.getUint32(o, true) },
  float: { tamanho: 4, ler: (v, o) => v.getFloat32(o, true) },
  float32: { tamanho: 4, ler: (v, o) => v.getFloat32(o, true) },
  double: { tamanho: 8, ler: (v, o) => v.getFloat64(o, true) },
  float64: { tamanho: 8, ler: (v, o) => v.getFloat64(o, true) },
};

function lerPly(buffer) {
  const bytes = new Uint8Array(buffer);
  const texto = new TextDecoder('latin1').decode(bytes);

  const fimCabecalho = texto.indexOf('end_header');
  if (fimCabecalho < 0) {
    throw new Error('PLY sem cabeçalho');
  }

  const inicioDados = texto.indexOf('\n', fimCabecalho) + 1;
  const linhas = texto.slice(0, fimCabecalho).split(/\r?\n/);

  let quantidade = 0;
  const propriedades = [];

  for (const linha of linhas) {
    const partes = linha.trim().split(/\s+/);
    if (partes[0] === 'format' && partes[1] !== 'binary_little_endian') {
      throw new Error('só PLY binário little-endian com splats');
    }
    if (partes[0] === 'element' && partes[1] === 'vertex') {
      quantidade = Number(partes[2]);
    }
    if (partes[0] === 'property' && partes[1] !== 'list') {
      propriedades.push({ tipo: partes[1], nome: partes[2] });
    }
  }

  if (quantidade <= 0 || propriedades.length === 0) {
    throw new Error('PLY de splats vazio');
  }

  const deslocamentos = {};
  let tamanhoRegistro = 0;
  for (const p of propriedades) {
    const tipo = TIPOS[p.tipo];
    deslocamentos[p.nome] = { offset: tamanhoRegistro, tipo };
    tamanhoRegistro += tipo?.tamanho ?? 0;
  }

  for (const obrigatorio of ['x', 'y', 'z', 'f_dc_0', 'opacity', 'scale_0']) {
    if (!deslocamentos[obrigatorio]) {
      throw new Error('PLY sem os campos de splat esperados');
    }
  }

  const visor = new DataView(buffer, inicioDados, tamanhoRegistro * quantidade);
  const valor = (nome, base) => {
    const campo = deslocamentos[nome];
    return campo ? campo.tipo.ler(visor, base + campo.offset) : 0;
  };

  const posicoes = new Float32Array(quantidade * 3);
  const cores = new Float32Array(quantidade * 3);
  const alphas = new Float32Array(quantidade);
  const escalas = new Float32Array(quantidade);

  for (let i = 0; i < quantidade; i++) {
    const base = i * tamanhoRegistro;

    posicoes[i * 3] = valor('x', base);
    posicoes[i * 3 + 1] = valor('y', base);
    posicoes[i * 3 + 2] = valor('z', base);

    cores[i * 3] = Math.min(1, Math.max(0, 0.5 + SH_C0 * valor('f_dc_0', base)));
    cores[i * 3 + 1] = Math.min(1, Math.max(0, 0.5 + SH_C0 * valor('f_dc_1', base)));
    cores[i * 3 + 2] = Math.min(1, Math.max(0, 0.5 + SH_C0 * valor('f_dc_2', base)));

    const opacidade = valor('opacity', base);
    alphas[i] = 1 / (1 + Math.exp(-opacidade));

    const sx = Math.exp(valor('scale_0', base));
    const sy = deslocamentos.scale_1 ? Math.exp(valor('scale_1', base)) : sx;
    const sz = deslocamentos.scale_2 ? Math.exp(valor('scale_2', base)) : sx;
    escalas[i] = Math.max(sx, sy, sz);
  }

  // Sem ordenação aqui: a ordem é por câmera, refeita a cada mudança de vista.
  return { quantidade, posicoes, cores, alphas, escalas };
}
