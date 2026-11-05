using System.Buffers.Binary;
using System.Text;
using CVExplorer.Host;
using CVExplorer.Host.Features.Media;
using CVExplorer.Host.Features.Thumbnails;

namespace CVExplorer.Tests;

public sealed class VideoPlanTests
{
    private static MediaInfo Info(
        int largura = 1920,
        int altura = 1080,
        double fps = 29.97,
        double duracao = 60,
        string pixFmt = "yuv420p",
        string codec = "h264",
        string? transferencia = "bt709")
        => new()
        {
            Caminho = @"C:\midia\clipe.mp4",
            Largura = largura,
            Altura = altura,
            Fps = fps,
            DuracaoSegundos = duracao,
            PixFmt = pixFmt,
            Codec = codec,
            Transferencia = transferencia,
            TemAudio = true,
        };

    [Theory]
    [InlineData(50, "prores422lt")]   // XDCAM HD422 50 Mbps, como o MXF de 1080p29.97
    [InlineData(20, "dnxhr_lb")]
    [InlineData(70, "prores422")]
    [InlineData(120, "prores422hq")]
    public void EscolhePerfilProporcionalAoBitrateDaOrigem(double mbps, string esperado)
    {
        var info = Info();
        info.BitrateVideoMbps = mbps;

        var plano = VideoPlanBuilder.Choose(info, "auto", espacoLivre: 0);

        Assert.Equal(esperado, plano.Perfil);
    }

    [Fact]
    public void OrigemDezBitsNuncaCaiParaPerfilDe8Bits()
    {
        var info = Info(pixFmt: "yuv422p10le");
        info.BitrateVideoMbps = 50;

        var plano = VideoPlanBuilder.Choose(info, "auto", espacoLivre: 0);

        Assert.Equal("prores422hq", plano.Perfil);
        Assert.True(info.DezBits);
    }

    [Fact]
    public void MxfXdcam50MbpsGeraArquivoMuitoMenorQueProResHq()
    {
        // Numeros reais de um MXF XDCAM HD422: 45 s, 1080p29.97, 50 Mbps, 8 bits 4:2:2.
        var info = Info(fps: 30000 / 1001.0, duracao: 45.045, pixFmt: "yuv422p", codec: "mpeg2video");
        info.BitrateVideoMbps = 50;

        var automatico = VideoPlanBuilder.Choose(info, "auto", espacoLivre: 0);
        var hq = VideoPlanBuilder.Choose(info, "prores422hq", espacoLivre: 0);

        Assert.Equal("prores422lt", automatico.Perfil);
        Assert.True(automatico.TamanhoEstimadoBytes < hq.TamanhoEstimadoBytes * 0.55,
            "o perfil automatico deve economizar mais de 45% frente ao ProRes 422 HQ");

        // E o aviso explica o motivo real do crescimento (long-GOP -> intra-frame).
        Assert.Contains(automatico.Avisos, a => a.Contains("long-GOP"));
    }

    [Fact]
    public void DnxHrLbFicaAbaixoDoTamanhoDoMxfOriginal()
    {
        var info = Info(fps: 30000 / 1001.0, duracao: 45.045, pixFmt: "yuv422p", codec: "mpeg2video");
        info.BitrateVideoMbps = 50;

        var lb = VideoPlanBuilder.Choose(info, "dnxhr_lb", espacoLivre: 0);

        // O MXF tem ~339 MB; DNxHR LB deve ficar na mesma faixa ou abaixo.
        Assert.InRange(lb.TamanhoEstimadoBytes, 150L * 1024 * 1024, 340L * 1024 * 1024);
    }

    [Fact]
    public void AlphaForcaProRes4444ComPixFmtDeAlpha()
    {
        var info = Info();
        info.TemAlpha = true;

        var plano = VideoPlanBuilder.Choose(info, "prores422hq", espacoLivre: 0);

        Assert.Equal("prores4444", plano.Perfil);
        Assert.Equal("yuva444p10le", plano.PixFmt);
        Assert.True(plano.ComAlpha);
    }

