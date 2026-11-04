using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace CVExplorer.Host.Ipc;

/// <summary>
/// Ponte tipada entre a interface (JS) e o host (C#).
/// Requisicao:  { "id": 1, "cmd": "fs.list", "args": { ... } }
/// Resposta:    { "id": 1, "ok": true,  "data": ... } | { "id": 1, "ok": false, "error": "..." }
/// Evento:      { "evt": "dir.batch", "data": ... }
/// </summary>
public sealed class IpcBridge
{
    private readonly CoreWebView2 _web;
    private readonly Dictionary<string, Route> _routes;
    private readonly CancellationRegistry _cancellations;
    private readonly SynchronizationContext _ui;
    private readonly Action? _aoIniciarComando;

    public sealed record Route(Func<JsonElement, CancellationToken, Task<object?>> Handler, bool UsaOperacao);

    public IpcBridge(
        CoreWebView2 web,
        Dictionary<string, Route> routes,
        CancellationRegistry cancellations,
        Action? aoIniciarComando = null)
    {
        _web = web;
        _routes = routes;
        _cancellations = cancellations;
        _aoIniciarComando = aoIniciarComando;
        // Capturado na criacao (thread da UI): eventos vindos do ffmpeg/copia
        // precisam ser publicados aqui, senao o WebView2 recusa a mensagem.
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _web.WebMessageReceived += OnMessageReceived;
    }

    public void Emit(string evt, object? data)
    {
        string payload;
        try
        {
            payload = JsonSerializer.Serialize(new { evt, data }, Json.Compact);
        }
        catch (Exception ex)
        {
            Log.Error($"Falha ao serializar evento {evt}", ex);
            return;
        }

        _ui.Post(_ =>
        {
            try
            {
                _web.PostWebMessageAsJson(payload);
            }
            catch (Exception ex)
            {
                Log.Error($"Falha ao emitir evento {evt}", ex);
            }
        }, null);
    }

    private async void OnMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        long id = 0;
        try
        {
            // Marca a janela de origem: os eventos da operacao voltam para ela.
            _aoIniciarComando?.Invoke();

            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;

            id = root.TryGetProperty("id", out var idEl) ? idEl.GetInt64() : 0;
            var cmd = root.TryGetProperty("cmd", out var cmdEl) ? cmdEl.GetString() ?? string.Empty : string.Empty;
            var args = root.TryGetProperty("args", out var argsEl) ? argsEl.Clone() : default;

            if (!_routes.TryGetValue(cmd, out var route))
            {
                Respond(id, null, $"Comando desconhecido: {cmd}");
                return;
            }

            using var cts = route.UsaOperacao ? _cancellations.Create(OperacaoId(args)) : new CancellationTokenSource();

            var data = await route.Handler(args, cts.Token).ConfigureAwait(true);
            Respond(id, data, null);
        }
        catch (OperationCanceledException)
        {
            Respond(id, null, "Operacao cancelada.");
        }
        catch (Exception ex)
        {
            Log.Error("Erro ao executar comando IPC", ex);
            Respond(id, null, ex.Message);
        }
    }

    private void Respond(long id, object? data, string? error)
    {
        try
        {
            var payload = error is null
                ? JsonSerializer.Serialize(new { id, ok = true, data }, Json.Compact)
                : JsonSerializer.Serialize(new { id, ok = false, error }, Json.Compact);

            _web.PostWebMessageAsJson(payload);
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao responder IPC", ex);
        }
    }

    private static string OperacaoId(JsonElement args)
    {
        if (args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty("opId", out var el)
            && el.ValueKind == JsonValueKind.String)
        {
            return el.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

/// <summary>Cancellation por operacao: buscar, copiar, converter.</summary>
public sealed class CancellationRegistry
{
    private readonly Dictionary<string, CancellationTokenSource> _map = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public CancellationTokenSource Create(string operacaoId)
    {
        var cts = new CancellationTokenSource();
        if (string.IsNullOrEmpty(operacaoId))
        {
            return cts;
        }

        lock (_gate)
        {
            if (_map.TryGetValue(operacaoId, out var anterior))
            {
                anterior.Cancel();
                anterior.Dispose();
            }

            _map[operacaoId] = cts;
        }

        return cts;
    }

    public void Cancel(string operacaoId)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(operacaoId, out var cts))
            {
                cts.Cancel();
            }
        }
    }
}
