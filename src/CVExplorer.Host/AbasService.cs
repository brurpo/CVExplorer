using CVExplorer.Host.Native;

namespace CVExplorer.Host;

/// <summary>
/// Arraste de abas entre janelas. Como no arraste de arquivos, o Chromium de cada
/// janela não vê o mouse da outra: quem sabe onde o cursor está é o host.
///
/// O ciclo é este: a interface avisa que o arraste começou e segue reordenando as abas
/// enquanto o cursor está na barra; quando o cursor sai da janela de origem, a janela nova
/// nasce na hora, embaixo do cursor, já com as abas. Daí em diante quem acompanha o botão é
/// o host: soltar sobre a barra de outra janela junta as abas lá, soltar em qualquer outro
/// lugar só deixa a janela onde ela nasceu.
/// </summary>
public sealed class AbasService
{
    // Faixa das abas dentro da janela, em px de CSS: 30 (barra de título) + 52 (ferramentas).
    private const double FaixaTopo = 82;
    private const double FaixaAltura = 34;

    private readonly AppHost _host;
    private readonly object _gate = new();
    private System.Threading.Timer? _relogio;
    private Modo _modo;
    private Janela? _origem;
    private List<string> _abas = new();
    private Janela? _destacada;
    private Janela? _alvo;
    private int _executando;

    private enum Modo
    {
        Nenhum,
        Abas,
        Janela,
    }

    public AbasService(AppHost host) => _host = host;