    [Fact]
    public void Origem4444VaiParaProRes4444SemAlpha()
    {
        var plano = VideoPlanBuilder.Choose(Info(pixFmt: "yuv444p10le"), "auto", 0);

        Assert.Equal("prores4444", plano.Perfil);
        Assert.Equal("yuv444p10le", plano.PixFmt);
        Assert.False(plano.ComAlpha);
    }

    [Fact]
    public void HdrGeraAviso()
    {
        var plano = VideoPlanBuilder.Choose(Info(transferencia: "smpte2084"), "auto", 0);

        Assert.Contains(plano.Avisos, a => a.Contains("HDR"));
    }

    [Fact]
    public void DolbyVisionGeraAviso()
    {
        var info = Info();
        info.DolbyVision = true;

        var plano = VideoPlanBuilder.Choose(info, "auto", 0);

        Assert.Contains(plano.Avisos, a => a.Contains("Dolby Vision"));
    }

    [Fact]
    public void DimensaoImparGeraAviso()
    {
        var plano = VideoPlanBuilder.Choose(Info(largura: 1921, altura: 1081), "auto", 0);

        Assert.Contains(plano.Avisos, a => a.Contains("impares") || a.Contains("ímpares"));
    }

    [Fact]
    public void SemEspacoCaiParaOPerfilMaisPequeno()
    {
        // 1 hora de UHD passa de 350 GB em qualquer perfil alto; 10 GB livres nao cabe.
        var info = Info(largura: 3840, altura: 2160, fps: 30, duracao: 3600);

        var plano = VideoPlanBuilder.Choose(info, "auto", espacoLivre: 10L * 1024 * 1024 * 1024);

        Assert.Equal("dnxhr_lb", plano.Perfil);
        Assert.Contains(plano.Avisos, a => a.Contains("insuficiente"));
    }

    [Fact]
    public void EstimativaSegueADataRateCanonica()
    {
        // 1 hora de 1080p29.97 em ProRes 422 HQ ~ 99 GB (220 Mbps).
        var info = Info(duracao: 3600);
        var bytes = VideoPlanBuilder.Estimate(220, info);
        var gb = bytes / 1024d / 1024d / 1024d;

        Assert.InRange(gb, 90, 100);
    }

    [Fact]
    public void FormatBytesUsaPadraoBrasileiro()
    {
        var texto = VideoPlanBuilder.FormatBytes(1024L * 1024 * 1024 * 3 / 2);

        Assert.Contains("GB", texto);
        Assert.Contains(",", texto);
    }
}

