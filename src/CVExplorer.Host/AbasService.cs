namespace CVExplorer.Host;

/// <summary>
/// Arraste de abas entre janelas. Como no arraste de arquivos, o Chromium de cada
/// janela não vê o mouse da outra: quem sabe onde o cursor está é o host.
/// A interface avisa que começou, o host acompanha o cursor e, ao soltar, decide:
/// fora do app vira janela nova; sobre outra janela as abas entram naquela janela.
/// </summary>
public sealed class AbasService
{
    private readonly AppHost _host;
    private readonly object _gate = new();
    private System.Threading.Timer? _relogio;
    private Janela? _origem;
    private List<string> _abas = new();
    private string? _alvoAtual;

    public AbasService(AppHost host) => _host = host;

    public void Iniciar(Janela? origem, IReadOnlyList<string> abas)
    {
        lock (_gate)
        {
            _origem = origem;
            _abas = (abas ?? Array.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            _alvoAtual = null;
        }

        if (_abas.Count == 0)
        {
            return;
        }

        _relogio?.Dispose();
        _relogio = new System.Threading.Timer(_ => VerificarAlvo(), null, 0, 90);
    }

    public void Cancelar()
    {
        _relogio?.Dispose();
        _relogio = null;

        lock (_gate)
        {
            _origem = null;
            _abas = new List<string>();
            _alvoAtual = null;
        }

        _host.EmitTodos("abas.arraste", new { ativo = false });
    }

    /// <summary>Chamado ao soltar. Devolve a ação que a janela de origem deve aplicar.</summary>
    public object Soltar()
    {
        _relogio?.Dispose();
        _relogio = null;

        Janela? origem;
        List<string> abas;

        lock (_gate)
        {
            origem = _origem;
            abas = _abas.ToList();
            _origem = null;
            _abas = new List<string>();
            _alvoAtual = null;
        }

        _host.EmitTodos("abas.arraste", new { ativo = false });

        if (abas.Count == 0 || origem is null)
        {
            return new { acao = "nada" };
        }

        var (alvo, sobreJanela) = AlvoNoCursor(origem);

        // Sobre outra janela: as abas passam para a barra dela.
        if (alvo is not null)
        {
            alvo.Enviar("abas.adotar", new { abas });
            return new { acao = "mover", destino = alvo.Id };
        }

        // Ainda sobre alguma janela (a própria origem): o JS resolve a reordenação.
        if (sobreJanela)
        {
            return new { acao = "nada" };
        }

        // Solto fora do app: vira janela nova com as abas arrastadas.
        var nova = _host.AbrirJanela(abas[0], primaria: false, abasExtras: abas.Skip(1).ToList());
        return new { acao = "destacar", destino = nova.Id };
    }

    private void VerificarAlvo()
    {
        var (alvo, sobreJanela) = AlvoNoCursor(_origem);
        var marca = $"{alvo?.Id}|{sobreJanela}";

        lock (_gate)
        {
            if (marca == _alvoAtual)
            {
                return;
            }

            _alvoAtual = marca;
        }

        _host.EmitTodos("abas.arraste", new
        {
            ativo = true,
            alvo = alvo?.Id,
            sobreJanela,
            itens = _abas.Count,
        });
    }

    private (Janela? Janela, bool SobreAlguma) AlvoNoCursor(Janela? origem)
    {
        if (!Native.Win32.CursorPos(out var x, out var y))
        {
            return (null, false);
        }

        var janelas = _host.Janelas.Where(j => j is { IsDisposed: false }).ToList();
        var sobreAlguma = janelas.Any(j => j.ContemPonto(x, y));
        var alvo = janelas.FirstOrDefault(j => j.Id != origem?.Id && j.ContemPonto(x, y));
        return (alvo, sobreAlguma);
    }
}
