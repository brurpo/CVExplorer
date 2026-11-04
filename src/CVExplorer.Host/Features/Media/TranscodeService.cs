using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace CVExplorer.Host.Features.Media;

public sealed class TranscodeProgress
{
    public string OperacaoId { get; set; } = string.Empty;

    public string Arquivo { get; set; } = string.Empty;

    public double Percentual { get; set; }

    public string Velocidade { get; set; } = string.Empty;

    public TimeSpan TempoDecorrido { get; set; }
}

public sealed class TranscodeResult
{
    public bool Ok { get; set; }

    public string? Saida { get; set; }

    public string? Erro { get; set; }
}

/// <summary>
/// Colar video = transcodificar direto no destino.
/// O arquivo original NUNCA entra no projeto: o que aparece na pasta e apenas o .mov
/// convertido. O original permanece intacto onde estava.
/// Grava em .partial.mov e so renomeia no fim, para nunca aparecer arquivo pela metade.
/// </summary>
public sealed class TranscodeService
{
    private readonly FfmpegLocator _locator;

    public TranscodeService(FfmpegLocator locator) => _locator = locator;

    public async Task<TranscodeResult> ConvertAsync(
        MediaInfo info,
        VideoPlan plan,
        string pastaDestino,
        string operacaoId,
        Func<TranscodeProgress, Task> onProgress,
        CancellationToken ct)
    {
        var ffmpeg = _locator.Ffmpeg;
        if (ffmpeg is null)
        {
            return new TranscodeResult
            {
                Ok = false,
                Erro = "ffmpeg nao encontrado. Defina o caminho em Configuracoes.",
            };
        }

        if (!Directory.Exists(pastaDestino))
        {
            return new TranscodeResult { Ok = false, Erro = $"Pasta de destino inexistente: {pastaDestino}" };
        }

        var baseNome = Path.GetFileNameWithoutExtension(info.Caminho);
        var destino = Features.FileSystem.FileOperations.UniquePath(
            Path.Combine(pastaDestino, baseNome + plan.Container),
            plan.Container,
            isDirectory: false);

        var parcial = Path.Combine(
            Path.GetDirectoryName(destino)!,
            Path.GetFileNameWithoutExtension(destino) + ".partial.mov");

        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in FfmpegCommandBuilder.Build(info, plan, info.Caminho, parcial))
        {
            psi.ArgumentList.Add(arg);
        }

        var inicio = Stopwatch.StartNew();
        using var process = Process.Start(psi);
        if (process is null)
        {
            return new TranscodeResult { Ok = false, Erro = "Nao foi possivel iniciar o ffmpeg." };
        }

        var erros = process.StandardError.ReadToEndAsync(ct);
        var ultimoPercentual = -1.0;

        try
        {
            while (await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                var separador = line.IndexOf('=');
                if (separador <= 0)
                {
                    continue;
                }

                var chave = line[..separador];
                var valor = line[(separador + 1)..].Trim();

                if (chave is "out_time_us" or "out_time_ms" or "out_time")
                {
                    if (!TrySegundos(chave, valor, out var segundos) || segundos <= 0 || info.DuracaoSegundos <= 0)
                    {
                        continue;
                    }

                    var percentual = Math.Clamp(segundos / info.DuracaoSegundos * 100, 0, 100);

                    // O ffmpeg emite muitas linhas: limita o trafego para a interface.
                    if (percentual - ultimoPercentual < 0.4)
                    {
                        continue;
                    }

                    ultimoPercentual = percentual;
                    await onProgress(new TranscodeProgress
                    {
                        OperacaoId = operacaoId,
                        Arquivo = Path.GetFileName(info.Caminho),
                        Percentual = percentual,
                        TempoDecorrido = inicio.Elapsed,
                    }).ConfigureAwait(false);
                }
                else if (chave == "speed")
                {
                    await onProgress(new TranscodeProgress
                    {
                        OperacaoId = operacaoId,
                        Arquivo = Path.GetFileName(info.Caminho),
                        Percentual = Math.Max(ultimoPercentual, 0),
                        Velocidade = valor,
                        TempoDecorrido = inicio.Elapsed,
                    }).ConfigureAwait(false);
                }
            }

            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            TryDelete(parcial);
            throw;
        }

        if (process.ExitCode != 0)
        {
            var detalhe = (await erros.ConfigureAwait(false)).Trim();
            TryDelete(parcial);
            Log.Error($"ffmpeg falhou ({process.ExitCode}): {detalhe}");

            var ultimaLinha = detalhe
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault()?.Trim();

            return new TranscodeResult
            {
                Ok = false,
                Erro = string.IsNullOrWhiteSpace(ultimaLinha)
                    ? $"ffmpeg terminou com codigo {process.ExitCode}."
                    : ultimaLinha,
            };
        }

        File.Move(parcial, destino, overwrite: false);
        await RegistrarConversaoAsync(info, plan, destino).ConfigureAwait(false);

        return new TranscodeResult { Ok = true, Saida = destino };
    }

    /// <summary>
    /// Tempo ja processado. out_time_us e out_time_ms vem em microssegundos (peculiaridade
    /// historica do ffmpeg); out_time vem como HH:MM:SS.ffffff.
    /// </summary>
    internal static bool TrySegundos(string chave, string valor, out double segundos)
    {
        segundos = 0;

        if (chave == "out_time")
        {
            return TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out var tempo)
                   && (segundos = Math.Max(0, tempo.TotalSeconds)) >= 0;
        }

        if (long.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out var micros) && micros >= 0)
        {
            segundos = micros / 1_000_000.0;
            return true;
        }

        return false;
    }

    private static void TryKill(Process process)    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao encerrar ffmpeg", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // .partial fica para limpeza manual
        }
    }

    /// <summary>Historico de conversoes em %LOCALAPPDATA% (nao suja a pasta do projeto).</summary>
    private static async Task RegistrarConversaoAsync(MediaInfo info, VideoPlan plan, string destino)
    {
        try
        {
            AppPaths.EnsureCreated();
            var arquivo = Path.Combine(AppPaths.Root, "conversoes.jsonl");
            var registro = new
            {
                origem = info.Caminho,
                saida = destino,
                perfil = plan.NomeExibicao,
                pixFmt = plan.PixFmt,
                alpha = plan.ComAlpha,
                largura = info.Largura,
                altura = info.Altura,
                fps = info.Fps,
                duracao = info.DuracaoSegundos,
                hdr = info.Hdr,
                em = DateTimeOffset.Now,
            };

            await File.AppendAllTextAsync(
                arquivo,
                JsonSerializer.Serialize(registro, Json.Compact) + Environment.NewLine).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao registrar conversao", ex);
        }
    }
}
