using System.Buffers.Binary;
using System.Text;
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

    [Fact]
    public void PadraoEhProRes422Hq()
    {
        var plano = VideoPlanBuilder.Choose(Info(), "auto", espacoLivre: 0);

        Assert.Equal("prores422hq", plano.Perfil);
        Assert.Equal("prores_ks", plano.Encoder);
        Assert.Equal("yuv422p10le", plano.PixFmt);
        Assert.Equal(3, plano.ProfileValue);
        Assert.False(plano.ComAlpha);
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
    public void SemEspacoCaiParaPerfilMenor()
    {
        // 1 hora de UHD em ProRes HQ passa de 350 GB; 10 GB livres nao cabe.
        var info = Info(largura: 3840, altura: 2160, fps: 30, duracao: 3600);

        var plano = VideoPlanBuilder.Choose(info, "auto", espacoLivre: 10L * 1024 * 1024 * 1024);

        Assert.Equal("proxy", plano.Perfil);
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

    private static void AssertContains(List<string> args, string chave, string valor)
    {
        var indice = args.IndexOf(chave);
        Assert.True(indice >= 0, $"esperava o argumento {chave}");
        Assert.Equal(valor, args[indice + 1]);
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
