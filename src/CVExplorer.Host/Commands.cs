using System.Diagnostics;
using System.Text.Json;
using CVExplorer.Host.Features.FileSystem;
using CVExplorer.Host.Features.Media;
using CVExplorer.Host.Features.Projects;
using CVExplorer.Host.Features.Search;
using CVExplorer.Host.Features.Thumbnails;
using CVExplorer.Host.Ipc;
using CVExplorer.Host.Native;

namespace CVExplorer.Host;

/// <summary>
/// Tabela de comandos expostos a interface. Cada linha e um contrato:
/// nome do comando -> handler. Nada de logica de negocio aqui, so traducao.
/// </summary>
public static class Commands
{
    public static Dictionary<string, IpcBridge.Route> Build(AppHost host)
    {
        var routes = new Dictionary<string, IpcBridge.Route>(StringComparer.Ordinal);

        void Add(string name, Func<JsonElement, CancellationToken, Task<object?>> handler, bool operacao = false) =>
            routes[name] = new IpcBridge.Route(handler, operacao);

        // ---------- app / configuracao ----------

        Add("app.info", (_, _) => Task.FromResult<object?>(new
        {
            versao = AppInfo.Versao,
            nome = AppInfo.Nome,
            plataforma = Environment.OSVersion.VersionString,
            maquina = Environment.MachineName,
            pastas = new
            {
                dados = AppPaths.Root,
                miniaturas = AppPaths.ThumbsDir,
                logs = AppPaths.LogsDir,
            },
            ffmpeg = new
            {
                encontrado = host.Ffmpeg.Ffmpeg is not null,
                caminho = host.Ffmpeg.Ffmpeg,
                ffprobe = host.Ffmpeg.Ffprobe,
            },
            repositorioConfigurado = AppInfo.RepositorioConfigurado,
            raizes = host.Settings.Raizes,
            janela = host.JanelaDoContexto?.Id,
        }));

        Add("settings.get", (_, _) => Task.FromResult<object?>(host.Settings));

        Add("settings.save", (args, _) =>
        {
            var incoming = args.Deserialize<AppSettings>(Json.Options) ?? new AppSettings();
            host.Store.Save(incoming);
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("app.abrirPastaDados", (_, _) =>
        {
            Process.Start(new ProcessStartInfo(AppPaths.Root) { UseShellExecute = true });
            return Task.FromResult<object?>(new { ok = true });
        });

        // Guarda a ultima pasta so em memoria: a gravacao em disco acontece ao fechar a janela.
        Add("app.ultimaPasta", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            host.Settings.UltimaPasta = caminho;

            // Cada janela tem a sua pasta atual: e o destino do arraste entre janelas.
            if (host.JanelaDoContexto is { } janela)
            {
                janela.PastaAtual = caminho;
            }

            return Task.FromResult<object?>(new { ok = true });
        });

        // Pasta com que esta janela deve abrir (janela nova ja nasce no lugar certo).
        Add("app.inicio", (_, _) => Task.FromResult<object?>(new
        {
            caminho = host.JanelaDoContexto?.CaminhoInicial,
            janela = host.JanelaDoContexto?.Id,
        }));

        Add("app.novaJanela", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            var janela = host.AbrirJanela(string.IsNullOrWhiteSpace(caminho) ? host.JanelaDoContexto?.PastaAtual : caminho);
            return Task.FromResult<object?>(new { ok = true, janela = janela.Id });
        });

        // ---------- arraste entre janelas ----------

        Add("arraste.iniciar", (args, _) =>
        {
            var origem = host.JanelaDoContexto;
            if (origem is null)
            {
                return Task.FromResult<object?>(new { ok = false });
            }

            host.Arraste.Iniciar(origem, StrArray(args, "caminhos"), Bool(args, "copiar", false));
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("arraste.soltar", async (_, ct) =>
        {
            var entregue = await host.Arraste.SoltarAsync().ConfigureAwait(false);
            return new { ok = true, entregue };
        }, operacao: true);

        Add("arraste.cancelar", (_, _) =>
        {
            host.Arraste.Cancelar();
            return Task.FromResult<object?>(new { ok = true });
        });

        // ---------- sistema de arquivos ----------

        Add("fs.list", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var ocultos = Bool(args, "mostrarOcultos", host.Settings.MostrarOcultos);
            var opId = Str(args, "opId");

            var itens = await DirectoryLister.ListAsync(
                caminho,
                ocultos,
                batch =>
                {
                    host.Emit("dir.batch", new { opId, itens = batch });
                    return Task.CompletedTask;
                },
                ct).ConfigureAwait(false);

            host.Emit("dir.done", new { opId, total = itens.Count });
            return new { total = itens.Count };
        }, operacao: true);

        Add("fs.free", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            var (livre, total) = Win32.FreeSpace(caminho);
            return Task.FromResult<object?>(new
            {
                livre,
                total,
                livreTexto = VideoPlanBuilder.FormatBytes(livre),
            });
        });

        Add("fs.roots", (_, _) => Task.FromResult<object?>(Roots(host)));

        Add("fs.pickFolder", (args, _) =>
        {
            using var dialogo = new FolderBrowserDialog
            {
                Description = Str(args, "titulo", "Escolher pasta"),
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };

            var inicial = Str(args, "inicial");
            if (Directory.Exists(inicial))
            {
                dialogo.SelectedPath = inicial;
            }

            var ok = dialogo.ShowDialog() == DialogResult.OK;
            return Task.FromResult<object?>(new { caminho = ok ? dialogo.SelectedPath : null });
        });

        // Pre-visualizacao entregue como bytes (base64): nao depende de host virtual.
        Add("fs.preview", (args, _) =>
        {
            var (mime, dados) = PreviewRenderer.Render(Str(args, "caminho"), Int(args, "max", 1600));
            return Task.FromResult<object?>(new { mime, dados });
        });

        Add("fs.reveal", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            var ok = Win32.RevealInExplorer(caminho);
            if (!ok)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{caminho}\"") { UseShellExecute = true });
            }

            return Task.FromResult<object?>(new { ok = true });
        });

        Add("fs.open", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("fs.stat", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            if (Directory.Exists(caminho))
            {
                return Task.FromResult<object?>(new { pasta = true, tamanho = 0L, modificado = Directory.GetLastWriteTime(caminho) });
            }

            var info = new FileInfo(caminho);
            return Task.FromResult<object?>(new
            {
                pasta = false,
                tamanho = info.Exists ? info.Length : 0L,
                modificado = info.Exists ? info.LastWriteTime : DateTime.MinValue,
                criado = info.Exists ? info.CreationTime : DateTime.MinValue,
            });
        });

        Add("fs.copy", async (args, ct) =>
        {
            host.Conflicts.Reset();
            var origens = StrArray(args, "origens");
            var destino = Str(args, "destino");
            var opId = Str(args, "opId");
            var resultado = await host.Files.CopyAsync(origens, destino, opId, ct).ConfigureAwait(false);
            return ResultadoOperacao(resultado);
        }, operacao: true);

        Add("fs.move", async (args, ct) =>
        {
            host.Conflicts.Reset();
            var origens = StrArray(args, "origens");
            var destino = Str(args, "destino");
            var opId = Str(args, "opId");
            var resultado = await host.Files.MoveAsync(origens, destino, opId, ct).ConfigureAwait(false);
            return ResultadoOperacao(resultado);
        }, operacao: true);

        Add("fs.rename", (args, _) =>
        {
            var caminho = FileOperations.Rename(Str(args, "caminho"), Str(args, "novoNome"));
            return Task.FromResult<object?>(new { caminho });
        });

        Add("fs.mkdir", (args, _) =>
        {
            var caminho = FileOperations.CreateFolder(Str(args, "pai"), Str(args, "nome", "Nova pasta"));
            return Task.FromResult<object?>(new { caminho });
        });

        Add("fs.delete", (args, _) =>
        {
            var caminhos = StrArray(args, "caminhos");
            FileOperations.DeleteToRecycleBin(caminhos);
            return Task.FromResult<object?>(new { ok = true, excluidos = caminhos.Count });
        });

        Add("fs.conflict", (args, _) =>
        {
            var id = Str(args, "id");
            var decisao = Str(args, "decisao") switch
            {
                "substituir" => ConflictDecision.Substituir,
                "manterAmbos" => ConflictDecision.ManterAmbos,
                "pular" => ConflictDecision.Pular,
                _ => ConflictDecision.Cancelar,
            };

            host.Conflicts.Resolve(id, decisao, Bool(args, "aplicarTodos"));
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("fs.cancelar", (args, _) =>
        {
            host.Cancels.Cancel(Str(args, "opId"));
            host.Conflicts.CancelAll();
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("fs.clipboardGet", (_, _) => Task.FromResult<object?>(new
        {
            caminhos = System.Windows.Forms.Clipboard.ContainsFileDropList()
                ? System.Windows.Forms.Clipboard.GetFileDropList().Cast<string>().ToList()
                : new List<string>(),
        }));

        Add("fs.clipboardSet", (args, _) =>
        {
            var caminhos = StrArray(args, "caminhos");
            var collection = new System.Collections.Specialized.StringCollection();
            collection.AddRange(caminhos.ToArray());
            System.Windows.Forms.Clipboard.SetFileDropList(collection);
            return Task.FromResult<object?>(new { ok = true });
        });

        // ---------- projetos ----------

        Add("project.detect", async (args, _) =>
        {
            var raizes = StrArray(args, "raizes");
            if (raizes.Count == 0)
            {
                raizes = host.Settings.Raizes;
            }

            var profundidade = Int(args, "profundidade", 2);
            var projetos = await Task.Run(
                () => host.Projects.Detect(raizes, profundidade)).ConfigureAwait(false);

            return projetos;
        });

        Add("project.info", (args, _) => Task.FromResult<object?>(
            host.Projects.TryRead(Str(args, "caminho"))));

        // Deteccao a partir da pasta atual: sobe pelos pais ate achar o marcador.
        Add("project.resolve", (args, _) =>
        {
            var encontrado = host.Projects.Resolve(Str(args, "caminho"));
            if (encontrado is not null)
            {
                RegistrarRecente(host, encontrado.Caminho);
            }

            return Task.FromResult<object?>(encontrado);
        });

        Add("project.recentes", (_, _) => Task.FromResult<object?>(Recentes(host)));

        Add("project.shortcuts", (args, _) => Task.FromResult<object?>(
            host.Projects.Shortcuts(Str(args, "raiz"))));

        Add("project.esquecerRecente", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            host.Settings.ProjetosRecentes.RemoveAll(p => string.Equals(p, caminho, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("project.register", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            var marcador = host.Projects.Register(
                caminho,
                NullableStr(args, "nome"),
                NullableStr(args, "tipo"),
                NullableStr(args, "cliente"));

            RegistrarRecente(host, caminho);
            return Task.FromResult<object?>(marcador);
        });

        Add("template.get", (_, _) => Task.FromResult<object?>(new
        {
            arvore = host.Template,
            caminhos = TemplateCatalog.Flatten(host.Template),
            token = TemplateCatalog.QuadroToken,
            arquivo = AppPaths.TemplateFile,
        }));

        Add("project.materialize", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            var selecionadas = StrArray(args, "selecionadas");
            var quadros = StrArray(args, "quadros");
            var resultado = TemplateMaterializer.Materialize(caminho, selecionadas, quadros);
            return Task.FromResult<object?>(resultado);
        });

        // ---------- busca ----------

        Add("search.start", async (args, ct) =>
        {
            var raiz = Str(args, "raiz");
            var texto = Str(args, "texto");
            var opId = Str(args, "opId");
            var query = SearchQuery.Parse(texto);

            if (query.Vazia)
            {
                return new { total = 0, pastas = 0 };
            }

            var encontrados = 0;
            var totalPastas = 0;

            await host.Search.SearchAsync(
                raiz,
                query,
                new SearchOptions
                {
                    Paralelismo = host.Settings.ParalelismoBusca,
                    MostrarOcultos = host.Settings.MostrarOcultos,
                },
                batch =>
                {
                    encontrados += batch.Count;
                    host.Emit("search.batch", new { opId, itens = batch, total = encontrados });
                    return Task.CompletedTask;
                },
                pastas =>
                {
                    totalPastas = pastas;
                    host.Emit("search.progress", new { opId, pastas });
                    return Task.CompletedTask;
                },
                ct).ConfigureAwait(false);

            host.Emit("search.done", new { opId, total = encontrados });
            return new { total = encontrados, pastas = totalPastas };
        }, operacao: true);

        Add("search.cancel", (args, _) =>
        {
            host.Cancels.Cancel(Str(args, "opId"));
            return Task.FromResult<object?>(new { ok = true });
        });

        // ---------- miniaturas ----------

        Add("thumb.request", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var tamanho = Long(args, "tamanho", 0);
            var modificado = Date(args, "modificado");
            var size = Int(args, "size", host.Settings.TamanhoMiniatura);

            var resultado = await host.Thumbs
                .RequestAsync(caminho, tamanho, modificado, size, ct)
                .ConfigureAwait(false);

            return new
            {
                chave = resultado.Chave,
                pronta = resultado.Pronta,
                url = resultado.Pronta ? ThumbnailService.UrlFor(resultado.Chave) : null,
            };
        }, operacao: true);

        // ---------- midia ----------

        Add("media.probe", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var info = await host.Probe.InspectAsync(caminho, ct).ConfigureAwait(false);
            if (info is null)
            {
                return null;
            }

            var pasta = Path.GetDirectoryName(caminho) ?? caminho;
            var (livre, _) = Win32.FreeSpace(pasta);
            var perfilSolicitado = Str(args, "perfil", host.Settings.PerfilVideoPadrao);
            var plano = VideoPlanBuilder.Choose(info, perfilSolicitado, livre);

            return new
            {
                info = new
                {
                    info.Caminho,
                    info.Largura,
                    info.Altura,
                    info.Fps,
                    info.Codec,
                    info.PixFmt,
                    info.TemAlpha,
                    info.TemAudio,
                    info.FaixasAudio,
                    info.DuracaoSegundos,
                    info.Hdr,
                    info.DolbyVision,
                    info.Vfr,
                    info.DezBits,
                    info.BitrateVideoMbps,
                    duracaoTexto = TimeSpan.FromSeconds(info.DuracaoSegundos).ToString(@"hh\:mm\:ss"),
                },
                plano = new
                {
                    plano.Perfil,
                    plano.NomeExibicao,
                    plano.PixFmt,
                    plano.ComAlpha,
                    plano.OrigemMbps,
                    plano.AlvoMbps,
                    plano.Motivo,
                    plano.TamanhoEstimadoBytes,
                    estimadoTexto = VideoPlanBuilder.FormatBytes(plano.TamanhoEstimadoBytes),
                    avisos = plano.Avisos,
                },
                jaEditavel = PasteService.EhCodecDeEdicao(info.Codec),
                estimativas = VideoPlanBuilder.Estimates(info),
            };
        }, operacao: true);

        // Player embutido: o ffmpeg decodifica e entrega o fluxo H.264 direto no <video>.
        // Nada e gravado: abrir e instantaneo, e o scrub reabre o fluxo no segundo pedido.
        Add("media.play", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var inicio = Num(args, "inicio", 0);

            if (!File.Exists(caminho))
            {
                return new { ok = false, erro = "Arquivo nao encontrado." };
            }

            var info = await host.Probe.InspectAsync(caminho, ct).ConfigureAwait(false);
            if (info is null)
            {
                return new { ok = false, erro = "Nao foi possivel ler o video (ffmpeg ausente?)." };
            }

            var fluxo = host.Previa.Abrir(info, inicio);
            if (fluxo.Erro is not null || fluxo.Saida is null)
            {
                return new { ok = false, erro = fluxo.Erro ?? "Falha ao iniciar o player." };
            }

            host.Player.GarantirUrlBase();

            // A janela fecha -> o processo morre junto.
            host.JanelaDoContexto?.AoFechar(() => host.Previa.Fechar(fluxo.Id));

            return new
            {
                ok = true,
                id = fluxo.Id,
                url = host.Player.UrlDoFluxo(fluxo.Id),
                inicio,
                duracao = info.DuracaoSegundos,
                encoder = fluxo.Encoder,
            };
        }, operacao: true);

        Add("media.playFechar", (args, _) =>
        {
            host.Previa.Fechar(Str(args, "id"));
            return Task.FromResult<object?>(new { ok = true });
        });

        // Colar = transcodificar no destino. O original nunca e copiado para o projeto.
        Add("media.paste", async (args, ct) =>
        {
            host.Conflicts.Reset();
            var origens = StrArray(args, "origens");
            var destino = Str(args, "destino");
            var opId = Str(args, "opId");

            var resultado = await host.Paste.PasteAsync(
                origens,
                destino,
                opId,
                evt =>
                {
                    host.Emit("media.event", evt);
                    return Task.CompletedTask;
                },
                ct).ConfigureAwait(false);

            return new
            {
                convertidos = resultado.Convertidos,
                copiados = resultado.Copiados,
                erros = resultado.Erros,
                cancelado = resultado.Cancelado,
            };
        }, operacao: true);

        Add("media.cancel", (args, _) =>
        {
            host.Cancels.Cancel(Str(args, "opId"));
            host.Conflicts.CancelAll();
            return Task.FromResult<object?>(new { ok = true });
        });

        // Conversao explicita (menu de contexto): usa o perfil escolhido no dialogo.
        Add("media.convert", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var perfil = Str(args, "perfil", host.Settings.PerfilVideoPadrao);
            var destino = Str(args, "destino", Path.GetDirectoryName(caminho) ?? caminho);
            var opId = Str(args, "opId");

            var info = await host.Probe.InspectAsync(caminho, ct).ConfigureAwait(false);
            if (info is null)
            {
                return new { ok = false, erro = "Nao foi possivel ler o video (ffprobe ausente?)." };
            }

            var (livre, _) = Win32.FreeSpace(destino);
            var plano = VideoPlanBuilder.Choose(info, perfil, livre);

            var resultado = await host.Transcode.ConvertAsync(
                info,
                plano,
                destino,
                opId,
                progresso =>
                {
                    host.Emit("media.event", new
                    {
                        Tipo = "convertendo",
                        OperacaoId = opId,
                        Arquivo = Path.GetFileName(caminho),
                        Percentual = progresso.Percentual,
                        Mensagem = progresso.Velocidade,
                        Perfil = plano.NomeExibicao,
                    });
                    return Task.CompletedTask;
                },
                ct).ConfigureAwait(false);

            return new
            {
                ok = resultado.Ok,
                saida = resultado.Saida,
                erro = resultado.Erro,
                perfil = plano.NomeExibicao,
            };
        }, operacao: true);

        // ---------- atualizacoes ----------

        Add("updates.check", async (args, _) =>
            await host.Updates.CheckAsync(Bool(args, "baixar")).ConfigureAwait(false));

        Add("updates.apply", async (_, _) => await host.Updates.ApplyAsync().ConfigureAwait(false));

        return routes;
    }

    private static object ResultadoOperacao(OperationResult resultado) => new
    {
        copiados = resultado.Copiados,
        movidos = resultado.Movidos,
        ignorados = resultado.Ignorados,
        erros = resultado.Erros,
        cancelado = resultado.Cancelado,
    };

    private static void RegistrarRecente(AppHost host, string caminho)
    {
        var lista = host.Settings.ProjetosRecentes;
        lista.RemoveAll(p => string.Equals(p, caminho, StringComparison.OrdinalIgnoreCase));
        lista.Insert(0, caminho);

        if (lista.Count > 12)
        {
            lista.RemoveRange(12, lista.Count - 12);
        }
    }

    private static List<DetectedProject> Recentes(AppHost host)
    {
        var lista = new List<DetectedProject>();

        foreach (var caminho in host.Settings.ProjetosRecentes.ToList())
        {
            if (!Directory.Exists(caminho))
            {
                continue;
            }

            var marcador = host.Projects.TryRead(caminho);
            if (marcador is null)
            {
                continue;
            }

            lista.Add(new DetectedProject
            {
                Caminho = caminho,
                Nome = marcador.Nome,
                Origem = "marcador",
                Marcador = marcador,
            });
        }

        return lista;
    }

    private static List<object> Roots(AppHost host)
    {
        var roots = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string nome, string caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho) || !seen.Add(caminho))
            {
                return;
            }

            var (livre, total) = Win32.FreeSpace(caminho);
            roots.Add(new
            {
                nome,
                caminho,
                livre,
                total,
                livreTexto = VideoPlanBuilder.FormatBytes(livre),
            });
        }

        foreach (var raiz in host.Settings.Raizes)
        {
            Add(Path.GetFileName(raiz.TrimEnd(Path.DirectorySeparatorChar)), raiz);
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady)
                {
                    Add(string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : drive.VolumeLabel, drive.RootDirectory.FullName);
                }
            }
            catch
            {
                // drives sem acesso (ex.: leitor vazio)
            }
        }

        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("Pessoal", perfil);

        return roots;
    }

    private static string Str(JsonElement args, string name, string def = "")
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var el))
        {
            return def;
        }

        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? def : def;
    }

    private static string? NullableStr(JsonElement args, string name)
    {
        var value = Str(args, name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static bool Bool(JsonElement args, string name, bool def = false)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var el))
        {
            return def;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => def,
        };
    }

    private static int Int(JsonElement args, string name, int def)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var el) && el.TryGetInt32(out var v))
        {
            return v;
        }

        return def;
    }

    private static long Long(JsonElement args, string name, long def)
    {
        if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var el) && el.TryGetInt64(out var v))
        {
            return v;
        }

        return def;
    }

    private static double Num(JsonElement args, string name, double def)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.Number
            && el.TryGetDouble(out var v))
        {
            return v;
        }

        return def;
    }

    private static DateTime Date(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var el)
            && el.ValueKind == JsonValueKind.String
            && DateTime.TryParse(el.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var value))
        {
            return value;
        }

        return DateTime.MinValue;
    }

    private static List<string> StrArray(JsonElement args, string name)
    {
        var list = new List<string>();
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var el)
            || el.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } s)
            {
                list.Add(s);
            }
        }

        return list;
    }
}
