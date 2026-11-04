using System.Collections.Concurrent;
using CVExplorer.Host.Features.FileSystem;
using CVExplorer.Host.Features.Media;
using CVExplorer.Host.Features.Projects;
using CVExplorer.Host.Features.Search;
using CVExplorer.Host.Features.Thumbnails;
using CVExplorer.Host.Ipc;
using Microsoft.Web.WebView2.Core;

namespace CVExplorer.Host;

/// <summary>
/// Ponto unico de montagem dos servicos. Mantem o resto do codigo sem "new" espalhado.
/// </summary>
public sealed class AppHost
{
    private static readonly AsyncLocal<Janela?> ContextoAtual = new();

    private readonly List<Janela> _janelas = new();
    private CoreWebView2Environment? _ambiente;
    private Dictionary<string, IpcBridge.Route>? _routes;

    public SettingsStore Store { get; } = new();

    public AppSettings Settings => Store.Current;

    public CancellationRegistry Cancels { get; } = new();

    public ProjectService Projects { get; } = new();

    public List<TemplateNode> Template { get; private set; }

    public ThumbnailService Thumbs { get; }

    public FfmpegLocator Ffmpeg { get; }

    public MediaProbe Probe { get; }

    public TranscodeService Transcode { get; }

    public PreviewStreamService Previa { get; }

    public PlayerServer Player { get; }

    public FileOperations Files { get; }

    public PasteService Paste { get; }

    public SearchService Search { get; } = new();

    public ConflictBroker Conflicts { get; }

    public UpdateService Updates { get; } = new();

    public ArrasteService Arraste { get; }

    /// <summary>Janela que abriu o comando em execucao (para eventos e conflitos).</summary>
    public Janela? JanelaDoContexto => ContextoAtual.Value;

    public Janela? JanelaAtiva { get; set; }

    public IReadOnlyList<Janela> Janelas => _janelas;

    public Dictionary<string, IpcBridge.Route> Routes => _routes ??= Commands.Build(this);

    public void DefinirContexto(Janela janela) => ContextoAtual.Value = janela;

    public AppHost()
    {
        AppPaths.EnsureCreated();
        Store.Load();
        Template = TemplateCatalog.LoadOrSeed(AppPaths.TemplateFile);

        Ffmpeg = new FfmpegLocator(Settings);
        Probe = new MediaProbe(Ffmpeg);
        Transcode = new TranscodeService(Ffmpeg);
        Previa = new PreviewStreamService(Ffmpeg);
        Player = new PlayerServer(Previa);
        Conflicts = new ConflictBroker(info => EmitAsync("op.conflict", info));
        Files = new FileOperations(Conflicts.AskAsync, progress => EmitAsync("op.progress", progress));
        Paste = new PasteService(Probe, Transcode, Files, Settings);
        Thumbs = new ThumbnailService(Settings, Ffmpeg);
        Arraste = new ArrasteService(this);
    }

    /// <summary>Abre uma janela nova. A primeira persiste posicao; as outras nascem deslocadas.</summary>
    public Janela AbrirJanela(string? caminho = null, bool primaria = false)
    {
        var janela = new Janela(this, primaria, caminho);
        _janelas.Add(janela);
        janela.FormClosed += (_, _) =>
        {
            if (_janelas.Count(j => !j.IsDisposed) == 0)
            {
                UltimaJanelaFechada?.Invoke();
            }
        };
        janela.Show();
        return janela;
    }

    public event Action? UltimaJanelaFechada;

    public void RemoverJanela(Janela janela) => _janelas.Remove(janela);

    public async Task<CoreWebView2Environment> AmbienteWebViewAsync(string pastaDados)
    {
        if (_ambiente is not null)
        {
            return _ambiente;
        }

        _ambiente = await CoreWebView2Environment.CreateAsync(null, pastaDados).ConfigureAwait(true);
        return _ambiente;
    }

    public void TrazerParaFrente(Janela janela)
    {
        try
        {
            janela.Activate();
            Native.Win32.TrazerParaFrente(AppInfo.Nome);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao trazer janela para frente", ex);
        }
    }

    /// <summary>Arraste entre janelas: copia ou move os itens para a pasta da janela alvo.</summary>
    public async Task EntregarArrasteAsync(
        IReadOnlyList<string> caminhos,
        string destino,
        bool copiar,
        Janela alvo)
    {
        var opId = $"arraste-{Guid.NewGuid():N}";
        var origem = JanelaDoContexto ?? JanelaAtiva;

        try
        {
            var resultado = copiar
                ? await Files.CopyAsync(caminhos, destino, opId, CancellationToken.None).ConfigureAwait(false)
                : await Files.MoveAsync(caminhos, destino, opId, CancellationToken.None).ConfigureAwait(false);

            alvo.Enviar("dir.refresh", new { caminho = destino });
            origem?.Enviar("dir.refresh", new { caminho = origem.PastaAtual });

            if (resultado.Erros.Count > 0)
            {
                alvo.Enviar("arraste.erro", new { mensagem = resultado.Erros[0] });
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao entregar arraste", ex);
            alvo.Enviar("arraste.erro", new { mensagem = ex.Message });
        }
    }

    public void Emit(string evt, object? data)
    {
        if (ContextoAtual.Value is { } janela)
        {
            janela.Enviar(evt, data);
            return;
        }

        EmitTodos(evt, data);
    }

    public void EmitTodos(string evt, object? data)
    {
        foreach (var janela in _janelas.ToArray())
        {
            janela.Enviar(evt, data);
        }
    }

    public void EmitPara(Janela janela, string evt, object? data) => janela.Enviar(evt, data);

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
