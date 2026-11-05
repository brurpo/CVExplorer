using System.Collections.Concurrent;
using System.Diagnostics;

namespace CVExplorer.Host.Features.Media;

/// <summary>
/// O que o ffmpeg do usuário sabe fazer. É consultado uma vez e memorizado:
/// o build empacotado é LGPL e não traz tudo (não tem libx264, e o DXV só sai em DXT1).
/// </summary>
public static class CodecCapabilities
{
    private static readonly string[] H264Preferidos =
    [
        "libx264",
        "h264_nvenc",
        "h264_qsv",
        "h264_amf",
        "h264_mf",
        "libopenh264",
        "h264_vulkan",
    ];

    private static readonly ConcurrentDictionary<string, string?> CacheH264 = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> CacheDxvAlpha = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> CacheDesentrelacador = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Melhor encoder H.264 disponível (null quando não há nenhum).</summary>
    public static string? H264(string ffmpeg) => CacheH264.GetOrAdd(ffmpeg, ffmpegCaminho =>
    {
        var lista = Executar(ffmpegCaminho, "-hide_banner", "-encoders");
        return H264Preferidos.FirstOrDefault(p => lista.Contains($" {p} ", StringComparison.Ordinal));
    });

    /// <summary>
    /// O encoder dxv do FFmpeg oficial só implementa DXT1 (sem alpha) — não é limitação
    /// do build. O patch em vendor\ffmpeg adiciona DXT5, e scripts\build-ffmpeg-dxv.ps1
    /// compila esse binário; aqui só perguntamos, então os dois casos funcionam sem
    /// mudar código.
    /// </summary>
    public static bool DxvAlpha(string ffmpeg) => CacheDxvAlpha.GetOrAdd(ffmpeg, ffmpegCaminho =>
    {
        var ajuda = Executar(ffmpegCaminho, "-hide_banner", "-h", "encoder=dxv");
        return ajuda.Contains("dxt5", StringComparison.OrdinalIgnoreCase);
    });

    /// <summary>
    /// Melhor desentrelaçador que este ffmpeg oferece. O NNEDI3 é o preferido (borda por
    /// rede neural) e vem no build de scripts\build-ffmpeg-dxv.ps1 (que liga --enable-gpl
    /// e deixa o nnedi3_weights.bin ao lado do executável). Sem ele, bwdif; e yadif por último.
    /// </summary>
    public static string Desentrelacador(string ffmpeg) => CacheDesentrelacador.GetOrAdd(ffmpeg, ffmpegCaminho =>
    {
        var filtros = Executar(ffmpegCaminho, "-hide_banner", "-filters");

        if (filtros.Contains(" nnedi ", StringComparison.Ordinal))
        {
            return "nnedi=deint=interlaced";
        }

        return filtros.Contains(" bwdif ", StringComparison.Ordinal)
            ? "bwdif=mode=send_frame:parity=auto:deint=interlaced"
            : "yadif=deint=interlaced:mode=send_frame";
    });

    private static string Executar(string ffmpeg, params string[] argumentos)
    {
        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var argumento in argumentos)
            {
                psi.ArgumentList.Add(argumento);
            }

            using var processo = Process.Start(psi);
            if (processo is null)
            {
                return string.Empty;
            }

            var saida = processo.StandardOutput.ReadToEnd();
            processo.StandardError.ReadToEnd();
            processo.WaitForExit(5000);

            return saida;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao consultar capacidades do ffmpeg ({string.Join(' ', argumentos)})", ex);
            return string.Empty;
        }
    }
}
