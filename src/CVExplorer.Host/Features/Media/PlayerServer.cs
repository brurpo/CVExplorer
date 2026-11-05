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
    private readonly Dictionary<string, (string Caminho, DateTime Expira)> _arquivos = new(StringComparer.Ordinal);
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

    /// <summary>
    /// Expõe um arquivo do disco por alguns minutos para o renderizador 3D da interface
    /// (three.js precisa buscar os bytes). So arquivos registrados aqui sao servidos.
    /// </summary>
    public string RegistrarArquivo(string caminho) => Registrar(caminho, TimeSpan.FromMinutes(5), out _);

    /// <summary>
    /// Registra a mídia que o player vai tocar direto (sem ffmpeg). Fica valendo enquanto
    /// a prévia estiver aberta; o "id" devolvido serve para liberar depois.
    /// </summary>
    public string RegistrarMidia(string caminho, out string id) =>
        Registrar(caminho, TimeSpan.FromHours(12), out id);

    public void Liberar(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        lock (_gate)
        {
            _arquivos.Remove(id);
        }
    }

    /// <summary>Esquece as mídias registradas destes caminhos (arquivo vai ser movido/apagado).</summary>
    public void LiberarPorCaminho(IEnumerable<string> caminhos)
    {
        var alvos = new HashSet<string>(
            (caminhos ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)),
            StringComparer.OrdinalIgnoreCase);
        if (alvos.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var id in _arquivos.Where(p => alvos.Contains(p.Value.Caminho)).Select(p => p.Key).ToList())
            {
                _arquivos.Remove(id);
            }
        }
    }

    private string Registrar(string caminho, TimeSpan validade, out string id)
    {
        GarantirUrlBase();

        id = Guid.NewGuid().ToString("N")[..16];
        lock (_gate)
        {
            var agora = DateTime.UtcNow;
            foreach (var expirado in _arquivos.Where(p => p.Value.Expira < agora).Select(p => p.Key).ToList())
            {
                _arquivos.Remove(expirado);
            }

            _arquivos[id] = (caminho, agora.Add(validade));
        }

        return $"{UrlBase()}/arquivo/{id}";
    }

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

            // /<token>/arquivo/<id>  -> bytes de um modelo 3D registrado
            if (partes.Length == 3 && partes[0] == _token && partes[1] == "arquivo")
            {
                await ServirArquivoAsync(contexto, partes[2]).ConfigureAwait(false);
                return;
            }

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

    private async Task ServirArquivoAsync(HttpListenerContext contexto, string id)
    {
        var resposta = contexto.Response;

        string? caminho;
        lock (_gate)
        {
            caminho = _arquivos.TryGetValue(id, out var registro) && registro.Expira > DateTime.UtcNow
                ? registro.Caminho
                : null;
        }

        if (caminho is null || !File.Exists(caminho))
        {
            resposta.StatusCode = 404;
            resposta.Close();
            return;
        }

        try
        {
            var info = new FileInfo(caminho);
            var total = info.Length;
            var (inicio, fim) = Faixa(contexto.Request.Headers["Range"], total);

            if (inicio is null)
            {
                // Faixa insatisfeita: o Chromium pede de novo com outro Range.
                resposta.StatusCode = 416;
                resposta.Headers["Content-Range"] = $"bytes */{total}";
                resposta.Close();
                return;
            }

            var parcial = contexto.Request.Headers["Range"] is not null;
            var quantidade = fim - inicio.Value + 1;

            resposta.StatusCode = parcial ? 206 : 200;
            resposta.ContentType = MimeDe(caminho);
            resposta.ContentLength64 = quantidade;
            resposta.Headers["Accept-Ranges"] = "bytes";
            resposta.Headers["Cache-Control"] = "no-store";
            resposta.Headers["Access-Control-Allow-Origin"] = "*";
            if (parcial)
            {
                resposta.Headers["Content-Range"] = $"bytes {inicio}-{fim}/{total}";
            }

            // FileShare.ReadWrite | Delete: apagar/mover o arquivo aberto na previa nao pode falhar.
            await using var arquivo = new FileStream(
                caminho, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024, useAsync: true);

            arquivo.Seek(inicio.Value, SeekOrigin.Begin);

            var buffer = new byte[128 * 1024];
            var restante = quantidade;
            while (restante > 0)
            {
                var lidos = await arquivo.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, restante))).ConfigureAwait(false);
                if (lidos <= 0)
                {
                    break;
                }

                await resposta.OutputStream.WriteAsync(buffer.AsMemory(0, lidos)).ConfigureAwait(false);
                restante -= lidos;
            }

            resposta.Close();
        }
        catch (HttpListenerException)
        {
            // interface trocou de modelo: encerra
        }
        catch (IOException)
        {
            // idem
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao servir modelo {caminho}", ex);
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

    private static (long? Inicio, long Fim) Faixa(string? cabecalho, long total)
    {
        if (string.IsNullOrWhiteSpace(cabecalho) || !cabecalho.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return total > 0 ? (0, total - 1) : (null, 0);
        }

        var valor = cabecalho["bytes=".Length..].Trim();
        var partes = valor.Split('-', 2);
        if (partes.Length != 2 || total == 0)
        {
            return (null, 0);
        }

        if (partes[0].Length == 0)
        {
            // "bytes=-N": os últimos N bytes.
            if (!long.TryParse(partes[1], out var sufixo) || sufixo <= 0)
            {
                return (null, 0);
            }

            var inicioSufixo = Math.Max(0, total - sufixo);
            return (inicioSufixo, total - 1);
        }

        if (!long.TryParse(partes[0], out var inicio) || inicio < 0 || inicio >= total)
        {
            return (null, 0);
        }

        var fim = total - 1;
        if (partes[1].Length > 0 && long.TryParse(partes[1], out var pedido))
        {
            fim = Math.Min(pedido, total - 1);
        }

        return fim < inicio ? (null, 0) : (inicio, fim);
    }

    private static string MimeDe(string caminho) => Path.GetExtension(caminho).ToLowerInvariant() switch
    {
        ".glb" => "model/gltf-binary",
        ".gltf" => "model/gltf+json",
        ".usdz" => "model/vnd.usdz+zip",
        ".mp4" or ".m4v" => "video/mp4",
        ".mov" => "video/quicktime",
        ".webm" => "video/webm",
        _ => "application/octet-stream",
    };

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
