using System.Reflection;

namespace CVExplorer.Host;

internal static class AppInfo
{
    /// <summary>
    /// URL do repositorio, injetada pelo build (-p:RepositorioUrl=...).
    /// E a unica fonte de atualizacao: o app consulta as releases deste repositorio.
    /// </summary>
    public static string RepoUrl =>
        Meta("RepositorioUrl") ?? "https://github.com/SEU-USUARIO/CVExplorer";

    public const string PackId = "CVExplorer";

    public const string Nome = "CV Explorer";

    public static string Versao =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static bool RepositorioConfigurado =>
        !RepoUrl.Contains("SEU-USUARIO", StringComparison.OrdinalIgnoreCase);

    private static string? Meta(string chave) =>
        typeof(AppInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key.Equals(chave, StringComparison.Ordinal))
            ?.Value;
}
