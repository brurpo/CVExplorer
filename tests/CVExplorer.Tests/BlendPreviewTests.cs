using System.Buffers.Binary;
using System.Text;
using CVExplorer.Host.Features.Thumbnails;

namespace CVExplorer.Tests;

/// <summary>
/// O .blend guarda a miniatura num bloco "TEST" logo depois do cabeÃ§alho. Quando o arquivo
/// Ã© salvo comprimido (padrÃ£o do Blender 4.x, zstd; gzip nas versÃµes antigas), os blocos vÃªm
/// dentro do fluxo comprimido â€” era isso que fazia a miniatura nunca aparecer.
/// </summary>
public sealed class BlendComprimidoTests
{
    private static byte[] PreviewDeTeste(int largura, int altura, byte r, byte g, byte b)
    {
        var dados = new byte[8 + (largura * altura * 4)];
        BinaryPrimitives.WriteInt32LittleEndian(dados.AsSpan(0, 4), largura);
        BinaryPrimitives.WriteInt32LittleEndian(dados.AsSpan(4, 4), altura);

        for (var i = 0; i < largura * altura; i++)
        {
            var baseIndice = 8 + (i * 4);
            dados[baseIndice] = r;
            dados[baseIndice + 1] = g;
            dados[baseIndice + 2] = b;
            dados[baseIndice + 3] = 255;
        }

        return dados;
    }

    /// <summary>Bloco no formato do arquivo: cÃ³digo, tamanho, ponteiro antigo, Ã­ndice SDNA e contagem.</summary>
    private static byte[] Bloco(string codigo, byte[] dados, int tamanhoPonteiro = 8)
    {
        var cabecalho = new byte[4 + 4 + tamanhoPonteiro + 4 + 4];
        Encoding.ASCII.GetBytes(codigo).CopyTo(cabecalho, 0);
        BinaryPrimitives.WriteInt32LittleEndian(cabecalho.AsSpan(4, 4), dados.Length);
        BinaryPrimitives.WriteInt32LittleEndian(cabecalho.AsSpan(4 + 4 + tamanhoPonteiro, 4), 1);

        var tudo = new byte[cabecalho.Length + dados.Length];
        cabecalho.CopyTo(tudo, 0);
        dados.CopyTo(tudo, cabecalho.Length);
        return tudo;
    }

    private static byte[] CabecalhoBlender() => "BLENDER-v403"u8.ToArray();

    /// <summary>CabeÃ§alho do Blender 5: 17 bytes, versÃ£o em texto e sem o "-v" clÃ¡ssico.</summary>
    private static byte[] CabecalhoBlender5() => "BLENDER17-01v0502"u8.ToArray();

    private static byte[] BlendSimples(byte[] preview) =>
        [.. CabecalhoBlender(), .. Bloco("TEST", preview), .. Bloco("DATA", new byte[16]), .. Bloco("ENDB", [])];

    /// <summary>
    /// No Blender 5 o conteÃºdo do TEST ganhou 8 bytes na frente: tipo e quadro antes de
    /// largura/altura. O bloco tambÃ©m nÃ£o declara mais o tamanho que a gente usava para
    /// validar, entÃ£o o candidato Ã© reconhecido pelas dimensÃµes e pelo bloco seguinte.
    /// </summary>
    private static byte[] ConteudoBlender5(int largura, int altura, byte r, byte g, byte b)
    {
        var pixels = PreviewDeTeste(largura, altura, r, g, b).AsSpan(8).ToArray();
        var dados = new byte[16 + pixels.Length];
        BinaryPrimitives.WriteInt32LittleEndian(dados.AsSpan(0, 4), 1);   // tipo
        BinaryPrimitives.WriteInt32LittleEndian(dados.AsSpan(4, 4), 0);   // quadro
        BinaryPrimitives.WriteInt32LittleEndian(dados.AsSpan(8, 4), largura);
        BinaryPrimitives.WriteInt32LittleEndian(dados.AsSpan(12, 4), altura);
        pixels.CopyTo(dados, 16);
        return dados;
    }

