using System.Collections.Concurrent;
using System.Diagnostics;

namespace CVExplorer.Host.Features.Media;

/// <summary>
/// Descobre qual encoder H.264 este ffmpeg oferece. A ordem é por qualidade de
/// resultado e disponibilidade: libx264 (software, universal), depois os de GPU,
/// e por último os limitados (openh264/MF).
/// </summary>
public static class H264Picker
{
    private static readonly string[] Preferidos =
    [
        "libx264",
        "h264_nvenc",
        "h264_qsv",
        "h264_amf",
        "h264_mf",
        "libopenh264",
        "h264_vulkan",
    ];

    private static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? Escolher(string ffmpeg) => Cache.GetOrAdd(ffmpeg, Procurar);

    private static string? Procurar(string ffmpeg)
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
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");

            using var processo = Process.Start(psi);
            if (processo is null)
            {
                return null;
            }

            var texto = processo.StandardOutput.ReadToEnd();
            processo.StandardError.ReadToEnd();
            processo.WaitForExit(5000);

            return Preferidos.FirstOrDefault(p => texto.Contains($" {p} ", StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao listar encoders H.264", ex);
            return null;
        }
    }
}
