using System.Diagnostics;
using System.Text.Json;
using CVExplorer.Host;
using CVExplorer.Host.Features.FileSystem;
using CVExplorer.Host.Features.Media;

namespace CVExplorer.Tests;

/// <summary>
/// Teste de integracao real (roda quando CVX_FFMPEG aponta para um ffmpeg.exe).
/// Verifica a regra central: colar video gera SOMENTE o .mov convertido no destino,
/// nunca uma copia do original.
/// </summary>
public sealed class ColarVideoIntegracaoTests
{
    private static string? Ffmpeg =>
        Environment.GetEnvironmentVariable("CVX_FFMPEG") is { Length: > 0 } caminho && File.Exists(caminho)
            ? caminho
            : null;

    [Fact]
    public async Task ColarVideoProduzSomenteProResNoDestino()
    {
        var ffmpeg = Ffmpeg;
        if (ffmpeg is null)
        {
            return; // ambiente sem ffmpeg: teste nao aplicavel
        }

        using var temp = new PastaTemporaria();
        var origem = Path.Combine(temp.Caminho, "origem");
        var destino = Path.Combine(temp.Caminho, "destino");
        Directory.CreateDirectory(origem);
        Directory.CreateDirectory(destino);

        var fonte = Path.Combine(origem, "captura_camera.mp4");
        GerarVideo(ffmpeg, fonte, 1920, 1080, 25, 2);

        var config = new AppSettings
        {
            CaminhoFfmpeg = Path.GetDirectoryName(ffmpeg),
            ConverterVideoAoColar = true,
            PerfilVideoPadrao = "auto",
        };

        var localizador = new FfmpegLocator(config);
        var probe = new MediaProbe(localizador);
        var transcode = new TranscodeService(localizador);
        var arquivos = new FileOperations(
            _ => Task.FromResult(ConflictDecision.ManterAmbos),
            _ => Task.CompletedTask);
        var paste = new PasteService(probe, transcode, arquivos, config);

        var eventos = new List<PasteEvent>();
        var resultado = await paste.PasteAsync(
            new[] { fonte },
            destino,
            "teste",
            e => { eventos.Add(e); return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Empty(resultado.Erros);

        var arquivosDestino = Directory.GetFiles(destino);
        Assert.Single(arquivosDestino);
        Assert.EndsWith(".mov", arquivosDestino[0], StringComparison.OrdinalIgnoreCase);

        // O original nao pode ter sido copiado para o projeto.
        Assert.DoesNotContain(arquivosDestino, f => f.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(arquivosDestino, f => f.Contains(".partial", StringComparison.OrdinalIgnoreCase));

        // O original continua intacto onde estava.
        Assert.True(File.Exists(fonte));

        // E o arquivo entregue e realmente um codec de edicao intra-frame.
        var info = await probe.InspectAsync(arquivosDestino[0], CancellationToken.None);
        Assert.NotNull(info);
        Assert.Contains(info!.Codec, new[] { "prores", "dnxhd" });
        Assert.Equal(1920, info.Largura);
        Assert.Equal(1080, info.Altura);
        Assert.Contains("422p", info.PixFmt);
        Assert.False(info.TemAlpha);

        Assert.Contains(eventos, e => e.Tipo == "convertido");
        Assert.All(eventos.Where(e => e.Tipo == "convertendo"), e => Assert.True(e.Percentual <= 100));
    }

    [Fact]
    public async Task VideoJaEmCodecDeEdicaoEhCopiadoSemReencodar()
    {
        var ffmpeg = Ffmpeg;
        if (ffmpeg is null)
        {
            return;
        }

        using var temp = new PastaTemporaria();
        var destino = Path.Combine(temp.Caminho, "destino");
        Directory.CreateDirectory(destino);

        var preres = Path.Combine(temp.Caminho, "ja_editavel.mov");
        GerarVideo(ffmpeg, preres, 1280, 720, 25, 1, codec: "prores_ks", perfil: "3");

        var config = new AppSettings { CaminhoFfmpeg = Path.GetDirectoryName(ffmpeg) };
        var localizador = new FfmpegLocator(config);
        var probe = new MediaProbe(localizador);
        var paste = new PasteService(
            probe,
            new TranscodeService(localizador),
            new FileOperations(_ => Task.FromResult(ConflictDecision.ManterAmbos), _ => Task.CompletedTask),
            config);

        var resultado = await paste.PasteAsync(
            new[] { preres }, destino, "teste2", _ => Task.CompletedTask, CancellationToken.None);

        Assert.Empty(resultado.Erros);
        Assert.Single(resultado.Copiados);
        Assert.Empty(resultado.Convertidos);
    }

    private static void GerarVideo(
        string ffmpeg,
        string destino,
        int largura,
        int altura,
        int fps,
        int segundos,
        string codec = "h264_mf",
        string? perfil = null)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-y",
                     "-f", "lavfi", "-i", $"testsrc2=size={largura}x{altura}:rate={fps}:duration={segundos}",
                     "-c:v", codec, "-pix_fmt", "yuv420p",
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        if (perfil is not null)
        {
            psi.ArgumentList.Add("-profile:v");
            psi.ArgumentList.Add(perfil);
            psi.ArgumentList.Add("-pix_fmt");
            psi.ArgumentList.Add("yuv422p10le");
        }

        psi.ArgumentList.Add(destino);

        using var processo = Process.Start(psi)!;
        var erro = processo.StandardError.ReadToEnd();
        processo.WaitForExit();

        Assert.True(processo.ExitCode == 0 && File.Exists(destino), $"falha ao gerar video: {erro}");
    }
}
