using System.Net;
using System.Net.Sockets;

namespace CVExplorer.Host.Features.Media;

/// <summary>
/// Serve o fluxo do ffmpeg por HTTP em 127.0.0.1, com transfer-encoding chunked.
/// O &lt;video&gt; do WebView2 toca enquanto o ffmpeg decodifica: nada de arquivo de
/// previa, nada de esperar o final. So aceita os ids de fluxo que este processo criou.
/// </summary>
public sealed class PlayerServer : IDisposable
{
    private readonly PreviewStreamService _fluxos;
    private readonly string _token = Guid.NewGuid().ToString("N")[..16];
    private readonly object _gate = new();
    private HttpListener? _ouvinte;
    private int _porta;

    public PlayerServer(PreviewStreamService fluxos) => _fluxos = fluxos;

    /// <summary>Sobe o servidor na primeira previa e devolve a URL base com token.</summary>
    public string GarantirUrlBase()
    {
        lock (_gate)
        {
            if (_ouvinte is not null)
            {
                return UrlBase();
            }

            _porta = PortaLivre();
            var ouvinte = new HttpListener();
            ouvinte.Prefixes.Add($"http://127.0.0.1:{_porta}/");
            ouvinte.Start();
            _ouvinte = ouvinte;

            _ = Task.Run(() => AtenderAsync(ouvinte));
            return UrlBase();
        }
    }

    public string UrlDoFluxo(string id) => $"{UrlBase()}/{id}";

    private string UrlBase() => $"http://127.0.0.1:{_porta}/{_token}";

    private static int PortaLivre()
    {
        var sonda = new TcpListener(IPAddress.Loopback, 0);
        sonda.Start();
        var porta = ((IPEndPoint)sonda.LocalEndpoint).Port;
        sonda.Stop();
        return porta;
    }

    private async Task AtenderAsync(HttpListener ouvinte)
    {
        while (ouvinte.IsListening)
        {
            HttpListenerContext contexto;
            try
            {
                contexto = await ouvinte.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => ResponderAsync(contexto));
        }
    }

    private async Task ResponderAsync(HttpListenerContext contexto)
    {
        var resposta = contexto.Response;

        try
        {
            var partes = contexto.Request.Url?.AbsolutePath.Trim('/').Split('/') ?? Array.Empty<string>();

            // /<token>/<id>
            if (partes.Length != 2 || partes[0] != _token)
            {
                resposta.StatusCode = 404;
                resposta.Close();
                return;
            }

            var fluxo = _fluxos.Obter(partes[1]);
            if (fluxo?.Saida is null)
            {
                resposta.StatusCode = 404;
                resposta.Close();
                return;
            }

            resposta.StatusCode = 200;
            resposta.ContentType = "video/mp4";
            resposta.SendChunked = true;
            resposta.Headers["Cache-Control"] = "no-store";
            resposta.Headers["Access-Control-Allow-Origin"] = "*";

            var buffer = new byte[64 * 1024];
            int lidos;

            while ((lidos = await fluxo.Saida.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                await resposta.OutputStream.WriteAsync(buffer, 0, lidos).ConfigureAwait(false);
                await resposta.OutputStream.FlushAsync().ConfigureAwait(false);
            }

            resposta.Close();
        }
        catch (HttpListenerException)
        {
            // navegador trocou de fonte/parou o video: encerra o fluxo
        }
        catch (IOException)
        {
            // idem
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao servir previa", ex);
        }
        finally
        {
            try
            {
                resposta.Abort();
            }
            catch
            {
                // ja fechado
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try
            {
                _ouvinte?.Stop();
                _ouvinte?.Close();
            }
            catch
            {
                // ja parado
            }

            _ouvinte = null;
        }
    }
}
