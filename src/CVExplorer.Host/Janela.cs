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

    public Janela(AppHost host, bool primaria, string? caminhoInicial)
    {
        _host = host;
        Primaria = primaria;
        CaminhoInicial = caminhoInicial;

        Text = AppInfo.Nome;
        MinimumSize = new Size(940, 600);
        BackColor = Color.FromArgb(14, 15, 17);
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;

        if (primaria)
        {
            RestaurarJanela();
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
            }
        };

        _web.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] caminhos)
            {
                Emitir("os.drop", new { caminhos });
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
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AplicarBarraTituloEscura();
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
        }
        catch
        {
            // Windows 10 antigo simplesmente mantem a barra clara
        }
    }
}