public sealed class FfmpegCommandTests
{
    [Fact]
    public void ProResHqUsaPerfil3VendorAppleEPcm24()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mp4",
            Largura = 1920,
            Altura = 1080,
            Fps = 25,
            PixFmt = "yuv420p",
            TemAudio = true,
            Primarias = "bt709",
            Transferencia = "bt709",
            EspacoCor = "bt709",
            FaixaCor = "tv",
            Timecode = "01:00:00:00",
        };

        var plano = VideoPlanBuilder.Choose(info, "prores422hq", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "entrada.mp4", "saida.mov");

        AssertContains(args, "-profile:v", "3");
        AssertContains(args, "-vendor", "apl0");
        AssertContains(args, "-pix_fmt", "yuv422p10le");
        AssertContains(args, "-c:a", "pcm_s24le");
        AssertContains(args, "-timecode", "01:00:00:00");
        AssertContains(args, "-color_primaries", "bt709");
        Assert.Contains("+faststart+write_colr", args);
        Assert.DoesNotContain("-vf", args);
        Assert.Equal("saida.mov", args[^1]);
    }

    [Fact]
    public void ProRes4444ExigePixelFormatDeAlphaExplicito()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.webm",
            Largura = 1080,
            Altura = 1080,
            Fps = 23.976,
            PixFmt = "yuva420p",
            TemAlpha = true,
        };

        var plano = VideoPlanBuilder.Choose(info, "auto", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.webm", "s.mov");

        AssertContains(args, "-profile:v", "4");
        AssertContains(args, "-pix_fmt", "yuva444p10le");
        AssertContains(args, "-alpha_bits", "16");
        Assert.Contains("-an", args);
    }

    [Fact]
    public void DnxHrHqxUsaEncoderDnxhdE10Bits()
    {
        var info = new MediaInfo { Caminho = @"C:\a.mp4", Largura = 1920, Altura = 1080, PixFmt = "yuv422p10le", TemAudio = true };
        var plano = VideoPlanBuilder.Choose(info, "dnxhr_hqx", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mp4", "s.mov");

        AssertContains(args, "-c:v", "dnxhd");
        AssertContains(args, "-profile:v", "dnxhr_hqx");
        AssertContains(args, "-pix_fmt", "yuv422p10le");
    }

    [Fact]
    public void DnxHrSqUsaPerfilSqE8Bits()
    {
        var info = new MediaInfo { Caminho = @"C:\a.mxf", Largura = 1920, Altura = 1080, PixFmt = "yuv422p", TemAudio = true };
        var plano = VideoPlanBuilder.Choose(info, "dnxhr_sq", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mxf", "s.mov");

        AssertContains(args, "-c:v", "dnxhd");
        AssertContains(args, "-profile:v", "dnxhr_sq");
        AssertContains(args, "-pix_fmt", "yuv422p");
    }

    [Fact]
    public void VfrPreservaQuadros()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mov",
            Largura = 1920,
            Altura = 1080,
            Fps = 29.93,
            PixFmt = "yuv420p",
            Vfr = true,
        };

        var plano = VideoPlanBuilder.Choose(info, "auto", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mov", "s.mov");

        AssertContains(args, "-fps_mode", "passthrough");
    }

    [Fact]
    public void DimensaoImparRecebeAjusteParaPar()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mov",
            Largura = 1921,
            Altura = 1081,
            PixFmt = "yuv420p",
            Fps = 24,
        };

        var plano = VideoPlanBuilder.Choose(info, "auto", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mov", "s.mov");

        AssertContains(args, "-vf", "scale=trunc(iw/2)*2:trunc(ih/2)*2");
    }

    [Fact]
    public void NuncaSobrescreveOriginalNemUsaSaidaIgualEntrada()
    {
        var info = new MediaInfo { Caminho = @"C:\pasta\clipe.mp4", Largura = 1920, Altura = 1080, PixFmt = "yuv420p" };
        var plano = VideoPlanBuilder.Choose(info, "auto", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, info.Caminho, @"C:\pasta\clipe.mov");

        Assert.Contains("-i", args);
        Assert.Equal(@"C:\pasta\clipe.mov", args[^1]);
        Assert.NotEqual(info.Caminho, args[^1]);
    }

    [Fact]
    public void EspacoDeCorRgbNaoEhPassadoAoEncoder()
    {
        // PNG/tela chegam como "gbr": o encoder recusa com "Undefined constant".
        var info = new MediaInfo
        {
            Caminho = @"C:\tela.png",
            Largura = 1920,
            Altura = 1080,
            Fps = 24,
            PixFmt = "rgb24",
            EspacoCor = "gbr",
        };

        var plano = VideoPlanBuilder.Choose(info, "dnxhr_sq_mov", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.png", "s.mov");

        Assert.DoesNotContain("-colorspace", args);
    }

    [Fact]
    public void SequenciaNaoCopiaTagsDeCorDoPrimeiroQuadro()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\seq_0001.png",
            Largura = 1920,
            Altura = 1080,
            Fps = 24,
            PixFmt = "rgb24",
            Primarias = "bt709",
            Transferencia = "bt709",
            EspacoCor = "gbr",
            FaixaCor = "pc",
        };

        var plano = VideoPlanBuilder.Choose(info, "prores422_mov", 0);
        var args = FfmpegCommandBuilder.Build(
            info,
            plano,
            @"C:\temp\lista.txt",
            @"C:\saida.mov",
            new FfmpegCommandBuilder.EntradaSequencia(24));

        Assert.DoesNotContain("-color_primaries", args);
        Assert.DoesNotContain("-colorspace", args);
        Assert.DoesNotContain("-color_range", args);
    }

    [Fact]
    public void OrigemEntrelacadaDesentrelacaNaConversao()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mxf",
            Largura = 1920,
            Altura = 1080,
            Fps = 25,
            PixFmt = "yuv422p",
            Codec = "mpeg2video",
            Interlaced = true,
            FieldOrder = "tt",
        };

        var plano = VideoPlanBuilder.Choose(info, "prores422_mov", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mxf", "s.mov");

        Assert.True(plano.Desentrelacar);
        var indice = args.IndexOf("-vf");
        Assert.True(indice >= 0, "esperava um filtro de vídeo");
        Assert.Contains("bwdif", args[indice + 1]);
    }

    [Fact]
    public void DesentrelacamentoCombinaComAjusteDeDimensaoImpar()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mxf",
            Largura = 1921,
            Altura = 1081,
            Fps = 25,
            PixFmt = "yuv422p",
            Interlaced = true,
        };

        var plano = VideoPlanBuilder.Choose(info, "auto", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mxf", "s.mov");

        var cadeia = args[args.IndexOf("-vf") + 1];
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2", cadeia);
        Assert.Contains("bwdif", cadeia);
        Assert.Equal(1, args.Count(a => a == "-vf"));
    }

    [Fact]
    public void ProgressivoNaoRecebeFiltroDeDesentrelacamento()
    {
        var info = new MediaInfo { Caminho = @"C:\a.mp4", Largura = 1920, Altura = 1080, Fps = 25, PixFmt = "yuv420p" };

        var plano = VideoPlanBuilder.Choose(info, "prores422_mov", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mp4", "s.mov");

        Assert.False(plano.Desentrelacar);
        Assert.DoesNotContain("-vf", args);
    }

    private static void AssertContains(List<string> args, string chave, string valor)
    {
        var indice = args.IndexOf(chave);
        Assert.True(indice >= 0, $"esperava o argumento {chave}");
        Assert.Equal(valor, args[indice + 1]);
    }

    [Theory]
    [InlineData("prores422_mov", ".mov", "prores_ks", "pcm_s24le")]
    [InlineData("prores4444_mov", ".mov", "prores_ks", "pcm_s24le")]
    [InlineData("dnxhr_sq_mov", ".mov", "dnxhd", "pcm_s24le")]
    [InlineData("dnxhr_hq_mov", ".mov", "dnxhd", "pcm_s24le")]
    [InlineData("dnxhr_sq_mxf", ".mxf", "dnxhd", "pcm_s24le")]
    [InlineData("dnxhr_hq_mxf", ".mxf", "dnxhd", "pcm_s24le")]
    [InlineData("xdcam", ".mxf", "mpeg2video", "pcm_s24le")]
    [InlineData("dxv3", ".mov", "dxv", "pcm_s16le")]
    [InlineData("dxv3_alpha", ".mov", "dxv", "pcm_s16le")]
    [InlineData("hap_alpha", ".mov", "hap", "pcm_s16le")]
    [InlineData("h264", ".mp4", "h264", "aac")]
    public void PresetsManuaisDefinemCodecContainerEAudio(string chave, string container, string encoder, string audio)
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mxf",
            Largura = 1920,
            Altura = 1080,
            Fps = 29.97,
            PixFmt = "yuv422p",
            TemAudio = true,
        };

        var plano = VideoPlanBuilder.Choose(info, chave, espacoLivre: 0);

        Assert.Equal(chave, plano.Perfil);
        Assert.Equal(container, plano.Container);
        Assert.Equal(encoder, plano.Encoder);

        var args = FfmpegCommandBuilder.Build(info, plano, "e.mxf", "s" + container);
        AssertContains(args, "-c:v", encoder);
        AssertContains(args, "-c:a", audio);

        if (container == ".mxf")
        {
            AssertContains(args, "-f", "mxf");
            Assert.DoesNotContain("-movflags", args);
        }
        else
        {
            Assert.Contains("-movflags", args);
            Assert.DoesNotContain("-f", args);
        }
    }

    [Fact]
    public void XdcamFixaBitrateCbrDeEmissora()
    {
        var info = new MediaInfo { Caminho = @"C:\a.mxf", Largura = 1920, Altura = 1080, Fps = 29.97, PixFmt = "yuv422p" };
        var plano = VideoPlanBuilder.Choose(info, "xdcam", 0);
        var args = FfmpegCommandBuilder.Build(info, plano, "e.mxf", "s.mxf");

        AssertContains(args, "-b:v", "50M");
        AssertContains(args, "-minrate", "50M");
        AssertContains(args, "-maxrate", "50M");
        AssertContains(args, "-pix_fmt", "yuv422p");
        Assert.Contains("-an", args);
    }

    [Fact]
    public void PresetManualNaoEhTrocadoPorFaltaDeEspaco()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mxf",
            Largura = 1920,
            Altura = 1080,
            Fps = 29.97,
            PixFmt = "yuv422p",
            DuracaoSegundos = 600,
        };

        var plano = VideoPlanBuilder.Choose(info, "dnxhr_hq_mxf", espacoLivre: 1024);

        Assert.Equal("dnxhr_hq_mxf", plano.Perfil);
        Assert.Equal(".mxf", plano.Container);
        Assert.DoesNotContain(plano.Avisos, a => a.Contains("Espaco insuficiente"));
    }

    [Fact]
    public void H264AvisaQueAlphaSeraDescartado()
    {
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mov",
            Largura = 1920,
            Altura = 1080,
            Fps = 25,
            PixFmt = "yuva444p10le",
            TemAlpha = true,
        };

        var plano = VideoPlanBuilder.Choose(info, "h264", espacoLivre: 0);

        Assert.Equal("h264", plano.Perfil);
        Assert.False(plano.ComAlpha);
        Assert.Contains(plano.Avisos, a => a.Contains("alpha"));
    }

    [Fact]
    public void MenuDePresetsCobreTodosOsFormatos()
    {
        var presets = VideoPlanBuilder.Presets();
        Assert.Equal(12, presets.Count);

        foreach (var esperado in new[]
                 {
                     "prores422_mov", "prores4444_mov", "dnxhr_sq_mov", "dnxhr_hq_mov",
                     "dnxhr_sq_mxf", "dnxhr_hq_mxf", "xdcam", "dxv3", "dxv3_alpha", "hap_alpha", "h264", "h264_50",
                 })
        {
            Assert.Contains(presets, p => p.Chave == esperado);
        }
    }

    [Fact]
    public void H264TemDuasOpcoesAutoE50Mbps()
    {
        var info = new MediaInfo { Caminho = @"C:\a.mov", Largura = 1920, Altura = 1080, Fps = 30, PixFmt = "yuv422p" };
        info.BitrateVideoMbps = 20;

        var automatico = VideoPlanBuilder.Choose(info, "h264", espacoLivre: 0);
        var fixo = VideoPlanBuilder.Choose(info, "h264_50", espacoLivre: 0);

        Assert.Equal("h264", automatico.Perfil);
        Assert.True(automatico.AlvoMbps < 30, $"automatico deveria seguir a origem, veio {automatico.AlvoMbps}");
        Assert.Equal("h264_50", fixo.Perfil);
        Assert.Equal(50, fixo.AlvoMbps);
        Assert.Equal(".mp4", fixo.Container);
    }

    [Fact]
    public void OrigemIntraFrameNaoEhChamadaDeLongGop()
    {
        // ProRes -> DXV3: a origem já é intra-frame, o aviso não pode culpar o long-GOP.
        var info = new MediaInfo
        {
            Caminho = @"C:\a.mov",
            Largura = 1920,
            Altura = 1080,
            Fps = 30,
            PixFmt = "yuva444p10le",
            Codec = "prores",
            TemAlpha = true,
        };
        info.BitrateVideoMbps = 9;

        var plano = VideoPlanBuilder.Choose(info, "dxv3", espacoLivre: 0);

        Assert.Contains(plano.Avisos, a => a.Contains("independente"));
        Assert.DoesNotContain(plano.Avisos, a => a.Contains("long-GOP"));
    }

    [Fact]
    public void DxvComAlphaPedeDxt5EHapComAlphaPedeHapAlpha()
    {
        var info = new MediaInfo { Caminho = @"C:\a.mov", Largura = 1920, Altura = 1080, Fps = 30, PixFmt = "yuva444p10le", TemAlpha = true };

        var dxv = VideoPlanBuilder.Choose(info, "dxv3_alpha", 0);
        Assert.True(dxv.ComAlpha);
        var argsDxv = FfmpegCommandBuilder.Build(info, dxv, "e.mov", "s.mov");
        AssertContains(argsDxv, "-format", "dxt5");
        AssertContains(argsDxv, "-pix_fmt", "rgba");

        var semAlpha = VideoPlanBuilder.Choose(info, "dxv3", 0);
        Assert.False(semAlpha.ComAlpha);
        AssertContains(FfmpegCommandBuilder.Build(info, semAlpha, "e.mov", "s.mov"), "-format", "dxt1");

        var hap = VideoPlanBuilder.Choose(info, "hap_alpha", 0);
        Assert.True(hap.ComAlpha);
        var argsHap = FfmpegCommandBuilder.Build(info, hap, "e.mov", "s.mov");
        AssertContains(argsHap, "-format", "hap_alpha");
        AssertContains(argsHap, "-pix_fmt", "rgba");
    }
}

