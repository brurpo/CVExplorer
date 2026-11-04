using CVExplorer.Host.Features.Projects;

namespace CVExplorer.Host.Features.FileSystem;
/// <summary>
/// Lista pastas em lotes. Em rede (SMB) uma pasta grande pode levar segundos,
/// entao a UI recebe resultados parciais em vez de esperar tudo.
/// </summary>
public static class DirectoryLister
{
    public const int BatchSize = 200;

    public static async Task<IReadOnlyList<FileEntry>> ListAsync(
        string path,
        bool showHidden,
        Func<IReadOnlyList<FileEntry>, Task> onBatch,
        CancellationToken ct)
    {
        var all = new List<FileEntry>();
        var batch = new List<FileEntry>(BatchSize);

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            AttributesToSkip = showHidden
                ? FileAttributes.System
                : FileAttributes.System | FileAttributes.Hidden,
        };

        var dir = new DirectoryInfo(path);

        // EnumerateFileSystemInfos reaproveita os dados do proprio FindFirstFile:
        // nao ha uma chamada extra de stat por arquivo (crucial em rede).
        foreach (var info in dir.EnumerateFileSystemInfos("*", options))
        {
            ct.ThrowIfCancellationRequested();

            // O marcador do projeto e metadado do app, nao um arquivo de trabalho.
            if (info is FileInfo && info.Name.Equals(ProjectMarker.FileName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var entry = new FileEntry
            {
                Nome = info.Name,
                Caminho = info.FullName,
                Pasta = info is DirectoryInfo,
                Ext = info is DirectoryInfo ? string.Empty : info.Extension.ToLowerInvariant(),
                Modificado = info.LastWriteTime,
                Oculto = (info.Attributes & FileAttributes.Hidden) != 0,
            };

            if (info is FileInfo file)
            {
                try
                {
                    entry.Tamanho = file.Length;
                }
                catch
                {
                    entry.Tamanho = 0;
                }
            }

            all.Add(entry);
            batch.Add(entry);

            if (batch.Count >= BatchSize)
            {
                await onBatch(batch).ConfigureAwait(false);
                batch = new List<FileEntry>(BatchSize);
            }
        }

        if (batch.Count > 0)
        {
            await onBatch(batch).ConfigureAwait(false);
        }

        var ordered = all
            .OrderByDescending(e => e.Pasta)
            .ThenBy(e => e.Nome, NaturalComparer.Instance)
            .ToList();

        return ordered;
    }
}
