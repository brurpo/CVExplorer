using System.Collections.Concurrent;
using System.Threading.Channels;
using CVExplorer.Host.Features.FileSystem;

namespace CVExplorer.Host.Features.Search;

public sealed class SearchOptions
{
    public int Paralelismo { get; set; } = 8;

    public int MaxResultados { get; set; } = 3000;

    public bool MostrarOcultos { get; set; }
}

/// <summary>
/// Busca paralela: N workers consomem uma fila de pastas. Isso e o que importa em
/// rede (SMB), onde a latencia domina — mais threads ate o ponto de saturacao da share.
/// </summary>
public sealed class SearchService
{
    private static readonly HashSet<string> Ignorar = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", "$RECYCLE.BIN", "System Volume Information", "node_modules",
        ".vs", "obj", "bin", ".tools",
    };

    public async Task SearchAsync(
        string root,
        SearchQuery query,
        SearchOptions options,
        Func<IReadOnlyList<FileEntry>, Task> onBatch,
        Func<int, Task> onProgress,
        CancellationToken ct)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Pasta nao encontrada: {root}");
        }

        var parallelism = Math.Clamp(options.Paralelismo, 1, 32);
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = false,
        });

        var batch = new ConcurrentQueue<FileEntry>();
        var pendentes = 1;
        var pastasVisitadas = 0;
        var encontrados = 0;
        var lastFlush = Environment.TickCount64;

        var optionsEnum = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = options.MostrarOcultos
                ? FileAttributes.System
                : FileAttributes.System | FileAttributes.Hidden,
        };

        channel.Writer.TryWrite(root);

        async Task FlushAsync(bool force)
        {
            if (batch.IsEmpty)
            {
                return;
            }

            if (!force && batch.Count < 120 && Environment.TickCount64 - lastFlush < 150)
            {
                return;
            }

            var items = new List<FileEntry>();
            while (items.Count < 400 && batch.TryDequeue(out var item))
            {
                items.Add(item);
            }

            if (items.Count > 0)
            {
                lastFlush = Environment.TickCount64;
                await onBatch(items).ConfigureAwait(false);
            }
        }

        var workers = Enumerable.Range(0, parallelism).Select(_ => Task.Run(async () =>
        {
            try
            {
                while (await channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (channel.Reader.TryRead(out var dir))
                    {
                        ct.ThrowIfCancellationRequested();

                        try
                        {
                            foreach (var info in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", optionsEnum))
                            {
                                ct.ThrowIfCancellationRequested();

                                var isDir = info is DirectoryInfo;
                                if (isDir)
                                {
                                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                                        || Ignorar.Contains(info.Name))
                                    {
                                        continue;
                                    }

                                    if (encontrados < options.MaxResultados
                                        && query.Matches(info.FullName, info.Name))
                                    {
                                        Interlocked.Increment(ref encontrados);
                                        batch.Enqueue(ToEntry(info, true));
                                    }

                                    Interlocked.Increment(ref pendentes);
                                    channel.Writer.TryWrite(info.FullName);
                                    continue;
                                }

                                if (encontrados >= options.MaxResultados)
                                {
                                    continue;
                                }

                                if (!query.Matches(info.FullName, info.Name))
                                {
                                    continue;
                                }

                                Interlocked.Increment(ref encontrados);
                                batch.Enqueue(ToEntry(info, false));
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Log.Error($"Busca: falha em {dir}", ex);
                        }

                        var visitadas = Interlocked.Increment(ref pastasVisitadas);
                        if (visitadas % 20 == 0)
                        {
                            await onProgress(visitadas).ConfigureAwait(false);
                        }

                        await FlushAsync(false).ConfigureAwait(false);

                        // Ultimo ramo processado fecha a fila: nada mais pode entrar.
                        if (Interlocked.Decrement(ref pendentes) == 0)
                        {
                            channel.Writer.TryComplete();
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // cancelamento normal (o usuario digitou outra busca)
            }
        }, ct)).ToArray();

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        finally
        {
            await FlushAsync(true).ConfigureAwait(false);
            await onProgress(pastasVisitadas).ConfigureAwait(false);
        }
    }

    private static FileEntry ToEntry(FileSystemInfo info, bool isDir)
    {
        long size = 0;
        if (info is FileInfo file)
        {
            try { size = file.Length; } catch { size = 0; }
        }

        return new FileEntry
        {
            Nome = info.Name,
            Caminho = info.FullName,
            Pasta = isDir,
            Ext = isDir ? string.Empty : info.Extension.ToLowerInvariant(),
            Modificado = info.LastWriteTime,
            Tamanho = size,
        };
    }
}
