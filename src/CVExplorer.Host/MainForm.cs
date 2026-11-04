using System.Drawing;
using System.Runtime.InteropServices;
using CVExplorer.Host.Ipc;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CVExplorer.Host;

public sealed class MainForm : Form
{
    private readonly AppHost _host = new();
    private readonly WebView2 _web = new();
    private bool _interfacePronta;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public MainForm()
    {
        Text = AppInfo.Nome;
        MinimumSize = new Size(940, 600);
        BackColor = Color.FromArgb(14, 15, 17);
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;

        RestaurarJanela();

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
            }
        };

        Controls.Add(_web);

        Load += async (_, _) =>
        {
            AplicarBarraTituloEscura();
            await IniciarAsync();
        };
        FormClosing += (_, _) => SalvarJanela();
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

            var ambiente = await CoreWebView2Environment.CreateAsync(null, pastaDados);
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

            _host.Bridge = new IpcBridge(core, Commands.Build(_host), _host.Cancels);

            var mapas = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _host.MapearConteudo = caminho =>
            {
                if (string.IsNullOrWhiteSpace(caminho) || !Directory.Exists(caminho))
                {
                    return null;
                }

                lock (mapas)
                {
                    if (mapas.TryGetValue(caminho, out var existente))
                    {
                        return $"https://{existente}";
                    }

                    // Limite de mapeamentos para nao crescer sem controle numa sessao longa.
                    if (mapas.Count >= 96)
                    {
                        mapas.Clear();
                    }

                    var nome = $"c{Guid.NewGuid():N}"[..12] + ".cvexplorer";
                    core.SetVirtualHostNameToFolderMapping(
                        nome, caminho, CoreWebView2HostResourceAccessKind.Allow);

                    mapas[caminho] = nome;
                    return $"https://{nome}";
                }
            };

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
        if (!_host.Settings.VerificarAtualizacoes)
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

    private void Emitir(string evt, object? data)
    {
        if (_interfacePronta)
        {
            _host.Emit(evt, data);
        }
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