public sealed class SequenciaDeImagensTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), $"cvexplorer-seq-{Guid.NewGuid():N}");

    public SequenciaDeImagensTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_pasta, recursive: true);
        }
        catch
        {
            // melhor esforco
        }
    }

    private string Criar(string nome, int bytes = 16)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, new byte[bytes]);
        return caminho;
    }

    [Fact]
    public void DetectaPrefixoSemAcentosComFuroNaNumerable()
    {
        Criar("shot_0001.png");
        Criar("shot_0002.png");
        Criar("shot_0003.png");
        var terceiro = Criar("shot_0010.png");

        var sequencia = ImageSequence.Detectar(terceiro);

        Assert.NotNull(sequencia);
        Assert.Equal("shot", sequencia!.NomeBase);
        Assert.Equal(".png", sequencia.Extensao);
        Assert.Equal(4, sequencia.Quadros);
        Assert.Equal(1, sequencia.Primeiro);
        Assert.Equal(10, sequencia.Ultimo);
        Assert.True(sequencia.ComFalhas);
        Assert.Equal(4, sequencia.Arquivos.Count);
    }

    [Fact]
    public void IgnoraArquivoSemNumeracao()
    {
        var solto = Criar("capa.png");
        Criar("shot_0001.png");
        Criar("shot_0002.png");
        Criar("shot_0003.png");

        Assert.Null(ImageSequence.Detectar(solto));
    }

    [Fact]
    public void MenosDeTresQuadrosNaoEhSequencia()
    {
        Criar("render_01.exr");
        var segundo = Criar("render_02.exr");

        Assert.Null(ImageSequence.Detectar(segundo));
    }

    [Fact]
    public void NaoMisturaExtensoesNemNumeracoesDeTamanhosDiferentes()
    {
        Criar("a_0001.png");
        Criar("a_0002.png");
        var quarto = Criar("a_0003.png");
        Criar("a_0004.jpg");
        Criar("a_00005.png");

        var sequencia = ImageSequence.Detectar(quarto);

        Assert.NotNull(sequencia);
        Assert.Equal(3, sequencia!.Quadros);
        Assert.All(sequencia.Arquivos, a => Assert.EndsWith(".png", a));
    }

    [Fact]
    public void ListaDoConcatUsaBarrasNormaisECobreOsQuadros()
    {
        Criar("f_0001.tga");
        Criar("f_0002.tga");
        var terceiro = Criar("f_0003.tga");

        var sequencia = ImageSequence.Detectar(terceiro)!;
        var lista = sequencia.MontarListaConcat(24);

        Assert.Equal(3, lista.Split("file '").Length - 1);
        Assert.Contains("duration 0.041667", lista);
        Assert.DoesNotContain(@"C:\\", lista);
    }

    [Fact]
    public void SequenciaViraClipeSemAudioComADuracaoDoFpsEscolhido()
    {
        var primeiro = new MediaInfo { Caminho = "x", Largura = 2048, Altura = 858, PixFmt = "rgba", TemAlpha = true };
        var sequencia = new ImageSequence
        {
            Pasta = _pasta,
            NomeBase = "shot",
            Extensao = ".png",
            Digitos = 4,
            Primeiro = 1,
            Ultimo = 240,
            Quadros = 240,
            Arquivos = ["a", "b"],
        };

        var info = sequencia.ComoVideo(primeiro, 24);

        Assert.Equal(10, info.DuracaoSegundos, 3);
        Assert.False(info.TemAudio);
        Assert.True(info.TemAlpha);
        Assert.Equal(24, info.Fps);
        Assert.Equal(2048, info.Largura);
    }

    [Fact]
    public void EntradaDeSequenciaUsaDemuxerConcatSemAudio()
    {
        var info = new MediaInfo { Caminho = "x", Largura = 1920, Altura = 1080, PixFmt = "yuv420p", Fps = 24 };
        var plano = VideoPlanBuilder.Choose(info, "prores422_mov", 0);

        var args = FfmpegCommandBuilder.Build(
            info,
            plano,
            @"C:\temp\lista.txt",
            @"C:\saida.mov",
            new FfmpegCommandBuilder.EntradaSequencia(24));

        var indice = args.IndexOf("-f");
        Assert.Equal(1, args.Count(a => a == "-f"));
        Assert.Equal("concat", args[indice + 1]);
        Assert.Equal("24", args[args.IndexOf("-r") + 1]);
        Assert.Contains("-an", args);
        Assert.Equal(@"C:\saida.mov", args[^1]);
    }
}

