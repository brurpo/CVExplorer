namespace CVExplorer.Host;

/// <summary>
/// Log simples em arquivo, com retencao curta. Sem dependencia externa de proposito:
/// o app precisa continuar funcionando mesmo quando algo falha cedo no boot.
/// </summary>
internal static class Log
{
    private static readonly object Gate = new();
    private static readonly string FilePath =
        Path.Combine(AppPaths.LogsDir, "app.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERRO", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogsDir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}"
                       + (ex is null
                           ? string.Empty
                           : $" -> {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}");

            lock (Gate)
            {
                File.AppendAllText(FilePath, line + Environment.NewLine);
                TrimIfNeeded();
            }
        }
        catch
        {
            // Logar nunca pode derrubar o app.
        }
    }

    private static void TrimIfNeeded()
    {
        var file = new FileInfo(FilePath);
        if (!file.Exists || file.Length < 2 * 1024 * 1024)
        {
            return;
        }

        var keep = File.ReadAllLines(FilePath).TakeLast(4000);
        File.WriteAllLines(FilePath, keep);
    }
}
