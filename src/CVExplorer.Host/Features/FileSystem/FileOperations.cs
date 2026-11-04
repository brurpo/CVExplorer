using Microsoft.VisualBasic.FileIO;
using CVExplorer.Host.Native;

namespace CVExplorer.Host.Features.FileSystem;

public enum ConflictDecision
{
    Substituir,
    ManterAmbos,
    Pular,
    Cancelar,
}

public sealed class ConflictInfo
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Origem { get; set; } = string.Empty;

    public string Destino { get; set; } = string.Empty;

    public bool Pasta { get; set; }

    public long TamanhoOrigem { get; set; }

    public long TamanhoDestino { get; set; }
}

public sealed class OperationProgress
{
    public string OperacaoId { get; set; } = string.Empty;

    public string Tipo { get; set; } = string.Empty;

    public int ItensTotal { get; set; }

    public int ItensConcluidos { get; set; }

    public string ItemAtual { get; set; } = string.Empty;

    public long BytesTotal { get; set; }

    public long BytesConcluidos { get; set; }
}

public sealed class OperationResult
{
    public int Copiados { get; set; }

    public int Movidos { get; set; }

    public int Ignorados { get; set; }

    public List<string> Erros { get; } = new();

    public bool Cancelado { get; set; }
}

/// <summary>
/// Copia/move com progresso, cancelamento e politica de conflito.
/// Regras: nunca sobrescreve sem decisao explicita e nunca apaga o original em copias.
/// </summary>
public sealed class FileOperations
{
    private const int BufferSize = 1024 * 1024;

    private readonly Func<ConflictInfo, Task<ConflictDecision>> _askConflict;
    private readonly Func<OperationProgress, Task> _report;

    public FileOperations(
        Func<ConflictInfo, Task<ConflictDecision>> askConflict,
        Func<OperationProgress, Task> report)
    {
        _askConflict = askConflict;
        _report = report;
    }

    public async Task<OperationResult> CopyAsync(
        IReadOnlyList<string> sources,
        string destinationDir,
        string operationId,
        CancellationToken ct)
    {
        var result = new OperationResult();
        Directory.CreateDirectory(destinationDir);

        var bytesTotal = await Task.Run(() => Measure(sources), ct).ConfigureAwait(false);
        var progress = new OperationProgress
        {
            OperacaoId = operationId,
            Tipo = "copiar",
            ItensTotal = sources.Count,
            BytesTotal = bytesTotal,
        };

        foreach (var source in sources)
        {
            if (ct.IsCancellationRequested)
            {
                result.Cancelado = true;
                break;
            }

            try
            {
                var target = await ResolveTargetAsync(source, destinationDir, ct).ConfigureAwait(false);
                if (target is null)
                {
                    result.Ignorados++;
                    continue;
                }

                await CopyEntryAsync(source, target, progress, ct).ConfigureAwait(false);
                result.Copiados++;
            }
            catch (OperationCanceledException)
            {
                result.Cancelado = true;
                break;
            }
            catch (Exception ex)
            {
                result.Erros.Add($"{Path.GetFileName(source)}: {ex.Message}");
                Log.Error($"Falha ao copiar {source}", ex);
            }
            finally
            {
                progress.ItensConcluidos++;
                await _report(progress).ConfigureAwait(false);
            }
        }

        return result;
    }

    public async Task<OperationResult> MoveAsync(
        IReadOnlyList<string> sources,
        string destinationDir,
        string operationId,
        CancellationToken ct)
    {
        var result = new OperationResult();
        Directory.CreateDirectory(destinationDir);

        var progress = new OperationProgress
        {
            OperacaoId = operationId,
            Tipo = "mover",
            ItensTotal = sources.Count,
        };

        foreach (var source in sources)
        {
            if (ct.IsCancellationRequested)
            {
                result.Cancelado = true;
                break;
            }

            try
            {
                var target = await ResolveTargetAsync(source, destinationDir, ct).ConfigureAwait(false);
                if (target is null)
                {
                    result.Ignorados++;
                    continue;
                }

                var sameName = string.Equals(
                    Path.GetFullPath(Path.GetDirectoryName(source)!),
                    Path.GetFullPath(Path.GetDirectoryName(target)!),
                    StringComparison.OrdinalIgnoreCase);

                if (!sameName && SameVolume(source, target))
                {
                    progress.ItemAtual = Path.GetFileName(source);
                    await _report(progress).ConfigureAwait(false);

                    if (Directory.Exists(source))
                    {
                        Directory.Move(source, target);
                    }
                    else
                    {
                        File.Move(source, target, overwrite: true);
                    }
                }
                else if (!sameName)
                {
                    // Volumes diferentes (ex.: SSD -> rede): copia e so entao remove.
                    await CopyEntryAsync(source, target, progress, ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    DeletePermanently(source);
                }

                result.Movidos++;
            }
            catch (OperationCanceledException)
            {
                result.Cancelado = true;
                break;
            }
            catch (Exception ex)
            {
                result.Erros.Add($"{Path.GetFileName(source)}: {ex.Message}");
                Log.Error($"Falha ao mover {source}", ex);
            }
            finally
            {
                progress.ItensConcluidos++;
                await _report(progress).ConfigureAwait(false);
            }
        }

        return result;
    }

    public static string Rename(string path, string newName)
    {
        var clean = Sanitize(newName);
        if (clean.Length == 0)
        {
            throw new ArgumentException("Nome invalido.");
        }

        var parent = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Caminho sem pasta pai.");

        var target = Path.Combine(parent, clean);
        if (string.Equals(target, path, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (File.Exists(target) || Directory.Exists(target))
        {
            target = UniquePath(target, Path.GetExtension(path), Directory.Exists(path));
        }

        if (Directory.Exists(path))
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }

        return target;
    }

    public static string CreateFolder(string parent, string name)
    {
        var clean = Sanitize(name);
        if (clean.Length == 0)
        {
            clean = "Nova pasta";
        }

        var target = UniquePath(Path.Combine(parent, clean), string.Empty, true);
        Directory.CreateDirectory(target);
        return target;
    }

    /// <summary>Envia para a Lixeira (reversivel). Pastas grandes: recursivo.</summary>
    public static void DeleteToRecycleBin(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
            else if (File.Exists(path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            }
        }
    }

    public static long Measure(IEnumerable<string> sources)
    {
        long total = 0;
        foreach (var source in sources)
        {
            try
            {
                if (Directory.Exists(source))
                {
                    var di = new DirectoryInfo(source);
                    total += di.EnumerateFiles("*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                    }).Sum(f =>
                    {
                        try { return f.Length; } catch { return 0; }
                    });
                }
                else if (File.Exists(source))
                {
                    total += new FileInfo(source).Length;
                }
            }
            catch
            {
                // tamanho e apenas informativo
            }
        }

        return total;
    }

    /// <summary>"nome (2).ext", "nome (3).ext" — nunca gera nome duplicado.</summary>
    public static string UniquePath(string desiredPath, string extension, bool isDirectory)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath))
        {
            return desiredPath;
        }

