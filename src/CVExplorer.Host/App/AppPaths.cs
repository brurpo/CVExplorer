namespace CVExplorer.Host;

/// <summary>
/// Todos os caminhos que o app grava ficam em %LOCALAPPDATA%\CVExplorer.
/// Nada e gravado ao lado do executavel (que pode estar em Program Files).
/// </summary>
internal static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CVExplorer");

    public static string SettingsFile => Path.Combine(Root, "config.json");

    public static string TemplateFile => Path.Combine(Root, "modelo-projeto.json");

    public static string ThumbsDir => Path.Combine(Root, "miniaturas");

    public static string LogsDir => Path.Combine(Root, "logs");

    public static string UiDir { get; } = Path.Combine(AppContext.BaseDirectory, "ui");

    public static string FfmpegDir => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    /// <summary>Host virtual usado pelo WebView2 para servir a interface (ES modules exigem http).</summary>
    public const string UiVirtualHost = "app.cvexplorer";

    /// <summary>Host virtual que serve as miniaturas ja geradas direto do disco.</summary>
    public const string ThumbsVirtualHost = "miniaturas.cvexplorer";

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ThumbsDir);
        Directory.CreateDirectory(LogsDir);
    }
}
