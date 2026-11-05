using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace CVExplorer.Host.Features.Media;

public sealed class MediaInfo
{
    public string Caminho { get; set; } = string.Empty;

    public double DuracaoSegundos { get; set; }

    public int Largura { get; set; }

    public int Altura { get; set; }

    public double Fps { get; set; }

    public string PixFmt { get; set; } = string.Empty;

    public string Codec { get; set; } = string.Empty;

    public bool TemAlpha { get; set; }

    public bool TemAudio { get; set; }

    public int AudioCanais { get; set; } = 2;

    /// <summary>Quantidade de faixas de audio (ISO tracks de camera podem ser 8+).</summary>
    public int FaixasAudio { get; set; }

    /// <summary>Bitrate de video da origem em Mbps (0 quando o container nao informa).</summary>
    public double BitrateVideoMbps { get; set; }

    /// <summary>Bits por amostra informados pelo container (0 quando desconhecido).</summary>
    public int BitsPorAmostra { get; set; }

    /// <summary>Origem com 10 bits ou mais por amostra.</summary>
    public bool DezBits =>
        BitsPorAmostra >= 10
        || PixFmt.Contains("p10", StringComparison.OrdinalIgnoreCase)
        || PixFmt.Contains("p12", StringComparison.OrdinalIgnoreCase)
        || PixFmt.Contains("p16", StringComparison.OrdinalIgnoreCase)
        || PixFmt.Contains("p010", StringComparison.OrdinalIgnoreCase);

    public string? Primarias { get; set; }

    public string? Transferencia { get; set; }

    public string? EspacoCor { get; set; }

    public string? FaixaCor { get; set; }

    public string? Timecode { get; set; }

    /// <summary>Frames com taxas irregulares (celular, captura de tela).</summary>
    public bool Vfr { get; set; }

    /// <summary>
    /// Vídeo entrelaçado (field_order tt/bb/tb/bt). A conversão desentrelaça sozinha:
    /// quadro entrelaçado em ProRes vira "serrilhado" no AE a cada movimento.
    /// </summary>
    public bool Interlaced { get; set; }

    /// <summary>Ordem dos campos, quando entrelaçado (tt, bb, tb, bt).</summary>
    public string? FieldOrder { get; set; }

    public bool DolbyVision { get; set; }

    public bool Hdr => Transferencia is "smpte2084" or "arib-std-b67";

    /// <summary>4:4:4 ou RGB na origem: vale preservar croma em ProRes 4444.</summary>
    public bool AltaCromaticidade =>
        PixFmt.Contains("444", StringComparison.OrdinalIgnoreCase)
        || PixFmt.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
        || PixFmt.StartsWith("gbr", StringComparison.OrdinalIgnoreCase);
}

public sealed class MediaProbe
{
    private readonly FfmpegLocator _locator;

    public MediaProbe(FfmpegLocator locator) => _locator = locator;

    public async Task<MediaInfo?> InspectAsync(string caminho, CancellationToken ct)
    {
        var ffprobe = _locator.Ffprobe;
        if (ffprobe is null)
        {
            Log.Error("ffprobe nao encontrado: conversao de video indisponivel.");
            return null;
        }

        var psi = new ProcessStartInfo(ffprobe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in new[]
                 {
                     "-v", "error", "-print_format", "json",
                     "-show_streams", "-show_format", caminho,
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process is null)
        {
            return null;
        }

        var json = await process.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return Parse(json, caminho);
    }

    public static MediaInfo? Parse(string json, string caminho)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement? video = null;
            var temAudio = false;
            var canais = 2;
            var faixasAudio = 0;

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var type = stream.TryGetProperty("codec_type", out var t) ? t.GetString() : null;
                    if (type == "video" && video is null)
                    {
                        video = stream;
                    }
                    else if (type == "audio")
                    {
                        temAudio = true;
                        faixasAudio++;
                        if (stream.TryGetProperty("channels", out var ch) && ch.TryGetInt32(out var v))
                        {
                            canais = Math.Max(canais, v);
                        }
                    }
                }
            }

            if (video is null)
            {
                return null;
            }

            var info = new MediaInfo
            {
                Caminho = caminho,
                TemAudio = temAudio,
                AudioCanais = canais,
                FaixasAudio = faixasAudio,
                Largura = GetInt(video.Value, "width"),
                Altura = GetInt(video.Value, "height"),
                PixFmt = GetString(video.Value, "pix_fmt") ?? string.Empty,
                Codec = GetString(video.Value, "codec_name") ?? string.Empty,
                Primarias = GetString(video.Value, "color_primaries"),
                Transferencia = GetString(video.Value, "color_transfer"),
                EspacoCor = GetString(video.Value, "color_space"),
                FaixaCor = GetString(video.Value, "color_range"),
                Timecode = GetTag(video.Value, "timecode"),
            };

            info.BitrateVideoMbps = ParseMbps(GetString(video.Value, "bit_rate"));
            info.BitsPorAmostra = GetInt(video.Value, "bits_per_raw_sample");

            info.FieldOrder = GetString(video.Value, "field_order");
            info.Interlaced = info.FieldOrder is "tt" or "bb" or "tb" or "bt";

            info.Fps = ParseRate(GetString(video.Value, "avg_frame_rate"));
            var rFps = ParseRate(GetString(video.Value, "r_frame_rate"));
            info.Vfr = rFps > 0 && info.Fps > 0 && Math.Abs(rFps - info.Fps) > 0.01;

            info.TemAlpha = info.PixFmt.Contains('a')
                            || (GetString(video.Value, "codec_tag_string") ?? string.Empty).StartsWith("ap4h", StringComparison.OrdinalIgnoreCase);

            var tag = GetString(video.Value, "codec_tag_string") ?? string.Empty;
            info.DolbyVision = tag.StartsWith("dvh", StringComparison.OrdinalIgnoreCase);

            if (root.TryGetProperty("format", out var format))
            {
                if (double.TryParse(GetString(format, "duration"), NumberStyles.Any, CultureInfo.InvariantCulture, out var dur))
                {
                    info.DuracaoSegundos = dur;
                }

                info.Timecode ??= GetTag(format, "timecode");
            }

            if (info.DuracaoSegundos <= 0 && double.TryParse(
                    GetString(video.Value, "duration"), NumberStyles.Any, CultureInfo.InvariantCulture, out var vdur))
            {
                info.DuracaoSegundos = vdur;
            }

            return info;
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao interpretar ffprobe", ex);
            return null;
        }
    }

    private static string? GetString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? GetTag(JsonElement el, string name) =>
        el.TryGetProperty("tags", out var tags) && tags.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int GetInt(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v))
        {
            return 0;
        }

        // O ffprobe mistura numeros e strings no mesmo JSON (ex.: "bits_per_raw_sample": "8").
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var numero))
        {
            return numero;
        }

        if (v.ValueKind == JsonValueKind.String
            && int.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var texto))
        {
            return texto;
        }

        return 0;
    }

    private static double ParseMbps(string? bitRate)
    {
        if (string.IsNullOrWhiteSpace(bitRate)
            || !long.TryParse(bitRate, NumberStyles.Any, CultureInfo.InvariantCulture, out var bps)
            || bps <= 0)
        {
            return 0;
        }

        return bps / 1_000_000.0;
    }

    private static double ParseRate(string? rate)
    {
        if (string.IsNullOrWhiteSpace(rate))
        {
            return 0;
        }

        var parts = rate.Split('/');
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var num)
            && double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var den)
            && den != 0)
        {
            return num / den;
        }

        return double.TryParse(rate, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