public sealed class CodecDeEdicaoTests
{
    [Theory]
    [InlineData("prores")]
    [InlineData("dnxhd")]
    [InlineData("cfhd")]
    public void CodecsDeEdicaoNaoSaoReencodados(string codec)
    {
        Assert.True(PasteService.EhCodecDeEdicao(codec));
    }

    [Theory]
    [InlineData("h264")]
    [InlineData("mpeg2video")]
    [InlineData("hevc")]
    [InlineData(null)]
    public void CodecsDeCameraPassamPorConversao(string? codec)
    {
        Assert.False(PasteService.EhCodecDeEdicao(codec));
    }

    [Fact]
    public void PerguntarAntesDeConverterVemLigadoPorPadrao()
    {
        Assert.True(new AppSettings().PerguntarAntesDeConverter);
    }
}

public sealed class BlendPreviewTests
{
    private static byte[] MontarBlend(int largura, int altura, byte preenchimento)
    {
        using var memoria = new MemoryStream();

        memoria.Write(Encoding.ASCII.GetBytes("BLENDER"));
        memoria.WriteByte((byte)'-'); // ponteiro de 8 bytes
        memoria.WriteByte((byte)'v'); // little endian
        memoria.Write(Encoding.ASCII.GetBytes("400")); // versao (3 bytes, fecha os 12 do cabecalho)

        var pixels = new byte[largura * altura * 4];
        Array.Fill(pixels, preenchimento);

        var tamanhoBloco = 8 + pixels.Length;
        memoria.Write(Encoding.ASCII.GetBytes("TEST"));
        memoria.Write(BitConverter.GetBytes(tamanhoBloco));
        memoria.Write(new byte[8]); // ponteiro antigo
        memoria.Write(BitConverter.GetBytes(0)); // indice SDNA
        memoria.Write(BitConverter.GetBytes(1)); // contagem

        memoria.Write(BitConverter.GetBytes(largura));
        memoria.Write(BitConverter.GetBytes(altura));
        memoria.Write(pixels);

        return memoria.ToArray();
    }

