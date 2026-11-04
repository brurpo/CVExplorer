using CVExplorer.Host.Features.FileSystem;

namespace CVExplorer.Host.Features.Media;

public sealed class PasteEvent
{
    /// <summary>Precisa bater com o card de progresso criado pela interface.</summary>
    public string OperacaoId { get; set; } = string.Empty;

    /// <summary>inicio | convertendo | copiado | convertido | erro | fim</summary>
    public string Tipo { get; set; } = string.Empty;

    public string Arquivo { get; set; } = string.Empty;

    public string? Destino { get; set; }

    public string? Mensagem { get; set; }

    public double Percentual { get; set; }

    public string? Perfil { get; set; }

    public int Total { get; set; }

    public int Atual { get; set; }
}

public sealed class PasteOutcome
{
    public List<string> Convertidos { get; } = new();

    public List<string> Copiados { get; } = new();

    public List<string> Erros { get; } = new();

    public bool Cancelado { get; set; }
}

/// <summary>
/// Regra de colar: video vira arquivo de edicao (ProRes) direto no destino.
/// Nada de copiar o original para dentro do projeto. Arquivos que ja sao de edicao
/// (ProRes/DNxHR/CineForm) ou que nao sao video sao copiados normalmente.
/// </summary>
public sealed class PasteService
{
    private static readonly HashSet<string> ExtensoesVideo = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mxf", ".avi", ".mkv", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg",
        ".mts", ".m2ts", ".r3d", ".braw", ".flv", ".ogv", ".3gp", ".m2v", ".ts",
    };

    private static readonly HashSet<string> CodecsDeEdicao = new(StringComparer.OrdinalIgnoreCase)
    {
        "prores", "dnxhd", "cfhd", "hap", "qtrle", "rawvideo", "v210", "bitpacked", "ffv1",
    };

    private readonly MediaProbe _probe;
    private readonly TranscodeService _transcode;
    private readonly FileOperations _files;
    private readonly AppSettings _settings;

    public PasteService(MediaProbe probe, TranscodeService transcode, FileOperations files, AppSettings settings)
    {
        _probe = probe;
        _transcode = transcode;
        _files = files;
        _settings = settings;
    }

    public static bool EhVideo(string caminho) => ExtensoesVideo.Contains(Path.GetExtension(caminho));

    public async Task<PasteOutcome> PasteAsync(
        IReadOnlyList<string> origens,
        string destino,
        string operacaoId,
        Func<PasteEvent, Task> onEvent,
        CancellationToken ct)
    {
        var outcome = new PasteOutcome();
        var copiar = new List<string>();
        var indice = 0;

        foreach (var origem in origens)
        {
            ct.ThrowIfCancellationRequested();
            indice++;

            if (Directory.Exists(origem))
            {
                copiar.Add(origem);
                continue;
            }

            if (!File.Exists(origem))
            {
                outcome.Erros.Add($"{Path.GetFileName(origem)}: arquivo nao encontrado.");
                continue;
            }

            if (!_settings.ConverterVideoAoColar || !EhVideo(origem))
            {
                copiar.Add(origem);
                continue;
            }

            await onEvent(new PasteEvent
            {
                OperacaoId = operacaoId,
                Tipo = "convertendo",
                Arquivo = Path.GetFileName(origem),
                Percentual = 0,
                Total = origens.Count,
                Atual = indice,
            }).ConfigureAwait(false);

            var info = await _probe.InspectAsync(origem, ct).ConfigureAwait(false);
            if (info is null)
            {
                // Sem ffprobe nao da para converter: melhor avisar do que copiar algo inutil para o AE.
                outcome.Erros.Add($"{Path.GetFileName(origem)}: nao foi possivel inspecionar o video (ffmpeg ausente?).");
                continue;
            }

            if (CodecsDeEdicao.Contains(info.Codec) && !string.IsNullOrEmpty(info.PixFmt))
            {
                // Ja e um codec de edicao: copiar preserva qualidade melhor do que reencodar.
                copiar.Add(origem);
                continue;
            }

            var espaco = Native.Win32.FreeSpace(destino);
            var plan = VideoPlanBuilder.Choose(info, _settings.PerfilVideoPadrao, espaco.Livre);

            var resultado = await _transcode.ConvertAsync(
                info,
                plan,
                destino,
                operacaoId,
                async p => await onEvent(new PasteEvent
                {
                    OperacaoId = operacaoId,
                    Tipo = "convertendo",
                    Arquivo = p.Arquivo,
                    Percentual = p.Percentual,
                    Mensagem = p.Velocidade,
                    Perfil = plan.NomeExibicao,
                    Total = origens.Count,
                    Atual = indice,
                }).ConfigureAwait(false),
                ct).ConfigureAwait(false);

            if (resultado.Ok && resultado.Saida is not null)
            {
                outcome.Convertidos.Add(resultado.Saida);
                await onEvent(new PasteEvent
                {
                    OperacaoId = operacaoId,
                    Tipo = "convertido",
                    Arquivo = Path.GetFileName(resultado.Saida),
                    Destino = resultado.Saida,
                    Perfil = plan.NomeExibicao,
                    Percentual = 100,
                    Total = origens.Count,
                    Atual = indice,
                    Mensagem = string.Join(" ", plan.Avisos),
                }).ConfigureAwait(false);
            }
            else
            {
                outcome.Erros.Add($"{Path.GetFileName(origem)}: {resultado.Erro}");
                await onEvent(new PasteEvent
                {
                    OperacaoId = operacaoId,
                    Tipo = "erro",
                    Arquivo = Path.GetFileName(origem),
                    Mensagem = resultado.Erro,
                    Total = origens.Count,
                    Atual = indice,
                }).ConfigureAwait(false);
            }
        }

        if (copiar.Count > 0)
        {
            var resultado = await _files.CopyAsync(copiar, destino, operacaoId, ct).ConfigureAwait(false);
            outcome.Copiados.AddRange(copiar.Where(File.Exists));
            outcome.Erros.AddRange(resultado.Erros);
            outcome.Cancelado |= resultado.Cancelado;
        }

        return outcome;
    }
}
