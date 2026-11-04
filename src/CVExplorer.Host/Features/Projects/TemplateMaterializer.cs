namespace CVExplorer.Host.Features.Projects;

public sealed class MaterializeResult
{
    public List<string> Criadas { get; } = new();

    public List<string> JaExistiam { get; } = new();

    public List<string> Ignoradas { get; } = new();

    public List<string> Erros { get; } = new();
}

/// <summary>
/// Cria somente as pastas escolhidas pelo usuario, nunca duplica e nunca sobrescreve.
/// A operacao e idempotente: rodar duas vezes com a mesma selecao nao cria nada novo.
/// </summary>
public static class TemplateMaterializer
{
    public static MaterializeResult Materialize(
        string projectRoot,
        IEnumerable<string> selectedPaths,
        IReadOnlyList<string> quadros)
    {
        var result = new MaterializeResult();
        var root = Path.GetFullPath(projectRoot);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = (quadros ?? Array.Empty<string>())
            .Select(SanitizeSegment)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var raw in selectedPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var expanded in Expand(raw, names, result))
            {
                var segments = expanded.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0 || !IsSafe(segments))
                {
                    result.Ignoradas.Add(raw);
                    continue;
                }

                var current = root;
                foreach (var segment in segments)
                {
                    current = Path.Combine(current, segment);
                    if (!seen.Add(current))
                    {
                        continue;
                    }

                    try
                    {
                        if (Directory.Exists(current))
                        {
                            result.JaExistiam.Add(Relative(root, current));
                        }
                        else
                        {
                            Directory.CreateDirectory(current);
                            result.Criadas.Add(Relative(root, current));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Erros.Add($"{Relative(root, current)}: {ex.Message}");
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> Expand(string raw, List<string> quadros, MaterializeResult result)
    {
        var path = raw.Replace('\\', '/').Trim('/');
        if (!path.Contains(TemplateCatalog.QuadroToken, StringComparison.Ordinal))
        {
            yield return path;
            yield break;
        }

        if (quadros.Count == 0)
        {
            result.Ignoradas.Add($"{path} (informe o nome do quadro)");
            yield break;
        }

        foreach (var quadro in quadros)
        {
            yield return path.Replace(TemplateCatalog.QuadroToken, quadro, StringComparison.Ordinal);
        }
    }

    private static bool IsSafe(IEnumerable<string> segments) =>
        segments.All(s => s is not ("." or "..") && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

    /// <summary>Remove caracteres invalidos e espacos/pontos finais (Windows rejeita ambos).</summary>
    public static string SanitizeSegment(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.TrimEnd('.', ' ');
    }

    private static string Relative(string root, string full) =>
        Path.GetRelativePath(root, full).Replace('\\', '/');
}
