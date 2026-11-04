using System.Globalization;

namespace CVExplorer.Host.Features.Search;

/// <summary>
/// Sintaxe de busca:
///   texto livre          -> casa no nome do arquivo
///   ext:psd,ai           -> filtra por extensao
///   tipo:video           -> video | imagem | audio | 3d | documento
///   nome:"shot 01"       -> trecho exato do nome
///   pasta:"chamadas"     -> trecho do caminho
/// </summary>
public sealed class SearchQuery
{
    private static readonly CompareInfo Comparador =
        CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    public List<string> Textos { get; } = new();

    public List<string> Extensoes { get; } = new();

    public List<string> Tipos { get; } = new();

    public List<string> Nomes { get; } = new();

    public List<string> Pastas { get; } = new();

    public bool Vazia => Textos.Count == 0 && Extensoes.Count == 0 && Tipos.Count == 0
                         && Nomes.Count == 0 && Pastas.Count == 0;

    public static readonly IReadOnlyDictionary<string, string[]> TiposConhecidos =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["video"] = new[] { ".mp4", ".mov", ".mxf", ".avi", ".mkv", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".r3d", ".braw" },
            ["imagem"] = new[] { ".jpg", ".jpeg", ".png", ".tif", ".tiff", ".psd", ".psb", ".ai", ".svg", ".exr", ".gif", ".bmp", ".webp", ".tga", ".dpx", ".heic", ".cr2", ".nef", ".arw" },
            ["audio"] = new[] { ".wav", ".mp3", ".aif", ".aiff", ".flac", ".m4a", ".ogg", ".aac" },
            ["3d"] = new[] { ".blend", ".obj", ".fbx", ".usd", ".usda", ".usdc", ".usdz", ".glb", ".gltf", ".ma", ".mb", ".c4d", ".abc", ".stl" },
            ["documento"] = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".csv", ".md" },
            ["projeto"] = new[] { ".aep", ".prproj", ".drp", ".nk", ".c4d" },
        };

    public static SearchQuery Parse(string? raw)
    {
        var query = new SearchQuery();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return query;
        }

        foreach (var token in Tokenize(raw))
        {
            var colon = token.IndexOf(':');
            if (colon <= 0)
            {
                query.Textos.Add(token);
                continue;
            }

            var key = token[..colon].Trim().ToLowerInvariant();
            var value = token[(colon + 1)..].Trim().Trim('"');

            if (value.Length == 0)
            {
                continue;
            }

            switch (key)
            {
                case "ext":
                case "extensao":
                case "extensão":
                    foreach (var ext in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var clean = ext.Trim().TrimStart('*', '.');
                        if (clean.Length > 0)
                        {
                            query.Extensoes.Add("." + clean.ToLowerInvariant());
                        }
                    }

                    break;

                case "tipo":
                    var normalized = RemoveAcentos(value).ToLowerInvariant();
                    if (TiposConhecidos.ContainsKey(normalized))
                    {
                        query.Tipos.Add(normalized);
                    }

                    break;

                case "nome":
                case "name":
                    query.Nomes.Add(value);
                    break;

                case "pasta":
                case "caminho":
                case "path":
                    query.Pastas.Add(value);
                    break;

                default:
                    query.Textos.Add(token);
                    break;
            }
        }

        return query;
    }

    public bool Matches(string fullPath, string fileName)
    {
        if (Extensoes.Count > 0)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (!Extensoes.Contains(ext))
            {
                return false;
            }
        }

        if (Tipos.Count > 0)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var ok = Tipos.Any(t => TiposConhecidos[t].Contains(ext));
            if (!ok)
            {
                return false;
            }
        }

        foreach (var nome in Nomes)
        {
            if (IndexOf(fileName, nome) < 0)
            {
                return false;
            }
        }

        foreach (var texto in Textos)
        {
            if (IndexOf(fileName, texto) < 0)
            {
                return false;
            }
        }

        foreach (var pasta in Pastas)
        {
            if (IndexOf(fullPath, pasta) < 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int IndexOf(string haystack, string needle) =>
        Comparador.IndexOf(haystack, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);

    public static string RemoveAcentos(string value)
    {
        var normalized = value.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    private static IEnumerable<string> Tokenize(string raw)
    {
        var buffer = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in raw)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                buffer.Append(c);
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (buffer.Length > 0)
                {
                    yield return buffer.ToString();
                    buffer.Clear();
                }

                continue;
            }

            buffer.Append(c);
        }

        if (buffer.Length > 0)
        {
            yield return buffer.ToString();
        }
    }
}
