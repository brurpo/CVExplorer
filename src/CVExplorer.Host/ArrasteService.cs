namespace CVExplorer.Host;

/// <summary>
/// Arraste entre janelas. O Chromium de cada WebView2 captura o mouse, entao o
/// "soltar" nunca chega na outra janela: quem sabe onde o cursor esta e o host.
/// A interface avisa que comecou, o host acompanha a posicao real do cursor
/// (GetCursorPos) e, ao soltar, entrega os arquivos na pasta atual da janela alvo.
/// O mesmo rastreio serve para o arraste **para fora do app**: quando o cursor sai de
/// todas as janelas, o host assume com um arraste do Windows (CF_HDROP com os caminhos
/// reais), que e o unico jeito de soltar no Explorer, no After Effects e companhia.
/// </summary>
public sealed class ArrasteService
{
    private readonly AppHost _host;
    private readonly object _gate = new();
    private System.Threading.Timer? _relogio;
    private string? _destinoAtual;
    private bool _nativo;

    public ArrasteService(AppHost host) => _host = host;

    public bool Ativo { get; private set; }

    public IReadOnlyList<string> Caminhos { get; private set; } = Array.Empty<string>();

    public bool Copiar { get; private set; }

    public Janela? Origem { get; private set; }

    /// <summary>
    /// O arraste virou um arraste do Windows (saiu do app). Serve para as nossas janelas
    /// recusarem o drop: os arquivos ja estao aqui dentro.
    /// </summary>
    public bool NativoEmCurso
    {
        get
        {
            lock (_gate)
            {
                return _nativo;
            }
        }
    }

    public void Iniciar(Janela origem, IReadOnlyList<string> caminhos, bool copiar)
    {
        lock (_gate)
        {
            Caminhos = caminhos;
            Copiar = copiar;
            Origem = origem;
            Ativo = caminhos.Count > 0;
            _destinoAtual = null;
            _nativo = false;
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
            _nativo = false;
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
            // Quem manda no arraste agora e o loop do Windows: nao mexe no estado.
            if (_nativo)
            {
                return false;
            }

            alvo = AlvoNoCursor();
            caminhos = Caminhos.ToList();
            copiar = Copiar;
        }

        _relogio?.Dispose();
        _relogio = null;

        lock (_gate)
        {
            Ativo = false;
            _nativo = false;
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

        // Botao ja solto (o SoltarAsync da interface resolve) ou arraste entregue ao Windows.
        if (!Native.Win32.BotaoEsquerdoPressionado())
        {
            return;
        }

        var alvo = AlvoNoCursor();

        // Fora de todas as janelas do app: daqui para frente quem arrasta e o Windows.
        if (alvo is null && !SobreOApp())
        {
            IniciarNativo();
            return;
        }

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

    /// <summary>
    /// Passa o arraste para o Windows. O DoDragDrop e um loop modal no thread da janela
    /// (nao pode rodar aqui, no thread do relogio) e roda ate o usuario soltar.
    /// </summary>
    private void IniciarNativo()
    {
        Janela? origem;
        string[] caminhos;
        bool copiar;

        lock (_gate)
        {
            if (_nativo || !Ativo)
            {
                return;
            }

            // Dentro de arquivo compactado o caminho nao existe no disco: nao da para arrastar.
            caminhos = Caminhos.Where(c => File.Exists(c) || Directory.Exists(c)).ToArray();
            _nativo = true;
            origem = Origem;
            copiar = Copiar;
        }

        _relogio?.Dispose();
        _relogio = null;

        if (origem is null || origem.IsDisposed || caminhos.Length == 0)
        {
            FinalizarNativo(false);
            return;
        }

        try
        {
            origem.BeginInvoke(new Action(() =>
            {
                var moveu = origem.ArrastarParaFora(caminhos, copiar);
                FinalizarNativo(moveu);
            }));
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao entregar o arraste ao Windows", ex);
            FinalizarNativo(false);
        }
    }

    private void FinalizarNativo(bool moveu)
    {
        lock (_gate)
        {
            Ativo = false;
            _nativo = false;
            Caminhos = Array.Empty<string>();
            _destinoAtual = null;
        }

        // moveu: o destino levou os arquivos embora (Explorer na mesma unidade) e a lista
        // da janela ficou desatualizada.
        _host.EmitTodos("arraste.estado", new { ativo = false, externo = true, moveu });
    }

    /// <summary>O cursor esta sobre alguma janela deste app (incluindo a de origem)?</summary>
    private bool SobreOApp()
    {
        if (!Native.Win32.CursorPos(out var x, out var y))
        {
            return true;
        }

        var raiz = Native.Win32.JanelaRaizNoCursor(x, y);

        // Sem janela conhecida (area de trabalho, menu do sistema): considera fora do app.
        if (raiz == IntPtr.Zero)
        {
            return false;
        }

        return _host.Janelas.Any(j => j is { IsDisposed: false } && j.Handle == raiz);
    }

    private Janela? AlvoNoCursor()
    {
        if (!Native.Win32.CursorPos(out var x, out var y))
        {
            return null;
        }

        var raiz = Native.Win32.JanelaRaizNoCursor(x, y);
        if (raiz == IntPtr.Zero)
        {
            return null;
        }

        // A janela de origem nao conta como alvo: soltar de volta nela e tratado no
        // proprio JavaScript (pasta sob o cursor, ou deixar o host encerrar o arraste).
        return _host.Janelas.FirstOrDefault(j =>
            j is { IsDisposed: false } && j.Id != Origem?.Id && j.Handle == raiz);
    }
}
