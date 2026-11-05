using System.Collections.Concurrent;
using System.Diagnostics;

namespace CVExplorer.Host.Features.Media;

public sealed class PlayerStream
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public string Caminho { get; set; } = string.Empty;

    public Process? Processo { get; set; }

    public Stream? Saida { get; set; }

    public string? Encoder { get; set; }

    public string? Erro { get; set; }
}

/// <summary>
/// O player e o Chromium do WebView2: ele nao decodifica MXF, ProRes, DNxHR nem R3D.
/// Em vez de gerar (e esperar) um arquivo de previa, o ffmpeg decodifica e entrega
/// um fluxo H.264 fragmentado direto no &lt;video&gt;, enquanto toca. Nada e gravado em disco:
/// abrir um video custa o tempo de iniciar o processo, e o scrub reinicia o fluxo no ponto.
///
/// O fluxo e <b>all-intra</b> (keyframe em todo quadro): dentro do trecho ja baixado o
/// &lt;video&gt; pula para qualquer instante sem esperar GOP nenhum, e um fluxo reaberto mostra
/// o primeiro quadro na hora. Custa bitrate, que aqui e loopback.
/// </summary>
public sealed class PreviewStreamService
{
    /// <summary>Resolucao maxima do fluxo (o painel de previa nao precisa de mais).</summary>
    private const int LarguraMaxima = 1280;

    private readonly FfmpegLocator _locator;
    private readonly ConcurrentDictionary<string, PlayerStream> _ativos = new();

    /// <summary>ffmpeg de extração de quadro em curso por arquivo (scrub arrastando).</summary>
    private readonly ConcurrentDictionary<string, Process> _quadros = new();
    private List<string> _disponiveis = new();
    private bool _procurou;

    public PreviewStreamService(FfmpegLocator locator) => _locator = locator;

    public PlayerStream Abrir(MediaInfo info, double inicioSegundos)
    {
        var ffmpeg = _locator.Ffmpeg;
        if (ffmpeg is null)
        {
            return new PlayerStream { Erro = "ffmpeg nao encontrado." };
        }

        var candidatos = Encoders(ffmpeg);
        if (candidatos.Count == 0)
        {
            return new PlayerStream { Erro = "Este ffmpeg nao tem encoder H.264 (libx264, h264_mf...)." };
        }

        // Encoder de GPU pode existir e ainda assim falhar (sem sessao, driver antigo).
        // Entao tenta um por um ate um deles ficar de pe produzindo o fluxo.
        foreach (var encoder in candidatos)
        {
            var tentativa = Iniciar(ffmpeg, info, encoder, inicioSegundos, allIntra: true);
            if (tentativa.Erro is not null || tentativa.Processo is null)
            {
                continue;
            }

            if (Estavel(tentativa.Processo))
            {
                Adotar(tentativa);
                return tentativa;
            }

            Log.Error($"Encoder {encoder} falhou ao iniciar o fluxo; tentando o proximo.");
            Encerrar(tentativa);
        }

        // Ultimo recurso: os mesmos encoders com o ajuste conservador. Uma opcao que este
        // ffmpeg nao aceita (aconteceu com -rate_control do h264_mf) derrubaria o player
        // inteiro; aqui ele volta a tocar, so com o scrub menos imediato.
        foreach (var encoder in candidatos)
        {
            var tentativa = Iniciar(ffmpeg, info, encoder, inicioSegundos, allIntra: false);
            if (tentativa.Erro is not null || tentativa.Processo is null)
            {
                continue;
            }

            if (Estavel(tentativa.Processo))
            {
                Log.Error($"Fluxo aberto com ajustes conservadores ({encoder}).");
                Adotar(tentativa);
                return tentativa;
            }

            Encerrar(tentativa);
        }

        return new PlayerStream { Erro = "Nenhum encoder H.264 conseguiu abrir o fluxo." };
    }

    private void Adotar(PlayerStream fluxo)
    {
        _ativos[fluxo.Id] = fluxo;
        fluxo.Processo!.EnableRaisingEvents = true;
        fluxo.Processo.Exited += (_, _) => _ativos.TryRemove(fluxo.Id, out _);
    }

    private static PlayerStream Iniciar(string ffmpeg, MediaInfo info, string encoder, double inicioSegundos, bool allIntra)
    {
        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in Argumentos(info, encoder, inicioSegundos, allIntra))
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            var processo = Process.Start(psi);
            if (processo is null)
            {
                return new PlayerStream { Erro = "Nao foi possivel iniciar o ffmpeg." };
            }

