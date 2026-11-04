namespace CVExplorer.Host;

/// <summary>
/// Arraste entre janelas. O Chromium de cada WebView2 captura o mouse, entao o
/// "soltar" nunca chega na outra janela: quem sabe onde o cursor esta e o host.
/// A interface avisa que comecou, o host acompanha a posicao real do cursor
/// (GetCursorPos) e, ao soltar, entrega os arquivos na pasta atual da janela alvo.
/// </summary>
public sealed class ArrasteService
{
    private readonly AppHost _host;
    private readonly object _gate = new();
    private System.Threading.Timer? _relogio;
    private string? _destinoAtual;

    public ArrasteService(AppHost host) => _host = host;

    public bool Ativo { get; private set; }

    public IReadOnlyList<string> Caminhos { get; private set; } = Array.Empty<string>();

    public bool Copiar { get; private set; }

    public Janela? Origem { get; private set; }

    public void Iniciar(Janela origem, IReadOnlyList<string> caminhos, bool copiar)
    {
        lock (_gate)
        {
            Caminhos = caminhos;
            Copiar = copiar;
            Origem = origem;
            Ativo = caminhos.Count > 0;
            _destinoAtual = null;
        }

        if (!Ativo)
        {
            return;
        }

        _relogio?.Dispose();
        _relogio = new System.Threading.Timer(_ => VerificarAlvo(), null, 0, 90);
    }

    public void Cancelar()
    {
        lock (_gate)
        {
            Ativo = false;
            Caminhos = Array.Empty<string>();
            _destinoAtual = null;
        }

        _relogio?.Dispose();
        _relogio = null;
        _host.EmitTodos("arraste.estado", new { ativo = false });
    }

    /// <summary>Chamado quando o usuario solta o botao. Devolve true se algo foi entregue.</summary>
    public async Task<bool> SoltarAsync()
    {
        Janela? alvo;
        List<string> caminhos;
        bool copiar;

        lock (_gate)
        {
            alvo = AlvoNoCursor();
            caminhos = Caminhos.ToList();
            copiar = Copiar;
        }

        _relogio?.Dispose();
        _relogio = null;

        lock (_gate)
        {
            Ativo = false;
            Caminhos = Array.Empty<string>();
            _destinoAtual = null;
        }

        _host.EmitTodos("arraste.estado", new { ativo = false });

        // Sem janela alvo (mesma janela de origem, ou fora do app): nada a fazer aqui.
        // O arraste dentro da propria janela e resolvido pela interface.
        if (alvo is null || caminhos.Count == 0)
        {
            return false;
        }

        var destino = alvo.PastaAtual;
        if (string.IsNullOrWhiteSpace(destino) || !Directory.Exists(destino))
        {
            return false;
        }

        await _host.EntregarArrasteAsync(caminhos, destino, copiar, alvo).ConfigureAwait(false);
        return true;
    }

    private void VerificarAlvo()
    {
        if (!Ativo)
        {
            return;
        }

        var alvo = AlvoNoCursor();
        var destino = alvo is null ? null : $"{alvo.Id}|{alvo.PastaAtual}";

        if (destino == _destinoAtual)
        {
            return;
        }

        _destinoAtual = destino;
        _host.EmitTodos("arraste.estado", new
        {
            ativo = true,
            alvo = alvo?.Id,
            pasta = alvo?.PastaAtual,
            copiar = Copiar,
            itens = Caminhos.Count,
        });
    }

    private Janela? AlvoNoCursor()
    {
        if (!Native.Win32.CursorPos(out var x, out var y))
        {
            return null;
        }

        // A janela de origem nao conta como alvo: arrastar para dentro dela e
        // tratado no proprio JavaScript (pasta sob o cursor).
        return _host.Janelas.FirstOrDefault(j =>
            j is { IsDisposed: false } && j.Id != Origem?.Id && j.ContemPonto(x, y));
    }
}