        var dir = Path.GetDirectoryName(desiredPath)!;
        var stem = Path.GetFileNameWithoutExtension(desiredPath);
        var ext = isDirectory ? string.Empty : (string.IsNullOrEmpty(extension) ? Path.GetExtension(desiredPath) : extension);

        for (var i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(dir, $"{stem} ({Guid.NewGuid():N}){ext}");
    }

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray()).TrimEnd('.', ' ');
    }

    private async Task<string?> ResolveTargetAsync(string source, string destinationDir, CancellationToken ct)
    {
        var name = Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar));
        var target = Path.Combine(destinationDir, name);
        var isDirectory = Directory.Exists(source);

        var exists = isDirectory ? Directory.Exists(target) : File.Exists(target);
        if (!exists)
        {
            return target;
        }

        var info = new ConflictInfo
        {
            Origem = source,
            Destino = target,
            Pasta = isDirectory,
            TamanhoOrigem = isDirectory ? Measure(new[] { source }) : new FileInfo(source).Length,
            TamanhoDestino = isDirectory
                ? Measure(new[] { target })
                : (File.Exists(target) ? new FileInfo(target).Length : 0),
        };

        var decision = await _askConflict(info).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        return decision switch
        {
            ConflictDecision.Substituir => target,
            ConflictDecision.ManterAmbos => UniquePath(target, Path.GetExtension(target), isDirectory),
            ConflictDecision.Pular => null,
            _ => throw new OperationCanceledException(),
        };
    }

    private async Task CopyEntryAsync(string source, string target, OperationProgress progress, CancellationToken ct)
    {
        if (Directory.Exists(source))
        {
            await CopyDirectoryAsync(source, target, progress, ct).ConfigureAwait(false);
            return;
        }

        progress.ItemAtual = Path.GetFileName(source);
        await _report(progress).ConfigureAwait(false);

        var dir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = target + ".cvtmp";
        await using (var input = new FileStream(
            Win32.LongPath(source), FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
        await using (var output = new FileStream(
            Win32.LongPath(tmp), FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress.BytesConcluidos += read;
            }
        }

        if (File.Exists(target))
        {
            File.SetAttributes(target, FileAttributes.Normal);
            File.Delete(target);
        }

        File.Move(tmp, target);
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
    }

    private async Task CopyDirectoryAsync(string source, string target, OperationProgress progress, CancellationToken ct)
    {
        Directory.CreateDirectory(target);

        foreach (var file in Directory.EnumerateFiles(source, "*", new EnumerationOptions
                 {
                     IgnoreInaccessible = true,
                     RecurseSubdirectories = false,
                     AttributesToSkip = FileAttributes.System,
                 }))
        {
            ct.ThrowIfCancellationRequested();
            var dest = Path.Combine(target, Path.GetFileName(file));
            await CopyEntryAsync(file, dest, progress, ct).ConfigureAwait(false);
        }

        foreach (var dir in Directory.EnumerateDirectories(source, "*", new EnumerationOptions
                 {
                     IgnoreInaccessible = true,
                     RecurseSubdirectories = false,
                     AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
                 }))
        {
            ct.ThrowIfCancellationRequested();
            await CopyDirectoryAsync(dir, Path.Combine(target, Path.GetFileName(dir)), progress, ct)
                .ConfigureAwait(false);
        }
    }

    private static bool SameVolume(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetPathRoot(Path.GetFullPath(a)),
                Path.GetPathRoot(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void DeletePermanently(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
