using System.Buffers.Binary;
using System.Text;

namespace CVExplorer.Host.Features.Thumbnails;

public sealed class BlendPreview
{
    public int Largura { get; init; }

    public int Altura { get; init; }

    /// <summary>Pixels RGBA na ordem do arquivo .blend.</summary>
    public byte[] Rgba { get; init; } = Array.Empty<byte>();
}

/// <summary>
/// Lê a miniatura que o Blender grava dentro do próprio .blend (bloco "TEST": 128x128 RGBA,
/// gerada ao salvar). Instantâneo, sem abrir o Blender.
///
/// Não dá para confiar no layout do arquivo:
///  - o cabeçalho de 12 bytes do formato clássico ("BLENDER-v403") virou outro no Blender 5
///    ("BLENDER17-01v0502"), com tamanho diferente e versão em texto;
///  - arquivos salvos comprimidos (padrão de fábrica) são um fluxo zstd que começa no byte 0,
///    sem nenhum "BLENDER" legível no começo.
///
/// Por isso a leitura é por varredura: descomprime quando precisa e procura a assinatura do
/// bloco "TEST" na cabeça do arquivo, validando o candidato (o tamanho do bloco tem que bater
/// com largura × altura × 4 + 8). Isso vale para qualquer versão do Blender, sem depender de
/// detalhes do cabeçalho.
/// </summary>
public static class BlendPreviewReader
{
    /// <summary>Quanto do arquivo (já descomprimido) vale a pena varrer: o TEST é um dos primeiros blocos.</summary>
    private const int Janela = 4 * 1024 * 1024;

    private static readonly byte[] Assinatura = "TEST"u8.ToArray();
    private static readonly byte[] Cabecalho = "BLENDER"u8.ToArray();

    public static BlendPreview? Read(Stream stream)
    {
        try
        {
            var fluxo = Abrir(stream);
            if (fluxo is null)
            {
                return null;
            }

            var (fonte, proprio) = fluxo.Value;
            try
            {
                var cabeca = LerCabeca(fonte);
                return Procurar(cabeca);
            }
            finally
            {
                if (proprio)
                {
                    fonte.Dispose();
                }
            }
        }
        catch
        {
            // Arquivo truncado, zstd inválido, bloco corrompido: sem miniatura, cai no ícone.
            return null;
        }
    }

    /// <summary>
    /// Devolve o fluxo onde os blocos podem ser lidos: o próprio arquivo (sem compressão) ou o
    /// fluxo descomprimido. O segundo caso precisa ser descartado por quem chamou.
    /// </summary>
    private static (Stream Fonte, bool Proprio)? Abrir(Stream stream)
    {
        stream.Position = 0;
        var inicio = new byte[16];
        var lidos = LerExato(stream, inicio, inicio.Length);
        stream.Position = 0;

        if (lidos >= 7 && inicio.AsSpan(0, 7).SequenceEqual(Cabecalho))
        {
            // Cabeçalho legível: os blocos podem estar comprimidos logo depois dele,
            // marcados por um bloco "ZSTD" (ou pelo magic do próprio zstd/gzip).
            var deslocamento = lidos >= 12 ? 12 : lidos;
            stream.Position = deslocamento;
            var marca = new byte[4];
            var leuMarca = LerExato(stream, marca, 4) == 4;
            stream.Position = deslocamento;

            if (leuMarca && Comprimido(marca))
            {
                var descomprimido = Descomprimir(marca, stream);
                if (descomprimido is not null)
                {
                    return (descomprimido, true);
                }
            }

            stream.Position = 0;
            return (stream, false);
        }

        if (lidos >= 4 && Comprimido(inicio.AsSpan(0, 4).ToArray()))
        {
            var descomprimido = Descomprimir(inicio.AsSpan(0, 4).ToArray(), stream);
            if (descomprimido is not null)
            {
                return (descomprimido, true);
            }
        }

        return null;
    }

    private static bool Comprimido(byte[] marca) =>
        (marca[0] == 0x28 && marca[1] == 0xB5 && marca[2] == 0x2F && marca[3] == 0xFD) // zstd
        || (marca[0] == (byte)'Z' && marca[1] == (byte)'S' && marca[2] == (byte)'T' && marca[3] == (byte)'D')
        || (marca[0] == 0x1F && marca[1] == 0x8B); // gzip (Blender 2.8x e anteriores)

    private static Stream? Descomprimir(byte[] marca, Stream stream)
    {
        if (marca[0] == (byte)'Z' && marca[1] == (byte)'S' && marca[2] == (byte)'T' && marca[3] == (byte)'D')
        {
            // "ZSTD" é o marcador; o fluxo zstd começa depois dele.
            stream.Position += 4;
            return new ZstdSharp.DecompressionStream(stream);
        }

        if (marca[0] == 0x1F && marca[1] == 0x8B)
        {
            return new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        }

        return new ZstdSharp.DecompressionStream(stream);
    }

    private static byte[] LerCabeca(Stream stream)
    {
        var buffer = new byte[Janela];
        var total = 0;

        while (total < buffer.Length)
        {
            var n = stream.Read(buffer, total, buffer.Length - total);
            if (n <= 0)
            {
                break;
            }

            total += n;
        }

        return total == buffer.Length ? buffer : buffer.AsSpan(0, total).ToArray();
    }

