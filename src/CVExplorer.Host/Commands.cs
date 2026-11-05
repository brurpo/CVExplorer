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
                // O menu só oferece DXV com alpha quando o encoder suporta DXT5.
                dxvAlpha = host.Ffmpeg.Ffmpeg is not null && CodecCapabilities.DxvAlpha(host.Ffmpeg.Ffmpeg),
                // Qual desentrelaçador a conversão vai usar (nnedi > bwdif > yadif).
                desentrelacador = host.Ffmpeg.Ffmpeg is null
                    ? null
                    : CodecCapabilities.Desentrelacador(host.Ffmpeg.Ffmpeg),
            },
            repositorioConfigurado = AppInfo.RepositorioConfigurado,
            repositorio = AppInfo.RepoUrl,
            atualizacao = host.Updates.Estado(),
            raizes = host.Settings.Raizes,
            janela = host.JanelaDoContexto?.Id,
            // Janela de relatório (pastas maiores) em vez do explorador.
            relatorio = host.JanelaDoContexto?.RelatorioPasta,
            // Janela de prévia rápida (barra de espaço).
            previa = host.JanelaDoContexto?.PreviaArquivo,
            // Abas que vieram junto da janela (arraste de abas para fora).
            abasIniciais = host.JanelaDoContexto?.AbasIniciais ?? new List<string>(),
        }));

        Add("settings.get", (_, _) => Task.FromResult<object?>(host.Settings));

        // ---------- barra de titulo propria ----------

        Add("app.janelaMinimizar", (args, _) =>
        {
            JanelaDoContexto(host, args)?.MinimizarJanela();
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("app.janelaMaximizar", (args, _) =>
        {
            var janela = JanelaDoContexto(host, args);
            janela?.AlternarMaximizado();
            return Task.FromResult<object?>(new
            {
                ok = janela is not null,
                maximizada = janela?.WindowState == FormWindowState.Maximized,
            });
        });

        Add("app.janelaFechar", (args, _) =>
        {
            var janela = JanelaDoContexto(host, args);
            janela?.BeginInvoke(janela.Close);
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("app.janelaArrastar", (args, _) =>
        {
            JanelaDoContexto(host, args)?.ArrastarJanela();
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("abas.arrastarIniciar", (args, _) =>
        {
            host.Abas.Iniciar(host.JanelaDoContexto, StrArray(args, "abas"));
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("abas.soltar", (_, _) =>
        {
            host.Abas.Cancelar();
            return Task.FromResult<object?>(new { ok = true });
        });

        Add("abas.cancelar", (_, _) =>
        {
            host.Abas.Cancelar();
            return Task.FromResult<object?>(new { ok = true });
        });

        // A janela arrastada manda as abas para a janela de destino e se fecha
        // (arrastar a barra de título sobre a barra de abas de outra janela).
        Add("abas.fundir", (args, _) =>
        {
            var origem = host.JanelaDoContexto;
            var destino = host.Janelas.FirstOrDefault(j => j.Id == NullableStr(args, "destino"));
            var abas = StrArray(args, "abas");

            if (destino is not null && destino.Id != origem?.Id && abas.Count > 0)
            {
                destino.Enviar("abas.adotar", new { abas });

                if (origem is not null)
                {
                    origem.BeginInvoke(origem.FecharSilencioso);
                }
            }

            return Task.FromResult<object?>(new { ok = true });
        });

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

            // Dentro de um compactado o caminho não existe no disco: não vale como "última pasta".
            if (ArchiveService.Separar(caminho) is not null)
            {
                return Task.FromResult<object?>(new { ok = true, ignorado = true });
            }

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
            var relatorio = Str(args, "relatorio");
            var previa = Str(args, "previa");
            var janela = host.AbrirJanela(
                string.IsNullOrWhiteSpace(caminho) ? host.JanelaDoContexto?.PastaAtual : caminho,
                primaria: false,
                relatorio: string.IsNullOrWhiteSpace(relatorio) ? null : relatorio,
                previa: string.IsNullOrWhiteSpace(previa) ? null : previa);
            return Task.FromResult<object?>(new { ok = true, janela = janela.Id });
        });

        // Prévia rápida (barra de espaço): reaproveita a janela já aberta, se houver,
        // em vez de encher a tela de janelinhas.
        Add("app.previaRapida", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            if (string.IsNullOrWhiteSpace(caminho))
            {
                return Task.FromResult<object?>(new { ok = false });
            }

            var existente = host.Janelas.FirstOrDefault(j => !j.IsDisposed && j.PreviaArquivo is not null);
            if (existente is not null)
            {
                existente.Enviar("previa.mostrar", new { caminho });
                existente.Activate();
                return Task.FromResult<object?>(new { ok = true, janela = existente.Id, reutilizada = true });
            }

            var janela = host.AbrirJanela(caminho, primaria: false, previa: caminho, tamanho: new Size(900, 640));
            return Task.FromResult<object?>(new { ok = true, janela = janela.Id, reutilizada = false });
        });

        // URL loopback do arquivo cru (o three.js lê o modelo direto dela).
        Add("fs.arquivoUrl", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            return Task.FromResult<object?>(new
            {
                ok = true,
                url = host.Player.RegistrarArquivo(caminho),
                nome = Path.GetFileName(caminho),
            });
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

            // Dentro de um arquivo compactado o "diretório" é o próprio arquivo.
            if (ArchiveService.Separar(caminho) is { } dentro)
            {
                var entradas = await Task.Run(
                    () => host.Archives.Listar(dentro.Arquivo, dentro.Interno),
                    ct).ConfigureAwait(false);

                entradas = entradas
                    .Where(e => ocultos || !e.Nome.StartsWith('.'))
                    .ToList();

                foreach (var lote in entradas.Chunk(DirectoryLister.BatchSize))
                {
                    host.Emit("dir.batch", new { opId, itens = lote });
                }

                host.Emit("dir.done", new { opId, total = entradas.Count });
                return new { total = entradas.Count };
            }

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

            // Dentro de um compactado não existe "espaço livre" do item.
            if (ArchiveService.Separar(caminho) is { } dentro)
            {
                var (livreArquivo, totalArquivo) = Win32.FreeSpace(dentro.Arquivo);
                return Task.FromResult<object?>(new
                {
                    livre = livreArquivo,
                    total = totalArquivo,
                    livreTexto = VideoPlanBuilder.FormatBytes(livreArquivo),
                });
            }

            var (livre, total) = Win32.FreeSpace(caminho);
            return Task.FromResult<object?>(new
            {
                livre,
                total,
                livreTexto = VideoPlanBuilder.FormatBytes(livre),
            });
        });

        // Extrai o que está selecionado dentro do compactado para uma pasta do disco.
        Add("fs.extrair", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var destino = Str(args, "destino");
            var nomePasta = Str(args, "nomePasta");
            var nomes = StrArray(args, "caminhos");

            if (ArchiveService.Separar(caminho) is not { } dentro)
            {
                return new { ok = false, erro = "Não é um arquivo compactado." };
            }

            // "Extrair aqui" usa a pasta que contém o arquivo; "extrair em pasta nova"
            // cria a pasta com o nome que o usuário digitou, ao lado do arquivo.
            var baseDestino = Path.GetDirectoryName(dentro.Arquivo) ?? AppPaths.Root;
            var pastaDestino = !string.IsNullOrWhiteSpace(destino)
                ? destino
                : string.IsNullOrWhiteSpace(nomePasta)
                    ? baseDestino
                    : Path.Combine(baseDestino, SanearNome(nomePasta));

            var internos = nomes
                .Select(n => ArchiveService.Separar(n) is { } alvo ? alvo.Interno : string.Empty)
                .Where(n => n.Length > 0)
                .ToList();

            if (internos.Count == 0)
            {
                internos.Add(dentro.Interno);
            }

            var extraidos = await Task.Run(
                () => host.Archives.Extrair(dentro.Arquivo, internos, pastaDestino),
                ct).ConfigureAwait(false);

            return new { ok = true, total = extraidos.Count, destino = pastaDestino, itens = extraidos };
        }, operacao: true);

        // Copiar de dentro de um compactado: extrai para um temporário e devolve os
        // caminhos reais — o resto do app (colar, arrastar, converter) segue igual.
        Add("fs.extrairTemporario", async (args, ct) =>
        {
            var caminhos = StrArray(args, "caminhos");
            var reais = new List<string>();

            foreach (var caminho in caminhos)
            {
                if (ArchiveService.Separar(caminho) is not { } dentro)
                {
                    reais.Add(caminho);
                    continue;
                }

                var extraidos = await Task.Run(
                    () => host.Archives.ExtrairParaTemporario(dentro.Arquivo, new[] { dentro.Interno }),
                    ct).ConfigureAwait(false);

                reais.AddRange(extraidos);
            }

            return new { ok = true, caminhos = reais };
        }, operacao: true);

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
            NegarCompactado(destino);
            var resultado = await host.Files.CopyAsync(origens, destino, opId, ct).ConfigureAwait(false);
            return ResultadoOperacao(resultado);
        }, operacao: true);

        Add("fs.move", async (args, ct) =>
        {
            host.Conflicts.Reset();
            var origens = StrArray(args, "origens");
            var destino = Str(args, "destino");
            var opId = Str(args, "opId");
            NegarCompactado(destino);
            var resultado = await host.Files.MoveAsync(origens, destino, opId, ct).ConfigureAwait(false);
            return ResultadoOperacao(resultado);
        }, operacao: true);

        Add("fs.rename", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            NegarCompactado(caminho);
            var renomeado = FileOperations.Rename(caminho, Str(args, "novoNome"));
            return Task.FromResult<object?>(new { caminho = renomeado });
        });

        // Relatório "pastas maiores": soma o tamanho de cada subpasta direta, em paralelo.
        Add("fs.pastasMaiores", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho))
            {
                return new { ok = false, erro = "Pasta nao encontrada.", pastas = Array.Empty<object>() };
            }

            string[] subpastas;
            try
            {
                subpastas = Directory.GetDirectories(caminho);
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao listar {caminho}", ex);
                return new { ok = false, erro = ex.Message, pastas = Array.Empty<object>() };
            }

            var medidas = new List<(string Nome, string Caminho, long Bytes, int Arquivos)>();
            var portao = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount / 2, 2, 8));
            var trava = new object();

            var tarefas = subpastas.Select(async pasta =>
            {
                await portao.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var (bytes, arquivos) = await Task.Run(() => Medir(pasta, ct), ct).ConfigureAwait(false);
                    lock (trava)
                    {
                        medidas.Add((Path.GetFileName(pasta), pasta, bytes, arquivos));
                    }
                }
                catch (OperationCanceledException)
                {
                    // cancelado pelo usuário: devolve o que já mediu
                }
                catch (Exception ex)
                {
                    Log.Error($"Falha ao medir {pasta}", ex);
                }
                finally
                {
                    portao.Release();
                }
            });

            await Task.WhenAll(tarefas).ConfigureAwait(false);

            var lista = medidas
                .OrderByDescending(m => m.Bytes)
                .Select(m => new { nome = m.Nome, caminho = m.Caminho, bytes = m.Bytes, arquivos = m.Arquivos })
                .ToList();

            return new
            {
                ok = true,
                caminho,
                totalBytes = medidas.Sum(m => m.Bytes),
                arquivos = medidas.Sum(m => m.Arquivos),
                pastas = lista,
            };
        }, operacao: true);

        static (long Bytes, int Arquivos) Medir(string pasta, CancellationToken ct)
        {
            long bytes = 0;
            var arquivos = 0;

            var opcoes = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            try
            {
                foreach (var arquivo in new DirectoryInfo(pasta).EnumerateFiles("*", opcoes))
                {
                    ct.ThrowIfCancellationRequested();
                    bytes += arquivo.Length;
                    arquivos++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // pasta sem permissão no meio do caminho: fica com o que já somou
            }

            return (bytes, arquivos);
        }

        Add("fs.mkdir", (args, _) =>
        {
            var pai = Str(args, "pai");
            NegarCompactado(pai);
            var caminho = FileOperations.CreateFolder(pai, Str(args, "nome", "Nova pasta"));
            return Task.FromResult<object?>(new { caminho });
        });

        Add("fs.delete", (args, _) =>
        {
            var caminhos = StrArray(args, "caminhos");
            foreach (var caminho in caminhos)
            {
                NegarCompactado(caminho);
            }

            FileOperations.DeleteToRecycleBin(caminhos);
            return Task.FromResult<object?>(new { ok = true, excluidos = caminhos.Count });
        });

        // Solta tudo o que estiver a ler estes arquivos: prévia (ffmpeg), miniaturas e
        // mídias registradas no servidor do player. Sem isso o Windows recusa mover/apagar.
        Add("fs.liberar", (args, _) =>
        {
            var caminhos = StrArray(args, "caminhos");
            host.Previa.FecharPorCaminho(caminhos);
            host.Thumbs.Liberar(caminhos);
            host.Player.LiberarPorCaminho(caminhos);
            return Task.FromResult<object?>(new { ok = true });
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

        // Pastas do modelo que fazem sentido dentro desta pasta (menu "Novo").
        // Na raiz de um projeto (tem .cvproj) são as pastas de primeiro nível;
        // nas demais casa pelo nome da pasta ou, quando ela é um quadro, pelo token {QUADRO}.
        Add("project.filhosModelo", (args, _) =>
        {
            var caminho = Str(args, "caminho");
            var temQuadro = TemplateCatalog.QuadroNode(host.Template) is not null;
            if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho))
            {
                return Task.FromResult<object?>(new { filhos = Array.Empty<object>(), quadro = temQuadro });
            }

            var modelo = host.Template;
            var raiz = caminho.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var nome = Path.GetFileName(raiz);
            var pai = Path.GetDirectoryName(raiz);
            var nomePai = string.IsNullOrEmpty(pai) ? null : Path.GetFileName(pai);
            var raizDeProjeto = File.Exists(Path.Combine(raiz, ProjectMarker.FileName));

            var no = TemplateCatalog.FilhosPara(modelo, nome, nomePai, raizDeProjeto);

            if (no.Count == 0)
            {
                return Task.FromResult<object?>(new { filhos = Array.Empty<object>(), quadro = temQuadro });
            }

            var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var pasta in Directory.EnumerateDirectories(caminho))
                {
                    existentes.Add(Path.GetFileName(pasta));
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao listar pastas de {caminho}", ex);
            }

            var filhos = no
                .Where(f => !f.Nome.Equals(TemplateCatalog.QuadroToken, StringComparison.OrdinalIgnoreCase))
                .Select(f => new
                {
                    nome = f.Nome,
                    existe = existentes.Contains(f.Nome),
                    temFilhos = f.Filhos.Count > 0,
                })
                .ToList();

            return Task.FromResult<object?>(new { origem = nome, raiz = raizDeProjeto, quadro = temQuadro, filhos });
        });

        // Cria uma pasta de quadro em qualquer lugar, com a estrutura que o modelo define dentro.
        Add("project.criarQuadro", (args, _) =>
        {
            var pai = Str(args, "caminho");
            if (string.IsNullOrWhiteSpace(pai) || !Directory.Exists(pai))
            {
                return Task.FromResult<object?>(new { ok = false, erro = "Pasta nao encontrada." });
            }

            var resultado = TemplateMaterializer.CreateQuadro(
                pai, Str(args, "nome"), StrArray(args, "selecionadas"));
            var caminho = Path.Combine(pai, TemplateMaterializer.SanitizeSegment(Str(args, "nome")));

            return Task.FromResult<object?>(new
            {
                ok = resultado.Erros.Count == 0,
                caminho,
                criadas = resultado.Criadas,
                jaExistiam = resultado.JaExistiam,
                erros = resultado.Erros,
            });
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
                renderizar = resultado.Renderizar,
                arquivoUrl = resultado.Renderizar is null ? null : host.Player.RegistrarArquivo(caminho),
            };
        }, operacao: true);

        // A interface renderizou o modelo 3D: grava o WebP no mesmo cache das outras miniaturas.
        Add("thumb.salvar", (args, _) =>
        {
            var chave = Str(args, "chave");
            var dados = Str(args, "dados");

            if (chave.Length != 20 || !chave.All(Uri.IsHexDigit))
            {
                return Task.FromResult<object?>(new { ok = false, erro = "chave invalida" });
            }

            const string prefixo = "data:image/webp;base64,";
            if (!dados.StartsWith(prefixo, StringComparison.Ordinal))
            {
                return Task.FromResult<object?>(new { ok = false, erro = "formato invalido" });
            }

            try
            {
                var bytes = Convert.FromBase64String(dados[prefixo.Length..]);
                if (bytes.Length is < 100 or > 8 * 1024 * 1024)
                {
                    return Task.FromResult<object?>(new { ok = false, erro = "tamanho invalido" });
                }

                var destino = Path.Combine(AppPaths.ThumbsDir, chave + ".webp");
                File.WriteAllBytes(destino, bytes);
                return Task.FromResult<object?>(new { ok = true, url = ThumbnailService.UrlFor(chave) });
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao salvar miniatura 3D", ex);
                return Task.FromResult<object?>(new { ok = false, erro = ex.Message });
            }
        });

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
                    info.Interlaced,
                    info.FieldOrder,
                    duracaoTexto = TimeSpan.FromSeconds(info.DuracaoSegundos).ToString(@"hh\:mm\:ss"),
                },
                plano = new
                {
                    plano.Perfil,
                    plano.NomeExibicao,
                    plano.PixFmt,
                    plano.ComAlpha,
                    plano.Desentrelacar,
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

        // Player embutido: toca o arquivo original quando o WebView2 decodifica o codec
        // (seek instantâneo, sem processo nenhum); nos codecs de edição o ffmpeg entrega
        // o fluxo H.264 sob demanda.
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

            if (ReproducaoDireta.Suportado(info))
            {
                var urlMidia = host.Player.RegistrarMidia(caminho, out var midiaId);
                return new
                {
                    ok = true,
                    nativo = true,
                    midiaId,
                    url = urlMidia,
                    inicio = 0,
                    duracao = info.DuracaoSegundos,
                    encoder = (string?)null,
                };
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
                nativo = false,
                midiaId = (string?)null,
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
            host.Player.Liberar(Str(args, "midiaId"));
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

        // Sequencia de imagens: qual e, quantos quadros, e o plano de conversao.
        Add("media.sequencia", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var fps = Num(args, "fps", 24);

            var sequencia = ImageSequence.Detectar(caminho);
            if (sequencia is null)
            {
                return null;
            }

            var primeiro = await host.Probe.InspectAsync(caminho, ct).ConfigureAwait(false);
            if (primeiro is null)
            {
                return null;
            }

            var info = sequencia.ComoVideo(primeiro, (int)Math.Round(fps));
            var pasta = Path.GetDirectoryName(caminho) ?? caminho;
            var (livre, _) = Win32.FreeSpace(pasta);
            var plano = VideoPlanBuilder.Choose(info, Str(args, "perfil", "auto"), livre);

            return new
            {
                sequencia = new
                {
                    nomeBase = sequencia.NomeBase,
                    extensao = sequencia.Extensao,
                    quadros = sequencia.Quadros,
                    primeiro = sequencia.Primeiro,
                    ultimo = sequencia.Ultimo,
                    digitos = sequencia.Digitos,
                    comFalhas = sequencia.ComFalhas,
                    bytes = sequencia.Bytes,
                },
                info = new
                {
                    info.Largura,
                    info.Altura,
                    info.PixFmt,
                    info.TemAlpha,
                    info.DezBits,
                    info.Fps,
                    duracaoSegundos = info.DuracaoSegundos,
                    duracaoTexto = TimeSpan.FromSeconds(info.DuracaoSegundos)
                        .ToString(info.DuracaoSegundos < 3600 ? @"mm\:ss\.ff" : @"hh\:mm\:ss"),
                },
                plano = new
                {
                    plano.Perfil,
                    plano.NomeExibicao,
                    plano.PixFmt,
                    plano.ComAlpha,
                    plano.AlvoMbps,
                    plano.Motivo,
                    plano.TamanhoEstimadoBytes,
                    estimadoTexto = VideoPlanBuilder.FormatBytes(plano.TamanhoEstimadoBytes),
                    avisos = plano.Avisos,
                },
                estimativas = VideoPlanBuilder.Estimates(info),
            };
        }, operacao: true);

        Add("media.convertSequencia", async (args, ct) =>
        {
            var caminho = Str(args, "caminho");
            var perfil = Str(args, "perfil", "auto");
            var fps = (int)Math.Round(Num(args, "fps", 24));
            var destino = Str(args, "destino", Path.GetDirectoryName(caminho) ?? caminho);
            var opId = Str(args, "opId");

            var sequencia = ImageSequence.Detectar(caminho);
            if (sequencia is null)
            {
                return new { ok = false, erro = "Nao encontrei mais quadros desta sequencia." };
            }

            var primeiro = await host.Probe.InspectAsync(caminho, ct).ConfigureAwait(false);
            if (primeiro is null)
            {
                return new { ok = false, erro = "Nao foi possivel ler o primeiro quadro (ffprobe ausente?)." };
            }

            var info = sequencia.ComoVideo(primeiro, Math.Max(fps, 1));
            var (livre, _) = Win32.FreeSpace(destino);
            var plano = VideoPlanBuilder.Choose(info, perfil, livre);

            var resultado = await host.Transcode.ConvertSequenceAsync(
                info,
                sequencia,
                plano,
                info.Fps > 0 ? (int)Math.Round(info.Fps) : 24,
                destino,
                opId,
                progresso =>
                {
                    host.Emit("media.event", new
                    {
                        Tipo = "convertendo",
                        OperacaoId = opId,
                        Arquivo = $"{sequencia.NomeBase} ({sequencia.Quadros} quadros)",
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

        void Add(string nome, string caminho, string tipo = "local", string? letra = null, string? icone = null)
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
                letra,
                tipo,
                icone,
                livre,
                total,
                livreTexto = VideoPlanBuilder.FormatBytes(livre),
            });
        }

        // Pastas do perfil vêm primeiro: é onde o dia de trabalho começa.
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Add("Pessoal", perfil, "especial", icone: "pessoal");

        void Especial(string nome, Environment.SpecialFolder pasta, string icone)
        {
            var caminho = Environment.GetFolderPath(pasta);
            if (!string.IsNullOrEmpty(caminho) && Directory.Exists(caminho))
            {
                Add(nome, caminho, "especial", icone: icone);
            }
        }

        Especial("Área de Trabalho", Environment.SpecialFolder.Desktop, "desktop");
        Especial("Documentos", Environment.SpecialFolder.MyDocuments, "documentos");

        var downloads = Path.Combine(perfil, "Downloads");
        if (Directory.Exists(downloads))
        {
            Add("Downloads", downloads, "especial", icone: "downloads");
        }

        Especial("Imagens", Environment.SpecialFolder.MyPictures, "imagens");
        Especial("Vídeos", Environment.SpecialFolder.MyVideos, "videos");

        foreach (var raiz in host.Settings.Raizes)
        {
            Add(Path.GetFileName(raiz.TrimEnd(Path.DirectorySeparatorChar)), raiz);
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady)
                {
                    continue;
                }

                var tipo = drive.DriveType switch
                {
                    DriveType.Network => "rede",
                    DriveType.Removable => "removivel",
                    DriveType.CDRom => "cd",
                    DriveType.Fixed => "local",
                    _ => "local",
                };

                // "T:\" -> letra T; volume sem rotulo usa a letra como nome.
                var letra = drive.Name.TrimEnd('\\', ':');
                var rotulo = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? letra : drive.VolumeLabel;
                Add(rotulo, drive.RootDirectory.FullName, tipo, letra);
            }
            catch
            {
                // drives sem acesso (ex.: leitor vazio)
            }
        }

        return roots;
    }

    /// <summary>Dentro de um arquivo compactado nada é gravado: só listar, copiar e extrair.</summary>
    private static void NegarCompactado(string caminho)
    {
        if (ArchiveService.Separar(caminho) is not null)
        {
            throw new InvalidOperationException(
                "Esta pasta está dentro de um arquivo compactado. Use \"Extrair aqui\" para trazer os itens para o disco.");
        }
    }

    /// <summary>Nome de pasta digitado pelo usuário: sem caracteres que o Windows recusa.</summary>
    private static string SanearNome(string nome)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpo = new string(nome.Trim().Select(c => invalidos.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(limpo) ? "extraido" : limpo;
    }

    /// <summary>Janela que fez a chamada (o id vem da interface; sem ele, a janela ativa).</summary>
    private static Janela? JanelaDoContexto(AppHost host, JsonElement args)
    {
        var id = Str(args, "janela");
        if (!string.IsNullOrEmpty(id))
        {
            var porId = host.Janelas.FirstOrDefault(j => !j.IsDisposed && j.Id == id);
            if (porId is not null)
            {
                return porId;
            }
        }

        return host.JanelaDoContexto ?? host.JanelaAtiva;
    }

    private static string Str(JsonElement args, string name, string def = "")    {
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