    /// <summary>Começou a arrastar as abas selecionadas.</summary>
    public void Iniciar(Janela? origem, IReadOnlyList<string> abas)
    {
        lock (_gate)
        {
            _modo = Modo.Abas;
            _origem = origem;
            _abas = (abas ?? Array.Empty<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();
            _destacada = null;
            _alvo = null;
        }

        if (abas is null || origem is null || _abas.Count == 0)
        {
            Encerrar();
            return;
        }

        _relogio?.Dispose();
        _relogio = new System.Threading.Timer(_ => Verificar(), null, 0, 60);
    }

    /// <summary>Começou a arrastar a janela pela barra de título.</summary>
    public void IniciarJanela(Janela origem)
    {
        lock (_gate)
        {
            _modo = Modo.Janela;
            _origem = origem;
            _abas = new List<string>();
            _destacada = null;
            _alvo = null;
        }

        _relogio?.Dispose();
        _relogio = new System.Threading.Timer(_ => Verificar(), null, 0, 60);
    }

    /// <summary>O arraste terminou dentro da própria janela: nada a fazer no host.</summary>
    public void Cancelar()
    {
        Encerrar();
        _host.EmitTodos("abas.arraste", new { ativo = false });
    }

    private void Encerrar()
    {
        _relogio?.Dispose();
        _relogio = null;

        lock (_gate)
        {
            _modo = Modo.Nenhum;
            _origem = null;
            _abas = new List<string>();
            _destacada = null;
            _alvo = null;
        }
    }

    private void Verificar()
    {
        // O timer roda numa thread de fundo, e daqui saem janelas (abrir, fechar, realçar).
        // WinForms só aceita isso na thread da interface — fazer fora dela trava o app.
        if (Interlocked.CompareExchange(ref _executando, 1, 0) != 0)
        {
            return;
        }

        var ancora = JanelaDaInterface();
        if (ancora is null)
        {
            Interlocked.Exchange(ref _executando, 0);
            return;
        }

        try
        {
            ancora.BeginInvoke(() =>
            {
                try
                {
                    VerificarNaInterface();
                }
                catch (Exception ex)
                {
                    Log.Error("Falha no rastreio do arraste de abas", ex);
                    Encerrar();
                }
                finally
                {
                    Interlocked.Exchange(ref _executando, 0);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao agendar o rastreio do arraste de abas", ex);
            Interlocked.Exchange(ref _executando, 0);
        }
    }

    /// <summary>Janela viva com handle, para postar trabalho na thread da interface.</summary>
    private Janela? JanelaDaInterface()
    {
        Janela? origem;
        lock (_gate)
        {
            origem = _origem;
        }

        if (origem is { IsDisposed: false, IsHandleCreated: true })
        {
            return origem;
        }

        return _host.Janelas.FirstOrDefault(j => j is { IsDisposed: false, IsHandleCreated: true });
    }

    private void VerificarNaInterface()
    {
        if (!Win32.CursorPos(out var x, out var y))
        {
            return;
        }

        Modo modo;
        Janela? origem;
        Janela? destacada;

        lock (_gate)
        {
            modo = _modo;
            origem = _origem;
            destacada = _destacada;
        }

        if (modo == Modo.Nenhum || origem is null || origem.IsDisposed)
        {
            return;
        }

        if (!Win32.BotaoEsquerdoPressionado())
        {
            if (modo == Modo.Abas)
            {
                SoltarAbas(x, y, origem, destacada);
            }
            else
            {
                SoltarJanela(x, y, origem);
            }

            return;
        }

        if (modo == Modo.Abas)
        {
            SeguirAbas(x, y, origem, destacada);
        }
        else
        {
            Marcar(NaFaixaDeAbas(x, y, origem.Id), origem.Id);
        }
    }

    private void SeguirAbas(int x, int y, Janela origem, Janela? destacada)
    {
        if (destacada is null)
        {
            // Saiu da janela de origem com o botão ainda pressionado: a janela nova
            // nasce já embaixo do cursor, com as abas que estavam sendo arrastadas.
            if (origem.ContemPonto(x, y))
            {
                Marcar(null, origem.Id);
                return;
            }

            Destacar(x, y, origem);
            return;
        }

        Marcar(NaFaixaDeAbas(x, y, destacada.Id), destacada.Id);
    }

    /// <summary>Cria a janela destacada uma única vez, no ponto do cursor.</summary>
    private void Destacar(int x, int y, Janela origem)
    {
        List<string> abas;
        lock (_gate)
        {
            if (_destacada is not null || _abas.Count == 0)
            {
                return;
            }

            abas = _abas.ToList();
        }

        var nova = _host.AbrirJanela(
            abas[0],
            primaria: false,
            abasExtras: abas.Skip(1).ToList(),
            posicao: new Point(x - 140, y - 18));

        lock (_gate)
        {
            _destacada = nova;
        }

        // A origem perdeu as abas: quem as tem agora é a janela nova.
        origem.Enviar("abas.destacada", new { janela = nova.Id });
        Marcar(null, nova.Id);
    }

    private void SoltarAbas(int x, int y, Janela origem, Janela? destacada)
    {
        List<string> abas;
        lock (_gate)
        {
            abas = _abas.ToList();
        }

        var alvo = NaFaixaDeAbas(x, y, destacada?.Id ?? origem.Id);

        if (alvo is not null && alvo.Id != origem.Id && abas.Count > 0)
        {
            alvo.Enviar("abas.adotar", new { abas });

            if (destacada is not null)
            {
                destacada.FecharSilencioso();
            }
            else
            {
                origem.Enviar("abas.destacada", new { janela = alvo.Id });
            }

            Encerrar();
            _host.EmitTodos("abas.arraste", new { ativo = false });
            return;
        }

        if (destacada is null && !origem.ContemPonto(x, y))
        {
            // Soltou fora de tudo sem ter saído da janela: ainda vira janela nova.
            Destacar(x, y, origem);
        }

        Encerrar();
        _host.EmitTodos("abas.arraste", new { ativo = false });
    }

    private void SoltarJanela(int x, int y, Janela origem)
    {
        var alvo = NaFaixaDeAbas(x, y, origem.Id);

        if (alvo is not null)
        {
            // A janela arrastada foi solta na barra de abas de outra: as abas passam
            // para lá e a janela de origem some.
            origem.Enviar("abas.fundir", new { destino = alvo.Id });
        }

        Encerrar();
        _host.EmitTodos("abas.arraste", new { ativo = false });
    }

    /// <summary>Realce da janela que receberia as abas. Só emite quando muda de dono.</summary>
    private void Marcar(Janela? alvo, string origemId)
    {
        Janela? anterior;
        lock (_gate)
        {
            if (_alvo?.Id == alvo?.Id)
            {
                return;
            }

            anterior = _alvo;
            _alvo = alvo;
        }

        if (anterior is { IsDisposed: false })
        {
            anterior.Enviar("abas.alvo", new { ativo = false });
        }

        alvo?.Enviar("abas.alvo", new { ativo = true });
        _host.EmitTodos("abas.arraste", new { ativo = true, alvo = alvo?.Id });
    }

    /// <summary>Janela cujo retângulo de abas contém o ponto (ignora a janela informada).</summary>
    private Janela? NaFaixaDeAbas(int x, int y, string? ignorar)
    {
        foreach (var janela in _host.Janelas.Where(j => j is { IsDisposed: false }))
        {
            if (janela.Id == ignorar)
            {
                continue;
            }

            var escala = janela.DeviceDpi / 96.0;
            var faixa = new Rectangle(
                janela.Bounds.Left,
                janela.Bounds.Top + (int)Math.Round(FaixaTopo * escala),
                janela.Bounds.Width,
                (int)Math.Round(FaixaAltura * escala));

            if (faixa.Contains(x, y))
            {
                return janela;
            }
        }

        return null;
    }
}