    /// <summary>
    /// Procura o bloco "TEST" e valida o candidato de dois jeitos, porque o conteúdo mudou:
    ///  - formato clássico: largura/altura logo no início do conteúdo e o tamanho do bloco
    ///    fecha exatamente com 8 + largura × altura × 4 (é a validação forte);
    ///  - formato do Blender 5: 8 bytes de prologo antes de largura/altura, e o tamanho do
    ///    bloco não fecha mais — aí a garantia é a dimensão plausível somada a um código de
    ///    bloco válido logo depois dos pixels.
    /// </summary>
    private static BlendPreview? Procurar(byte[] dados)
    {
        var limite = dados.Length - 8;
        var candidatos = new List<int>();

        for (var i = 0; i + 4 <= limite; i++)
        {
            if (dados[i] == Assinatura[0] && dados[i + 1] == Assinatura[1]
                && dados[i + 2] == Assinatura[2] && dados[i + 3] == Assinatura[3])
            {
                candidatos.Add(i);
            }
        }

        // 1) Formato clássico: o tamanho declarado no bloco confirma o candidato.
        foreach (var i in candidatos)
        {
            foreach (var tamanhoPonteiro in new[] { 8, 4 })
            {
                var inicioConteudo = i + 4 + 4 + tamanhoPonteiro + 4 + 4;
                foreach (var littleEndian in new[] { true, false })
                {
                    var tamanho = LerInt32(dados, i + 4, littleEndian);
                    var achado = Extrair(dados, i, inicioConteudo, i + 8, littleEndian,
                        validar: (largura, altura) => tamanho == 8 + ((long)largura * altura * 4));

                    if (achado is not null)
                    {
                        return achado;
                    }
                }
            }
        }

        // 2) Formato novo (Blender 5): mesmo começo de conteúdo, 8 bytes a mais antes de w/h.
        foreach (var i in candidatos)
        {
            foreach (var tamanhoPonteiro in new[] { 8, 4 })
            {
                var inicioConteudo = i + 4 + 4 + tamanhoPonteiro + 4 + 4;
                foreach (var littleEndian in new[] { true, false })
                {
                    var achado = Extrair(dados, i, inicioConteudo + 8, i + 8, littleEndian, validar: null);
                    if (achado is not null)
                    {
                        return achado;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Lê largura/altura em <paramref name="inicioDimensoes"/> e devolve os pixels que vêm
    /// depois dos dois inteiros. Sem <paramref name="validar"/>, exige só dimensões plausíveis
    /// e um código de bloco válido na sequência.
    /// </summary>
    private static BlendPreview? Extrair(
        byte[] dados,
        int codigo,
        int inicioDimensoes,
        int inicioSemDados,
        bool littleEndian,
        Func<int, int, bool>? validar)
    {
        if (inicioDimensoes + 8 > dados.Length)
        {
            return null;
        }

        var largura = LerInt32(dados, inicioDimensoes, littleEndian);
        var altura = LerInt32(dados, inicioDimensoes + 4, littleEndian);

        if (largura < 8 || altura < 8 || largura > 512 || altura > 512)
        {
            return null;
        }

        var pixels = (long)largura * altura * 4;
        var inicioPixels = inicioDimensoes + 8;
        if (inicioPixels + pixels > dados.Length)
        {
            return null;
        }

        if (validar is not null)
        {
            if (!validar(largura, altura))
            {
                return null;
            }
        }
        else
        {
            // Sem confirmação de tamanho: o que vem depois dos pixels tem que ser um bloco.
            var depois = (int)(inicioPixels + pixels);
            if (!CodigoDeBloco(dados, depois))
            {
                return null;
            }
        }

        return new BlendPreview
        {
            Largura = largura,
            Altura = altura,
            Rgba = dados.AsSpan(inicioPixels, (int)pixels).ToArray(),
        };
    }

    /// <summary>Códigos de bloco do .blend são quatro caracteres maiúsculos/dígitos (REND, GLOB, TEST…).</summary>
    private static bool CodigoDeBloco(byte[] dados, int posicao)
    {
        if (posicao + 4 > dados.Length)
        {
            // Fim do arquivo logo depois da miniatura: aceitável.
            return posicao <= dados.Length;
        }

        for (var i = posicao; i < posicao + 4; i++)
        {
            var c = dados[i];
            var valido = (c >= (byte)'A' && c <= (byte)'Z') || (c >= (byte)'0' && c <= (byte)'9') || c == (byte)'_';
            if (!valido)
            {
                return false;
            }
        }

        return true;
    }

    private static int LerInt32(byte[] dados, int posicao, bool littleEndian)
    {
        var fatia = dados.AsSpan(posicao, 4);
        return littleEndian
            ? BinaryPrimitives.ReadInt32LittleEndian(fatia)
            : BinaryPrimitives.ReadInt32BigEndian(fatia);
    }

    private static int LerExato(Stream stream, byte[] buffer, int quantidade)
    {
        var lidos = 0;
        while (lidos < quantidade)
        {
            var n = stream.Read(buffer, lidos, quantidade - lidos);
            if (n <= 0)
            {
                break;
            }

            lidos += n;
        }

        return lidos;
    }

    /// <summary>Só para depuração: código do primeiro bloco depois do cabeçalho.</summary>
    internal static string? PrimeiroBloco(byte[] dados) =>
        dados.Length >= 4 ? Encoding.ASCII.GetString(dados, 0, 4) : null;
}