    [Fact]
    public void LeOPreviewEmbutidoDoBlend()
    {
        var dados = MontarBlend(32, 24, 200);

        using var stream = new MemoryStream(dados);
        var preview = BlendPreviewReader.Read(stream);

        Assert.NotNull(preview);
        Assert.Equal(32, preview!.Largura);
        Assert.Equal(24, preview.Altura);
        Assert.Equal(32 * 24 * 4, preview.Rgba.Length);
        Assert.All(preview.Rgba, b => Assert.Equal(200, b));
    }

    [Fact]
    public void ArquivoQueNaoEhBlendRetornaNulo()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("nao sou um blend"));

        Assert.Null(BlendPreviewReader.Read(stream));
    }

    [Fact]
    public void BlendSemBlocoTestRetornaNulo()
    {
        using var memoria = new MemoryStream();
        memoria.Write(Encoding.ASCII.GetBytes("BLENDER"));
        memoria.WriteByte((byte)'-');
        memoria.WriteByte((byte)'v');
        memoria.Write(Encoding.ASCII.GetBytes("400"));

        memoria.Write(Encoding.ASCII.GetBytes("DATA"));
        memoria.Write(BitConverter.GetBytes(4));
        memoria.Write(new byte[8]);
        memoria.Write(BitConverter.GetBytes(0));
        memoria.Write(BitConverter.GetBytes(1));
        memoria.Write(new byte[4]);

        memoria.Position = 0;
        Assert.Null(BlendPreviewReader.Read(memoria));
    }
}

