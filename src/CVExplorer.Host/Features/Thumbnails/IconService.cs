using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CVExplorer.Host.Features.FileSystem;
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

    /// <summary>Entrada de compactado acima disso nao vale a extracao so para ler o icone.</summary>
    private const long LimiteExtracao = 128L * 1024 * 1024;

    private readonly ArchiveService? _arquivos;
    private readonly ConcurrentDictionary<string, Task<IconeResult>> _emAndamento = new();

    public IconService(ArchiveService? arquivos = null) => _arquivos = arquivos;

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

        var tarefa = _emAndamento.GetOrAdd(chave, _ => GerarAsync(caminho, destino, chave, tamanho, ct));
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

    private async Task<IconeResult> GerarAsync(
        string caminho,
        string destino,
        string chave,
        long tamanho,
        CancellationToken ct)
    {
        string origem = caminho;
        string? temporario = null;

        try
        {
            // Dentro de compactado o caminho nao existe no disco e o Shell so devolve o
            // icone generico do tipo: extrai a entrada uma vez e le o icone do arquivo real.
            if (DeveExtrair(caminho, tamanho) && ArchiveService.Separar(caminho) is { } dentro)
            {
                var extraidos = _arquivos?.ExtrairParaTemporario(dentro.Arquivo, new[] { dentro.Interno });
                if (extraidos is { Count: > 0 })
                {
                    origem = extraidos[0];
                    temporario = extraidos[0];
                }
            }

            var png = await ShellThumbnail.GetIconPngAsync(origem, Lado, ct).ConfigureAwait(false);
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
        finally
        {
            LimparTemporario(temporario);
        }
    }

    /// <summary>Vale extrair a entrada do compactado para ler o icone?</summary>
    private static bool DeveExtrair(string caminho, long tamanho)
    {
        if (ArchiveService.Separar(caminho) is null)
        {
            return false;
        }

        // So faz sentido quando o icone vem do arquivo; .txt dentro de zip usa o do tipo.
        if (!PorArquivo.Contains(Path.GetExtension(caminho)))
        {
            return false;
        }

        return tamanho <= 0 || tamanho <= LimiteExtracao;
    }

    private static void LimparTemporario(string? arquivo)
    {
        if (string.IsNullOrEmpty(arquivo))
        {
            return;
        }

        try
        {
            var raiz = Path.Combine(Path.GetTempPath(), "CVExplorer", "compactados");
            var pasta = Path.GetDirectoryName(arquivo);

            while (pasta is not null
                   && !string.Equals(Path.GetDirectoryName(pasta), raiz, StringComparison.OrdinalIgnoreCase))
            {
                pasta = Path.GetDirectoryName(pasta);
            }

            if (pasta is not null)
            {
                Directory.Delete(pasta, true);
            }
        }
        catch (Exception ex)
        {
            // Pasta temporaria que ficou para tras nao pode derrubar o icone.
            Log.Error("Falha ao limpar temporario do icone", ex);
        }
    }
}
