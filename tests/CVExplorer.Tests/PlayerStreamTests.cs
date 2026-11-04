using CVExplorer.Host.Features.Media;

namespace CVExplorer.Tests;

public sealed class PlayerStreamTests
{
    private static MediaInfo Video() => new()
    {
        Caminho = @"C:\clipes\a.mxf",
        Largura = 1920,
        Altura = 1080,
        Fps = 29.97,
        PixFmt = "yuv422p",
        TemAudio = true,
        DuracaoSegundos = 45,
    };

    [Fact]
    public void StreamUsaMp4FragmentadoParaTocarSemArquivoFinal()
    {
        var args = PreviewStreamService.Argumentos(Video(), "libx264", 0);

        Assert.Contains("pipe:1", args);
        Assert.Equal("mp4", args[args.IndexOf("-f") + 1]);

        var flags = args[args.IndexOf("-movflags") + 1];
        Assert.Contains("empty_moov", flags);
        Assert.Contains("frag_keyframe", flags);
    }

    [Fact]
    public void BuscaVemAntesDaEntradaParaSerInstantanea()
    {
        var semSalto = PreviewStreamService.Argumentos(Video(), "libx264", 0);
        Assert.DoesNotContain("-ss", semSalto);

        var comSalto = PreviewStreamService.Argumentos(Video(), "libx264", 12.5);
        var indiceSs = comSalto.IndexOf("-ss");
        var indiceEntrada = comSalto.IndexOf("-i");

        Assert.True(indiceSs >= 0, "esperava -ss para o scrub");
        Assert.True(indiceSs < indiceEntrada, "-ss precisa vir antes de -i");
        Assert.Equal("12.5", comSalto[indiceSs + 1]);
    }

    [Theory]
    [InlineData("libx264", "ultrafast")]
    [InlineData("h264_nvenc", "p4")]
    [InlineData("h264_mf", "speed")]
    [InlineData("h264_qsv", "veryfast")]
    public void CadaEncoderUsaSeuProprioPreset(string encoder, string preset)
    {
        var args = PreviewStreamService.Argumentos(Video(), encoder, 0);

        Assert.Equal(encoder, args[args.IndexOf("-c:v") + 1]);

        // Preset errado = encoder recusa abrir o fluxo (aconteceu com h264_nvenc).
        var indicePreset = args.IndexOf("-preset");
        if (indicePreset >= 0)
        {
            Assert.Equal(preset, args[indicePreset + 1]);
        }
    }

    [Fact]
    public void NuncaPassaDoTamanhoDoPainel()
    {
        var args = PreviewStreamService.Argumentos(Video(), "libx264", 0);
        var filtro = args[args.IndexOf("-vf") + 1];

        Assert.Contains("min(1280", filtro);
        Assert.Contains("yuv420p", args[args.IndexOf("-pix_fmt") + 1]);
    }

    [Fact]
    public void AudioOpcionalNaoQuebraArquivoSemSom()
    {
        var args = PreviewStreamService.Argumentos(Video(), "libx264", 0);

        // 0:a:0? = ignora quando o arquivo nao tem faixa de audio
        Assert.Contains("0:a:0?", args);
        Assert.Equal("aac", args[args.IndexOf("-c:a") + 1]);
    }
}
