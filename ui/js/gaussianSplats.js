import * as THREE from '../vendor/three/build/three.module.min.js';

/**
 * Gaussian Splats (.ply com as propriedades f_dc_/opacity/scale_/rot_).
 *
 * three.js não traz renderizador de splats, então o parse e o desenho são daqui:
 * cada splat vira um ponto cuja largura em pixels vem da escala do PLY projetada pela
 * perspectiva (`escala * focal / profundidade`), com o mesmo perfil gaussiano do splat
 * original. A ordem é resolvida uma vez (de trás para frente), o que basta para olhar um
 * resultado — não é um treino nem um viewer de produção.
 */
const SH_C0 = 0.28209479177387814;

/**
 * Quantos desvios-padrão cabem no quad do ponto. O splat só "existe" até ~2,5σ; desenhar
 * 1σ (o que o tamanho bruto daria) deixa os pontos minúsculos e o modelo vira uma nuvem
 * rala.
 */
const EXTENSAO = 5;

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

  const buffer = await resposta.arrayBuffer();
  const dados = lerPly(buffer);

  const geometria = new THREE.BufferGeometry();
  geometria.setAttribute('position', new THREE.Float32BufferAttribute(dados.posicoes, 3));
  geometria.setAttribute('aCor', new THREE.Float32BufferAttribute(dados.cores, 3));
  geometria.setAttribute('aAlfa', new THREE.Float32BufferAttribute(dados.alphas, 1));
  geometria.setAttribute('aEscala', new THREE.Float32BufferAttribute(dados.escalas, 1));

  const material = new THREE.ShaderMaterial({
    uniforms: {
      uAlturaTela: { value: 900 },
    },
    vertexShader: `
      attribute vec3 aCor;
      attribute float aAlfa;
      attribute float aEscala;
      uniform float uAlturaTela;
      varying vec3 vCor;
      varying float vAlfa;

      void main() {
        vCor = aCor;
        vAlfa = aAlfa;

        vec4 visto = modelViewMatrix * vec4(position, 1.0);
        gl_Position = projectionMatrix * visto;

        // Focal em pixels vem da própria matriz de projeção: assim a mudança de FOV
        // (modelo x panorama) e o zoom não exigem recalcular nada na CPU.
        float focal = 0.5 * uAlturaTela * projectionMatrix[1][1];
        float sigma = aEscala * focal / max(-visto.z, 0.0001);

        // O quad precisa cobrir a gaussiana inteira (ver EXTENSAO), não só 1σ.
        gl_PointSize = clamp(${EXTENSAO.toFixed(1)} * sigma, 1.0, 2048.0);
      }
    `,
    fragmentShader: `
      varying vec3 vCor;
      varying float vAlfa;

      void main() {
        vec2 d = gl_PointCoord - vec2(0.5);
        float r2 = dot(d, d);
        if (r2 > 0.25) discard;

        // Perfil gaussiano: o deslocamento em sigmas é r * EXTENSAO, então
        // alfa = exp(-0.5 * (r * EXTENSAO)^2).
        float peso = exp(-${((EXTENSAO * EXTENSAO) / 2).toFixed(4)} * r2);
        if (peso < 0.004) discard;

        gl_FragColor = vec4(vCor, vAlfa * peso);
      }
    `,
    transparent: true,
    depthWrite: false,
    depthTest: true,
  });

  const pontos = new THREE.Points(geometria, material);
  // Já vem ordenado do centro para fora: o que está longe desenha primeiro.
  pontos.frustumCulled = false;

  const grupo = new THREE.Group();
  grupo.add(pontos);

  const caixa = new THREE.Box3().setFromBufferAttribute(geometria.getAttribute('position'));
  const centro = caixa.getCenter(new THREE.Vector3());
  const raio = Math.max(caixa.getBoundingSphere(new THREE.Sphere()).radius, 0.001);

  return {
    objeto: grupo,
    centro,
    raio,
    /** Altura do buffer de desenho em pixels; a focal em pixels sai daí, na GPU. */
    atualizarTela(alturaPx) {
      material.uniforms.uAlturaTela.value = alturaPx;
    },
    dispose() {
      geometria.dispose();
      material.dispose();
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
  const ordem = [];

  for (let i = 0; i < quantidade; i++) {
    const base = i * tamanhoRegistro;
    const x = valor('x', base);
    const y = valor('y', base);
    const z = valor('z', base);

    posicoes[i * 3] = x;
    posicoes[i * 3 + 1] = y;
    posicoes[i * 3 + 2] = z;

    cores[i * 3] = Math.min(1, Math.max(0, 0.5 + SH_C0 * valor('f_dc_0', base)));
    cores[i * 3 + 1] = Math.min(1, Math.max(0, 0.5 + SH_C0 * valor('f_dc_1', base)));
    cores[i * 3 + 2] = Math.min(1, Math.max(0, 0.5 + SH_C0 * valor('f_dc_2', base)));

    const opacidade = valor('opacity', base);
    alphas[i] = 1 / (1 + Math.exp(-opacidade));

    const sx = Math.exp(valor('scale_0', base));
    const sy = deslocamentos.scale_1 ? Math.exp(valor('scale_1', base)) : sx;
    const sz = deslocamentos.scale_2 ? Math.exp(valor('scale_2', base)) : sx;
    escalas[i] = Math.max(sx, sy, sz);

    ordem.push({ i, d: x * x + y * y + z * z });
  }

  // Sem ordenação por câmera: do mais distante do centro para o mais próximo.
  ordem.sort((a, b) => b.d - a.d);

  const posicoesOrdenadas = new Float32Array(quantidade * 3);
  const coresOrdenadas = new Float32Array(quantidade * 3);
  const alphasOrdenados = new Float32Array(quantidade);
  const escalasOrdenadas = new Float32Array(quantidade);

  ordem.forEach((item, destino) => {
    posicoesOrdenadas.set(posicoes.subarray(item.i * 3, item.i * 3 + 3), destino * 3);
    coresOrdenadas.set(cores.subarray(item.i * 3, item.i * 3 + 3), destino * 3);
    alphasOrdenados[destino] = alphas[item.i];
    escalasOrdenadas[destino] = escalas[item.i];
  });

  return {
    posicoes: posicoesOrdenadas,
    cores: coresOrdenadas,
    alphas: alphasOrdenados,
    escalas: escalasOrdenadas,
  };
}