public sealed class ReproducaoDiretaTests
{
    private static MediaInfo Video(string caminho, string codec, string pixFmt = "yuv420p", string? audio = null) =>
        new()
        {
            Caminho = caminho,
            Codec = codec,
            PixFmt = pixFmt,
            Formato = "mov,mp4,m4a,3gp,3g2,mj2",
            TemAudio = audio is not null,
            AudioCodec = audio ?? string.Empty,
        };

    [Theory]
    [InlineData("a.mp4", "h264", "yuv420p", "aac")]
    [InlineData("a.mov", "h264", "yuv420p", null)]
    [InlineData("a.MP4", "av1", "yuv420p10le", "opus")]
    [InlineData("a.webm", "vp9", "yuv420p", "vorbis")]
    public void TocaDiretoOQueOChromiumDecodifica(string nome, string codec, string pixFmt, string? audio)
    {
        Assert.True(ReproducaoDireta.Suportado(Video(nome, codec, pixFmt, audio)));
    }

    [Theory]
    [InlineData("a.mov", "prores", "yuv422p10le", "pcm_s16le")]   // codec de edicao
    [InlineData("a.mov", "dnxhd", "yuv422p", null)]
    [InlineData("a.mp4", "h264", "yuv420p10le", "aac")]          // h264 de 10 bits
    [InlineData("a.mp4", "h264", "yuv420p", "pcm_s16le")]        // audio que o Chromium nao abre
    [InlineData("a.mxf", "h264", "yuv420p", null)]               // container
    [InlineData("a.avi", "h264", "yuv420p", null)]
    public void MantemNoFluxoDoFfmpegOQueEleNaoDecodifica(string nome, string codec, string pixFmt, string? audio)
    {
        var info = Video(nome, codec, pixFmt, audio);
        if (nome.EndsWith(".mxf", StringComparison.OrdinalIgnoreCase))
        {
            info.Formato = "mxf";
        }

        Assert.False(ReproducaoDireta.Suportado(info));
    }
}