    [Fact]
    public void LeMiniaturaNoFormatoDoBlender5()
    {
        // Bloco com tamanho declarado zerado, como o Blender 5 grava.
        var conteudo = ConteudoBlender5(16, 8, 90, 140, 200);
        byte[] bloco = [.. "TEST"u8.ToArray(), .. new byte[20], .. conteudo, .. "DATA"u8.ToArray(), .. new byte[40]];
        byte[] arquivo = [.. CabecalhoBlender5(), .. bloco];
        using var fluxo = new MemoryStream(arquivo);

        var lida = BlendPreviewReader.Read(fluxo);

        Assert.NotNull(lida);
        Assert.Equal(16, lida!.Largura);
        Assert.Equal(8, lida.Altura);
        Assert.Equal(90, lida.Rgba[0]);
        Assert.Equal(140, lida.Rgba[1]);
        Assert.Equal(200, lida.Rgba[2]);
    }

    [Fact]
    public void LeMiniaturaDoBlender5ComArquivoInteiroComprimido()
    {
        var conteudo = ConteudoBlender5(16, 8, 30, 60, 90);
        byte[] bloco = [.. "TEST"u8.ToArray(), .. new byte[20], .. conteudo, .. "DATA"u8.ToArray(), .. new byte[40]];
        byte[] original = [.. CabecalhoBlender5(), .. bloco];

        using var comprimido = new MemoryStream();
        using (var zstd = new ZstdSharp.CompressionStream(comprimido, leaveOpen: true))
        {
            zstd.Write(original);
        }

        using var fluxo = new MemoryStream(comprimido.ToArray());

        var lida = BlendPreviewReader.Read(fluxo);

        Assert.NotNull(lida);
        Assert.Equal(16, lida!.Largura);
        Assert.Equal(30, lida.Rgba[0]);
    }

    [Fact]
    public void LeMiniaturaDeArquivoNaoComprimido()
    {
        var preview = PreviewDeTeste(16, 8, 10, 20, 30);
        using var fluxo = new MemoryStream(BlendSimples(preview));

        var lida = BlendPreviewReader.Read(fluxo);

        Assert.NotNull(lida);
        Assert.Equal(16, lida!.Largura);
        Assert.Equal(8, lida.Altura);
        Assert.Equal(10, lida.Rgba[0]);
        Assert.Equal(20, lida.Rgba[1]);
        Assert.Equal(30, lida.Rgba[2]);
    }

    [Fact]
    public void LeMiniaturaDeArquivoComprimidoComZstd()
    {
        var preview = PreviewDeTeste(16, 8, 200, 120, 40);

        // Blender escreve o cabeÃ§alho legÃ­vel e o resto num fluxo zstd marcado por "ZSTD".
        var blocos = BlendSimples(preview).AsSpan(CabecalhoBlender().Length).ToArray();
        using var comprimido = new MemoryStream();
        using (var zstd = new ZstdSharp.CompressionStream(comprimido, leaveOpen: true))
        {
            zstd.Write(blocos);
        }

        byte[] arquivo = [.. CabecalhoBlender(), .. "ZSTD"u8.ToArray(), .. comprimido.ToArray()];
        using var fluxo = new MemoryStream(arquivo);

        var lida = BlendPreviewReader.Read(fluxo);

        Assert.NotNull(lida);
        Assert.Equal(16, lida!.Largura);
        Assert.Equal(200, lida.Rgba[0]);
        Assert.Equal(120, lida.Rgba[1]);
        Assert.Equal(40, lida.Rgba[2]);
    }

    [Fact]
    public void SemBlocoTestNaoHaMiniatura()
    {
        byte[] arquivo = [.. CabecalhoBlender(), .. Bloco("DATA", new byte[16]), .. Bloco("ENDB", [])];
        using var fluxo = new MemoryStream(arquivo);

        Assert.Null(BlendPreviewReader.Read(fluxo));
    }

    [Fact]
    public void ArquivoQueNaoEBlendEhIgnorado()
    {
        using var fluxo = new MemoryStream("nao sou blend"u8.ToArray());
        Assert.Null(BlendPreviewReader.Read(fluxo));
    }
}