            var fluxo = new PlayerStream
            {
                Caminho = info.Caminho,
                Encoder = encoder,
                Processo = processo,
                Saida = processo.StandardOutput.BaseStream,
            };

            // stderr precisa ser consumido, senao o processo trava quando o buffer enche.
            processo.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is { Length: > 0 } linha)
                {
                    Log.Error($"previa ffmpeg ({encoder}): {linha}");
                }
            };
            processo.BeginErrorReadLine();

            return fluxo;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao abrir previa com {encoder}", ex);
            return new PlayerStream { Erro = ex.Message };
        }
    }

    /// <summary>Da tempo do ffmpeg falhar ao abrir o encoder (GPU sem sessao, preset invalido).</summary>
    private static bool Estavel(Process processo)
    {
        try
        {
            return !processo.WaitForExit(700) || processo.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public PlayerStream? Obter(string id) => _ativos.TryGetValue(id, out var fluxo) ? fluxo : null;

    public void Fechar(string id)
    {
        if (!_ativos.TryRemove(id, out var fluxo))
        {
            return;
        }

        Encerrar(fluxo);
    }

    /// <summary>Encerra os fluxos que estão a ler algum destes arquivos e espera o ffmpeg sair.</summary>
    public void FecharPorCaminho(IEnumerable<string> caminhos)
    {
        var alvos = new HashSet<string>(
            (caminhos ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);

        if (alvos.Count == 0)
        {
            return;
        }

        foreach (var fluxo in _ativos.Values.Where(f => alvos.Contains(Seguro(f.Caminho))))
        {
            Fechar(fluxo.Id);
        }

        static string Seguro(string caminho)
        {
            try
            {
                return Path.GetFullPath(caminho);
            }
            catch
            {
                return caminho;
            }
        }
    }

    public void FecharTodos()
    {
        foreach (var id in _ativos.Keys.ToArray())
        {
            Fechar(id);
        }
    }

    /// <summary>Fecha processos de fluxos que nao sao vistos ha muito tempo.</summary>
    public void Encerrar(PlayerStream fluxo)
    {
        try
        {
            if (fluxo.Processo is { HasExited: false })
            {
                fluxo.Processo.Kill(entireProcessTree: true);

                // Espera o processo morrer: enquanto ele vive, o arquivo continua travado
                // e o Windows recusa mover/apagar (era o "aberto em ffmpeg" ao excluir).
                fluxo.Processo.WaitForExit(2000);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao encerrar ffmpeg da previa", ex);
        }

        try
        {
            fluxo.Saida?.Dispose();
            fluxo.Processo?.Dispose();
        }
        catch
        {
            // processo ja finalizado
        }
    }

    internal static List<string> Argumentos(MediaInfo info, string encoder, double inicioSegundos, bool allIntra = true)
    {
        var args = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin" };

        // -ss antes do -i: busca por keyframe, instantanea mesmo em arquivos longos.
        if (inicioSegundos > 0.05)
        {
            args.Add("-ss");
            args.Add(inicioSegundos.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
        }

        args.AddRange(new[]
        {
            "-i", info.Caminho,
            "-map", "0:v:0", "-map", "0:a:0?",
            "-sn", "-dn",
            "-vf", $"scale=min({LarguraMaxima}\\,iw):-2",
            "-pix_fmt", "yuv420p",
        });

        if (allIntra)
        {
            // All-intra: todo quadro e keyframe. O scrub nao espera GOP para achar o ponto.
            args.AddRange(new[] { "-g", "1", "-bf", "0" });
        }

        args.AddRange(EncoderArgs(encoder, allIntra));

        args.AddRange(new[]
        {
            "-c:a", "aac", "-b:a", "128k", "-ac", "2",
            // Fragmentado: o navegador toca antes do arquivo terminar (nao existe arquivo).
            "-f", "mp4",
            "-movflags", "frag_keyframe+empty_moov+default_base_moof+delay_moov",
            "-flush_packets", "1",
            "pipe:1",
        });

        return args;
    }

    /// <summary>
    /// Um quadro do vídeo no instante pedido, em JPEG. É o que aparece embaixo do cursor
    /// enquanto a barra de tempo é arrastada em um arquivo que o navegador não decodifica:
    /// em vez de reiniciar o fluxo a cada movimento (caro e aos trancos), sai um quadro por
    /// pedido — com no máximo um ffmpeg vivo por arquivo.
    /// </summary>
    public async Task<byte[]?> QuadroAsync(string caminho, double segundos, int largura, CancellationToken ct)
    {
        var ffmpeg = _locator.Ffmpeg;
        if (ffmpeg is null)
        {
            return null;
        }

        // Um pedido por vez por arquivo: o anterior é morto, senão o arraste empilha processos.
        var chave = caminho.ToLowerInvariant();
        if (_quadros.TryRemove(chave, out var anterior))
        {
            Matar(anterior);
        }

        var psi = new ProcessStartInfo(ffmpeg)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var cultura = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var arg in new[]
        {
            "-hide_banner", "-loglevel", "error", "-nostdin",
            // -ss antes do -i: busca por keyframe (num intra-frame é o instante exato).
            "-ss", Math.Max(0, segundos).ToString("0.###", cultura),
            "-i", caminho,
            "-frames:v", "1",
            "-vf", $"scale=min({largura}\\,iw):-2",
            "-q:v", "4",
            "-f", "image2", "-c:v", "mjpeg",
            "pipe:1",
        })
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            using var processo = Process.Start(psi);
            if (processo is null)
            {
                return null;
            }

            _quadros[chave] = processo;
            processo.ErrorDataReceived += (_, e) => { _ = e.Data; };
            processo.BeginErrorReadLine();

            using var memoria = new MemoryStream();
            await processo.StandardOutput.BaseStream.CopyToAsync(memoria, ct).ConfigureAwait(false);
            await processo.WaitForExitAsync(ct).ConfigureAwait(false);

            return memoria.Length > 0 ? memoria.ToArray() : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao extrair quadro de {caminho}", ex);
            return null;
        }
        finally
        {
            _quadros.TryRemove(chave, out _);
        }
    }

    private static void Matar(Process processo)
    {
        try
        {
            if (!processo.HasExited)
            {
                processo.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // processo já saiu
        }
    }

    private static IEnumerable<string> EncoderArgs(string encoder, bool allIntra = true) => encoder switch
    {
        // All-intra precisa de bitrate: pouco bitrate por quadro fica borrado. Num proxy de
        // 1280 px de largura, o modo qualidade acerta o olho sem depender de CBR.
        "libx264" => new[]
        {
            "-c:v", "libx264", "-preset", "ultrafast", "-tune", "zerolatency",
            "-crf", "20", "-x264-params", "keyint=1:min-keyint=1:scenecut=0",
        },
        "h264_nvenc" => new[] { "-c:v", "h264_nvenc", "-preset", "p1", "-tune", "ll", "-rc", "constqp", "-qp", "22" },
        "h264_qsv" => new[] { "-c:v", "h264_qsv", "-preset", "veryfast", "-b:v", "16M" },
        "h264_amf" => new[] { "-c:v", "h264_amf", "-quality", "speed", "-b:v", "16M" },
        // h264_mf nao tem -preset; o que existe e scenario/rate_control. Os valores aceitos
        // aqui sao default, cbr, pc_vbr e quality — qualquer outro derruba o encoder.
        "h264_mf" => allIntra
            ? new[]
            {
                "-c:v", "h264_mf", "-scenario", "live_streaming", "-rate_control", "quality", "-quality", "75",
            }
            : new[] { "-c:v", "h264_mf", "-scenario", "live_streaming", "-rate_control", "cbr", "-b:v", "8M" },
        "libopenh264" => new[] { "-c:v", "libopenh264", "-b:v", "16M" },
        "h264_vulkan" => new[] { "-c:v", "h264_vulkan", "-b:v", "16M" },
        _ => new[] { "-c:v", encoder, "-b:v", "16M" },
    };

    /// <summary>Todos os encoders H.264 que este ffmpeg oferece, na ordem de preferencia.</summary>
    private List<string> Encoders(string ffmpeg)
    {
        if (_procurou)
        {
            return _disponiveis;
        }

        _procurou = true;
        string[] preferidos =
        [
            "libx264",
            "h264_nvenc",
            "h264_qsv",
            "h264_amf",
            "h264_mf",
            "libopenh264",
            "h264_vulkan",
        ];

        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("-hide_banner");
            psi.ArgumentList.Add("-encoders");

            using var processo = Process.Start(psi);
            if (processo is null)
            {
                return _disponiveis;
            }

            var texto = processo.StandardOutput.ReadToEnd();
            processo.StandardError.ReadToEnd();
            processo.WaitForExit(5000);

            _disponiveis = preferidos.Where(p => texto.Contains($" {p} ", StringComparison.Ordinal)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao listar encoders do ffmpeg", ex);
        }

        return _disponiveis;
    }
}
