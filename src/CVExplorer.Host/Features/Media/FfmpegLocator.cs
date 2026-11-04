namespace CVExplorer.Host.Features.Media;

/// <summary>
/// Localiza o ffmpeg: configuracao do usuario -> pasta "ffmpeg" ao lado do app -> PATH.
/// O binario nunca e embutido no processo (roda como processo filho, o que mantem
/// a licenca do app separada da licenca do ffmpeg).
/// </summary>
public sealed class FfmpegLocator
{
    private string? _cached;

    public string? Ffmpeg => Resolve("ffmpeg.exe", p => _cached = p, _cached);

    public string? Ffprobe => Resolve("ffprobe.exe", _ => { }, null);

    private string? Resolve(string exeName, Action<string> cache, string? cached)
    {
        if (cached is not null && File.Exists(cached))
        {
            return cached;
        }

        var configured = Settings.CaminhoFfmpeg;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var direct = Directory.Exists(configured)
                ? Path.Combine(configured, exeName)
                : configured;

            // Aceita tanto a pasta quanto o caminho do proprio ffmpeg.exe.
            if (direct.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
            {
                direct = Path.Combine(Path.GetDirectoryName(direct)!, exeName);
            }

            if (File.Exists(direct))
            {
                cache(direct);
                return direct;
            }
        }

        var bundled = Path.Combine(AppPaths.FfmpegDir, exeName);
        if (File.Exists(bundled))
        {
            cache(bundled);
            return bundled;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                 .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), exeName);
                if (File.Exists(candidate))
                {
                    cache(candidate);
                    return candidate;
                }
            }
            catch
            {
                // entradas invalidas de PATH sao comuns
            }
        }

        return null;
    }

    public AppSettings Settings { get; }

    public FfmpegLocator(AppSettings settings) => Settings = settings;
}
