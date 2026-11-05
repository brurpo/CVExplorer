namespace CVExplorer.Host.Features.Media;

/// <summary>
/// Decide se o WebView2 toca o arquivo original direto, sem passar pelo ffmpeg.
/// O Chromium do WebView2 decodifica H.264/AV1/VP8/VP9 com áudio AAC/MP3/Opus/Vorbis/FLAC;
/// ProRes, DNxHR, DXV, MXF, R3D e afins só existem no ffmpeg, e esses continuam no fluxo.
///
/// Tocar o original é o que dá seek instantâneo: o próprio Chromium pede os bytes por
/// Range HTTP e pula para qualquer ponto sem reabrir processo nenhum.
/// </summary>
public static class ReproducaoDireta
{
    private static readonly HashSet<string> CodecsVideo = new(StringComparer.OrdinalIgnoreCase)
    {
        "h264", "av1", "vp8", "vp9",
    };

    /// <summary>Formatos de pixel de 8 bits que o Chromium abre para h264 (High 10 não abre).</summary>
    private static readonly HashSet<string> PixFmt8Bits = new(StringComparer.OrdinalIgnoreCase)
    {
        "yuv420p", "yuvj420p", "nv12", "yuv422p", "yuv444p",
    };

    private static readonly HashSet<string> CodecsAudio = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac", "mp3", "opus", "vorbis", "flac",
    };

    private static readonly string[] Extensoes = { ".mp4", ".m4v", ".mov", ".webm" };

    public static bool Suportado(MediaInfo info)
    {
        var extensao = Path.GetExtension(info.Caminho);
        if (!Extensoes.Contains(extensao, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!CodecsVideo.Contains(info.Codec))
        {
            return false;
        }

        // h264 de 10 bits (High 10) não decodifica no Chromium; AV1/VP9 de 10 bits sim.
        if (info.Codec.Equals("h264", StringComparison.OrdinalIgnoreCase)
            && !PixFmt8Bits.Contains(info.PixFmt))
        {
            return false;
        }

        if (info.TemAudio && info.AudioCodec.Length > 0 && !CodecsAudio.Contains(info.AudioCodec))
        {
            return false;
        }

        // MXF, Matroska solto e afins podem ter o mesmo codec, mas com metadados que o
        // Chromium não abre: só vale para os containers que ele conhece.
        return info.Formato.Contains("mp4", StringComparison.OrdinalIgnoreCase)
               || info.Formato.Contains("webm", StringComparison.OrdinalIgnoreCase);
    }
}
