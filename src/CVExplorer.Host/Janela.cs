using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using CVExplorer.Host.Ipc;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CVExplorer.Host;

/// <summary>
/// Uma janela = um WebView2 + uma ponte IPC. Todas compartilham os mesmos servicos
/// (config, miniaturas, ffmpeg, conflitos), entao abrir outra janela e barato.
/// </summary>
public sealed class Janela : Form
{
    private readonly AppHost _host;
    private readonly WebView2 _web = new();
    private readonly List<Action> _aoFechar = new();
    private IpcBridge? _bridge;
    private bool _interfacePronta;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int WM_NCCALCSIZE = 0x0083;
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_GETMINMAXINFO = 0x0024;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int HTCAPTION = 2;
    private const int HTCLIENT = 1;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    public Janela(
        AppHost host,
        bool primaria,
        string? caminhoInicial,
        string? relatorio = null,
        string? previaArquivo = null,
        Point? posicao = null,
        Size? tamanho = null)
    {
        _host = host;
        Primaria = primaria;
        CaminhoInicial = caminhoInicial;
        RelatorioPasta = relatorio;
        PreviaArquivo = previaArquivo;
        PosicaoForcada = posicao;
        TamanhoForcado = tamanho;

        Text = AppInfo.Nome;
        // Icone embutido (mesmo app.ico do executavel): garante que a barra de tarefas
        // e o Alt+Tab usem o logo novo, sem depender do cache de icones do Windows.
        Icon = IconeApp.Carregar() ?? Icon;
        MinimumSize = new Size(940, 600);
        BackColor = Color.FromArgb(14, 15, 17);
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        // Barra de titulo propria (HTML): a do Windows sai de cena e a altura util
        // comeca no logo, com o mesmo desenho escuro do resto do app.
        FormBorderStyle = FormBorderStyle.None;
        Padding = new Padding(1);

        if (primaria)
        {
            RestaurarJanela();
        }
        else if (PosicaoForcada is { } forcada)
        {
            var area = Screen.FromPoint(forcada).WorkingArea;
            var largura = TamanhoForcado?.Width ?? 1200;
            var altura = TamanhoForcado?.Height ?? 800;
            largura = Math.Clamp(largura, MinimumSize.Width, area.Width);
            altura = Math.Clamp(altura, MinimumSize.Height, area.Height);
            if (TamanhoForcado is { } pedido)
            {
                MinimumSize = new Size(Math.Min(MinimumSize.Width, pedido.Width), Math.Min(MinimumSize.Height, pedido.Height));
            }

            Bounds = new Rectangle(
                Math.Clamp(forcada.X, area.Left, Math.Max(area.Left, area.Right - largura)),
                Math.Clamp(forcada.Y, area.Top, Math.Max(area.Top, area.Bottom - altura)),
                largura,
                altura);
        }
        else if (TamanhoForcado is { } medida)
        {
            var referencia = _host.Janelas.FirstOrDefault(j => !j.IsDisposed)?.Bounds
                ?? new Rectangle(120, 90, 1280, 820);
            var area = Screen.FromPoint(new Point(referencia.X + 40, referencia.Y + 40)).WorkingArea;

            MinimumSize = new Size(
                Math.Min(MinimumSize.Width, medida.Width),
                Math.Min(MinimumSize.Height, medida.Height));

            var largura = Math.Clamp(medida.Width, 320, area.Width);
            var altura = Math.Clamp(medida.Height, 240, area.Height);
            Bounds = new Rectangle(
                Math.Clamp(referencia.X + (referencia.Width - largura) / 2, area.Left, Math.Max(area.Left, area.Right - largura)),
                Math.Clamp(referencia.Y + (referencia.Height - altura) / 2, area.Top, Math.Max(area.Top, area.Bottom - altura)),
                largura,
                altura);
        }
        else
        {
            PosicionarDeslocada();
        }

        _web.Dock = DockStyle.Fill;
        // AllowExternalDrop=false faz o controle entregar o drop do Explorer para o app,
        // com caminhos completos (o navegador so veria nomes de arquivo).
        _web.AllowExternalDrop = false;
        _web.DragEnter += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
                Emitir("os.dragOver", PontoDoMouse(e.X, e.Y));
            }
        };

        _web.DragOver += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            {
                e.Effect = DragDropEffects.Copy;
                Emitir("os.dragOver", PontoDoMouse(e.X, e.Y));
            }
        };

        _web.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] caminhos)
            {
                var ponto = PontoDoMouse(e.X, e.Y);
                Emitir("os.drop", new { caminhos, x = ponto.x, y = ponto.y });
                _host.TrazerParaFrente(this);
            }
        };

        Controls.Add(_web);

        Load += async (_, _) =>
        {
            AplicarBarraTituloEscura();
            await IniciarAsync();
        };

        FormClosing += (_, _) =>
        {
            foreach (var acao in _aoFechar)
            {
                try
                {
                    acao();
                }
                catch (Exception ex)
                {
                    Log.Error("Falha ao limpar recursos da janela", ex);
                }
            }

            if (Primaria)
            {
                SalvarJanela();
            }

            _host.RemoverJanela(this);
        };

        Activated += (_, _) => _host.JanelaAtiva = this;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>A janela principal guarda posicao/tamanho na configuracao.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primaria { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? CaminhoInicial { get; set; }

    /// <summary>Pasta cujo relatório de tamanho esta janela mostra (null = explorador normal).</summary>
    public string? RelatorioPasta { get; }

    /// <summary>Abas extras que esta janela deve abrir junto da pasta inicial (arraste de abas).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public List<string> AbasIniciais { get; } = new();

    /// <summary>Posição pedida na criação (janela que nasce sob o cursor, no arraste de abas).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? PosicaoForcada { get; set; }

    /// <summary>Tamanho pedido na criação (janela de prévia rápida, por exemplo).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size? TamanhoForcado { get; set; }

    /// <summary>Arquivo mostrado pela prévia rápida (null = janela normal).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? PreviaArquivo { get; set; }

    /// <summary>Pasta exibida agora: e o destino de um arraste vindo de outra janela.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? PastaAtual { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Pronta => _interfacePronta;

    public bool ContemPonto(int x, int y) => Bounds.Contains(x, y);

    public void FocarInterface()
    {
        if (_interfacePronta)
        {
            _web.Focus();
        }
    }

    public void Avisar(string evt, object? data) => Emitir(evt, data);

    /// <summary>Registra algo para executar quando esta janela fechar (ex.: matar o ffmpeg).</summary>
    public void AoFechar(Action acao) => _aoFechar.Add(acao);

    /// <summary>Entrega um evento para a interface desta janela (se ela ja carregou).</summary>
    public void Enviar(string evt, object? data)
    {
        if (_interfacePronta)
        {
            _bridge?.Emit(evt, data);
            return;
        }

        // Interface ainda carregando (aba movida logo depois de abrir a janela): guarda.
        lock (_pendentes)
        {
            _pendentes.Add((evt, data));
        }
    }

    private readonly List<(string Evento, object? Dados)> _pendentes = new();

    private void DespejarPendentes()
    {
        (string Evento, object? Dados)[] lista;
        lock (_pendentes)
        {
            if (_pendentes.Count == 0)
            {
                return;
            }

            lista = _pendentes.ToArray();
            _pendentes.Clear();
        }

        foreach (var (evento, dados) in lista)
        {
            _bridge?.Emit(evento, dados);
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AplicarBarraTituloEscura();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    private const int MONITOR_DEFAULTTONEAREST = 2;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            // Sem área não-cliente: a janela não tem mais borda cinza nem barra nativa.
            case WM_NCCALCSIZE when m.WParam != IntPtr.Zero:
                m.Result = IntPtr.Zero;
                return;

            // Maximizada não pode cobrir a barra de tarefas (área de trabalho do monitor).
            case WM_GETMINMAXINFO:
            {
                var monitor = MonitorFromWindow(Handle, MONITOR_DEFAULTTONEAREST);
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };

                if (GetMonitorInfo(monitor, ref info))
                {
                    var mmi = Marshal.PtrToStructure<MINMAXINFO>(m.LParam);
                    mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
                    mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
                    mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
                    mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
                    Marshal.StructureToPtr(mmi, m.LParam, false);
                    m.Result = IntPtr.Zero;
                    return;
                }

                break;
            }

            // Bordas de redimensionamento, já que a moldura nativa não existe mais.
            case WM_NCHITTEST:
            {
                base.WndProc(ref m);
                var tela = new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
                var cliente = PointToClient(tela);
                var borda = (int)Math.Round(5 * DeviceDpi / 96.0);
                var esquerda = cliente.X <= borda;
                var direita = cliente.X >= ClientSize.Width - borda;
                var topo = cliente.Y <= borda;
                var fundo = cliente.Y >= ClientSize.Height - borda;

                if (WindowState == FormWindowState.Maximized)
                {
                    m.Result = (IntPtr)HTCLIENT;
                    return;
                }

                m.Result = (IntPtr)((topo, fundo, esquerda, direita) switch
                {
                    (true, _, true, _) => HTTOPLEFT,
                    (true, _, _, true) => HTTOPRIGHT,
                    (_, true, true, _) => HTBOTTOMLEFT,
                    (_, true, _, true) => HTBOTTOMRIGHT,
                    (true, _, _, _) => HTTOP,
                    (_, true, _, _) => HTBOTTOM,
                    (_, _, true, _) => HTLEFT,
                    (_, _, _, true) => HTRIGHT,
                    _ => HTCLIENT,
                });
                return;
            }
        }

        base.WndProc(ref m);
    }

    /// <summary>Arrasta a janela pela barra de título feita em HTML.</summary>
    public void ArrastarJanela()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            // Sai do maximizado mantendo o cursor sob o ponto agarrado.
            var cursor = Cursor.Position;
            var proporcao = ClientSize.Width > 0 ? (double)cursor.X / Bounds.Width : 0.5;
            WindowState = FormWindowState.Normal;

            var largura = RestoreBounds.Width;
            Bounds = new Rectangle(
                cursor.X - (int)(largura * proporcao),
                cursor.Y - (int)(24 * DeviceDpi / 96.0),
                largura,
                RestoreBounds.Height);
        }

        ReleaseCapture();

        // O loop de arraste do Windows bloqueia ate soltar: sai da thread do IPC primeiro.
        BeginInvoke(() =>
        {
            // Enquanto a janela é arrastada, o host observa se ela foi solta sobre a barra
            // de abas de outra janela (aí as abas se fundem).
            _host.Abas.IniciarJanela(this);
            SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            Emitir("janela.estado", new { maximizada = WindowState == FormWindowState.Maximized });
        });
    }

    public void MinimizarJanela() => WindowState = FormWindowState.Minimized;

    /// <summary>
    /// Fecha sem passar pelo que a interface faria (a janela temporária do arraste de abas
    /// já entregou o conteúdo para outra).
    /// </summary>
    public void FecharSilencioso()
    {
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao fechar janela", ex);
        }
    }

    public void AlternarMaximizado()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;

        Emitir("janela.estado", new { maximizada = WindowState == FormWindowState.Maximized });
    }

    private async Task IniciarAsync()
    {
        try
        {
            var pastaDados = Path.Combine(AppPaths.Root, "webview2");
            Directory.CreateDirectory(pastaDados);

            // Cada janela precisa do seu proprio ambiente? Nao: o ambiente pode ser
            // compartilhado e economiza memoria e processos.
            var ambiente = await _host.AmbienteWebViewAsync(pastaDados).ConfigureAwait(true);
            await _web.EnsureCoreWebView2Async(ambiente);

            var core = _web.CoreWebView2;
            var settings = core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = true;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;
            // Ctrl+F, F5 etc. passam para a pagina em vez de virar atalho de navegador.
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsSwipeNavigationEnabled = false;

            core.SetVirtualHostNameToFolderMapping(
                AppPaths.UiVirtualHost, AppPaths.UiDir, CoreWebView2HostResourceAccessKind.Allow);
            core.SetVirtualHostNameToFolderMapping(
                AppPaths.ThumbsVirtualHost, AppPaths.ThumbsDir, CoreWebView2HostResourceAccessKind.Allow);

            _bridge = new IpcBridge(
                core,
                _host.Routes,
                _host.Cancels,
                () => _host.DefinirContexto(this));

            core.NavigationCompleted += async (_, _) =>
            {
                _interfacePronta = true;
                _web.Focus();
                DespejarPendentes();
                Emitir("app.ready", new { versao = AppInfo.Versao });
                await VerificarAtualizacoesAsync();
            };

            core.Navigate($"https://{AppPaths.UiVirtualHost}/index.html");
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(
                "O componente WebView2 (Microsoft Edge) nao esta instalado.\n"
                + "Instale o \"Evergreen WebView2 Runtime\" e abra o CV Explorer novamente.",
                "CV Explorer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao iniciar a interface", ex);
            MessageBox.Show($"Falha ao iniciar a interface:\n{ex.Message}", AppInfo.Nome,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private async Task VerificarAtualizacoesAsync()
    {
        if (!_host.Settings.VerificarAtualizacoes || !Primaria)
        {
            return;
        }

        try
        {
            var resultado = await _host.Updates.CheckAsync(baixar: false);
            if (resultado is { } r)
            {
                var tipo = r.GetType();
                var disponivel = tipo.GetProperty("disponivel")?.GetValue(r) as bool? ?? false;
                if (disponivel)
                {
                    Emitir("update.available", new
                    {
                        versao = tipo.GetProperty("versao")?.GetValue(r)?.ToString(),
                        atual = tipo.GetProperty("atual")?.GetValue(r)?.ToString(),
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Falha na verificacao de atualizacoes", ex);
        }
    }

    private void Emitir(string evt, object? data) => Enviar(evt, data);

    /// <summary>Converte o ponto do arraste (tela, pixels físicos) para a área da página.</summary>
    private (int x, int y) PontoDoMouse(int telaX, int telaY)
    {
        var ponto = _web.PointToClient(new Point(telaX, telaY));
        return (ponto.X, ponto.Y);
    }

    private void RestaurarJanela()
    {
        var b = _host.Settings.Janela;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 820);

        var largura = Math.Clamp(b.Width, MinimumSize.Width, area.Width);
        var altura = Math.Clamp(b.Height, MinimumSize.Height, area.Height);
        var x = Math.Clamp(b.X, area.Left, Math.Max(area.Left, area.Right - largura));
        var y = Math.Clamp(b.Y, area.Top, Math.Max(area.Top, area.Bottom - altura));

        Bounds = new Rectangle(x, y, largura, altura);

        if (b.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    /// <summary>Janelas novas nascem deslocadas para nao esconder a anterior.</summary>
    private void PosicionarDeslocada()
    {
        var referencia = _host.Janelas.FirstOrDefault(j => !j.IsDisposed);
        var baseRet = referencia?.Bounds ?? new Rectangle(120, 90, 1280, 820);
        var area = Screen.FromPoint(new Point(baseRet.X + 40, baseRet.Y + 40)).WorkingArea;

        var largura = referencia?.Width ?? baseRet.Width;
        var altura = referencia?.Height ?? baseRet.Height;
        var x = Math.Min(area.Right - largura, baseRet.X + 48);
        var y = Math.Min(area.Bottom - altura, baseRet.Y + 48);

        Bounds = new Rectangle(x, y, largura, altura);
    }

    private void SalvarJanela()
    {
        var b = _host.Settings.Janela;
        b.Maximized = WindowState == FormWindowState.Maximized;

        if (WindowState == FormWindowState.Normal)
        {
            b.X = Bounds.X;
            b.Y = Bounds.Y;
            b.Width = Bounds.Width;
            b.Height = Bounds.Height;
        }

        try
        {
            _host.Store.SaveCurrent();
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao salvar configuracao", ex);
        }
    }

    private void AplicarBarraTituloEscura()
    {
        try
        {
            var ativo = 1;
            DwmSetWindowAttribute(Handle, 20, ref ativo, sizeof(int));

            // Cantos arredondados do Windows 11 também em janela sem moldura.
            var arredondado = 2;
            DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref arredondado, sizeof(int));
        }
        catch
        {
            // Windows 10 antigo simplesmente mantem a barra clara
        }
    }
}
