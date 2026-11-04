using System.Collections.Concurrent;
using CVExplorer.Host.Features.FileSystem;
using CVExplorer.Host.Features.Media;
using CVExplorer.Host.Features.Projects;
using CVExplorer.Host.Features.Search;
using CVExplorer.Host.Features.Thumbnails;
using CVExplorer.Host.Ipc;

namespace CVExplorer.Host;

/// <summary>
/// Ponto unico de montagem dos servicos. Mantem o resto do codigo sem "new" espalhado.
/// </summary>
public sealed class AppHost
{
    public SettingsStore Store { get; } = new();

    public AppSettings Settings => Store.Current;

    public CancellationRegistry Cancels { get; } = new();

    public ProjectService Projects { get; } = new();

    public List<TemplateNode> Template { get; private set; }

    public ThumbnailService Thumbs { get; }

    public FfmpegLocator Ffmpeg { get; }

    public MediaProbe Probe { get; }

    public TranscodeService Transcode { get; }

    public FileOperations Files { get; }

    public PasteService Paste { get; }

    public SearchService Search { get; } = new();

    public ConflictBroker Conflicts { get; }

    public UpdateService Updates { get; } = new();

    public IpcBridge? Bridge { get; set; }

    public AppHost()
    {
        AppPaths.EnsureCreated();
        Store.Load();
        Template = TemplateCatalog.LoadOrSeed(AppPaths.TemplateFile);

        Ffmpeg = new FfmpegLocator(Settings);
        Probe = new MediaProbe(Ffmpeg);
        Transcode = new TranscodeService(Ffmpeg);
        Conflicts = new ConflictBroker(info => EmitAsync("op.conflict", info));
        Files = new FileOperations(Conflicts.AskAsync, progress => EmitAsync("op.progress", progress));
        Paste = new PasteService(Probe, Transcode, Files, Settings);
        Thumbs = new ThumbnailService(Settings, Ffmpeg);
    }

    public void Emit(string evt, object? data) => Bridge?.Emit(evt, data);

    public Task EmitAsync(string evt, object? data)
    {
        Emit(evt, data);
        return Task.CompletedTask;
    }

    public void RecarregarTemplate() => Template = TemplateCatalog.LoadOrSeed(AppPaths.TemplateFile);
}

/// <summary>
/// Conflito de nomes: a operacao pausa e espera a decisao da interface
/// (substituir / manter ambos / pular / cancelar).
/// </summary>
public sealed class ConflictBroker
{
    private readonly Func<ConflictInfo, Task> _notify;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ConflictDecision>> _aguardando = new();
    private ConflictDecision? _aplicarTodos;

    public ConflictBroker(Func<ConflictInfo, Task> notify) => _notify = notify;

    public void Reset() => _aplicarTodos = null;

    public async Task<ConflictDecision> AskAsync(ConflictInfo info)
    {
        if (_aplicarTodos is { } decisao)
        {
            return decisao;
        }

        var tcs = new TaskCompletionSource<ConflictDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        _aguardando[info.Id] = tcs;
        await _notify(info).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public void Resolve(string id, ConflictDecision decisao, bool aplicarTodos)
    {
        if (aplicarTodos)
        {
            _aplicarTodos = decisao;
        }

        if (_aguardando.TryRemove(id, out var tcs))
        {
            tcs.TrySetResult(decisao);
        }
    }

    /// <summary>Usado quando o usuario cancela: solta a operacao em espera.</summary>
    public void CancelAll()
    {
        foreach (var key in _aguardando.Keys)
        {
            if (_aguardando.TryRemove(key, out var tcs))
            {
                tcs.TrySetResult(ConflictDecision.Cancelar);
            }
        }
    }
}
