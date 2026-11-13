using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CVExplorer.Host.Native;

namespace CVExplorer.Host.Features.Thumbnails;

public sealed class IconeResult
{
    public string Chave { get; set; } = string.Empty;

    public bool Pronta { get; set; }
}

/// <summary>
/// Icones do Windows por tipo de arquivo (ou por arquivo, quando o icone vem de dentro
/// dele: .exe, .lnk, .ico…). Guarda PNG de 256 px — o maior tamanho que o Shell mantem —
/// no mesmo cache das miniaturas, entao a grade mostra o mesmo icone do Explorer sem
/// pedir nada ao Shell de novo.
/// </summary>
public sealed class IconService
{
    /// <summary>Icone no maior tamanho que o Windows guarda (256 px): a grade escala para baixo.</summary>
    public const int Lado = 256;

    /// <summary>Extensoes cujo icone vem de dentro do arquivo, nao do tipo.</summary>
    private static readonly HashSet<string> PorArquivo = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".url", ".ico", ".msi", ".scr", ".cpl", ".dll", ".com", ".appref-ms",
    };

    private readonly ConcurrentDictionary<string, Task<IconeResult>> _emAndamento = new();

    /// <summary>
    /// Chave do cache: por extensao quando o icone e do tipo (.txt, .pdf, .blend…) e por
    /// caminho quando ele vem do proprio arquivo (.exe, .lnk…). Assim mil .txt dividem
    /// um PNG so, e cada executavel guarda o seu.
    /// </summary>
    public static string Chave(string caminho, long tamanho, DateTime modificado)
    {
        var ext = Path.GetExtension(caminho).ToLowerInvariant();
        var identidade = PorArquivo.Contains(ext)
            ? $"{caminho.ToLowerInvariant()}|{tamanho}|{modificado.Ticks}"
            : ext;

        var hash = SHA1.HashData(Encoding.UTF8.GetBytes($"v1|{identidade}|{Lado}"));
        return "icone-" + Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    public static string UrlFor(string chave) =>
        $"https://{AppPaths.ThumbsVirtualHost}/{chave}.png";

    public async Task<IconeResult> ObterAsync(
        string caminho,
        long tamanho,
        DateTime modificado,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(caminho))
        {
            return new IconeResult();
        }

        var chave = Chave(caminho, tamanho, modificado);
        var destino = Path.Combine(AppPaths.ThumbsDir, chave + ".png");

        if (File.Exists(destino))
        {
            return new IconeResult { Chave = chave, Pronta = true };
        }

        var tarefa = _emAndamento.GetOrAdd(chave, _ => GerarAsync(caminho, destino, chave, ct));
        try
        {
            return await tarefa.ConfigureAwait(false);
        }
        finally
        {
            if (tarefa.IsCompleted)
            {
                _emAndamento.TryRemove(chave, out _);
            }
        }
    }

    private static async Task<IconeResult> GerarAsync(string caminho, string destino, string chave, CancellationToken ct)
    {
        var png = await ShellThumbnail.GetIconPngAsync(caminho, Lado, ct).ConfigureAwait(false);
        if (png is null || png.Length == 0)
        {
            return new IconeResult { Chave = chave, Pronta = false };
        }

        try
        {
            Directory.CreateDirectory(AppPaths.ThumbsDir);
            File.WriteAllBytes(destino, png);
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao guardar o icone de {caminho}", ex);
        }

        return new IconeResult { Chave = chave, Pronta = true };
    }
}
