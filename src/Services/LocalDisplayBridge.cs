using System.Net;
using System.Text;
using System.Text.Json;
using Serilog;

namespace MenuBuPrinterAgent.Services;

public sealed class LocalDisplayBridge : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Func<LocalDisplayStateRequest, Task<LocalDisplayStateResult>> _stateHandler;
    private readonly Func<LocalDisplayStatusResult> _statusHandler;
    private readonly string[] _allowedOrigins;
    private readonly int _port;
    private Task? _listenerTask;
    private bool _started;
    private bool _disposed;

    public LocalDisplayBridge(
        Func<LocalDisplayStateRequest, Task<LocalDisplayStateResult>> stateHandler,
        Func<LocalDisplayStatusResult> statusHandler,
        int port = 9077,
        string[]? allowedOrigins = null)
    {
        _stateHandler = stateHandler ?? throw new ArgumentNullException(nameof(stateHandler));
        _statusHandler = statusHandler ?? throw new ArgumentNullException(nameof(statusHandler));
        _port = port;
        _allowedOrigins = allowedOrigins is { Length: > 0 }
            ? allowedOrigins
            : new[] { "https://menubu.com.tr", "https://www.menubu.com.tr", "https://menubu.tr", "https://www.menubu.tr" };
    }

    public void Start()
    {
        if (_started || _disposed)
        {
            return;
        }

        var prefix = $"http://127.0.0.1:{_port}/";
        _listener.Prefixes.Add(prefix);
        _listener.Start();
        _listenerTask = Task.Run(() => ListenAsync(_cts.Token));
        _started = true;
        Log.Information("Lokal müşteri ekranı köprüsü başladı: {Prefix}", prefix);
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleContextAsync(context), cancellationToken);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpListenerException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Lokal müşteri ekranı köprüsü dinleme hatası");
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context)
    {
        using var response = context.Response;
        try
        {
            if (context.Request.RemoteEndPoint?.Address is not { } clientAddress || !IPAddress.IsLoopback(clientAddress))
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                return;
            }

            ApplyCors(response, context.Request.Headers["Origin"]);

            if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "";
            if (string.Equals(path, "/display/status", StringComparison.OrdinalIgnoreCase)
                && string.Equals(context.Request.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(response, _statusHandler());
                return;
            }

            if (!string.Equals(path, "/display/state", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                return;
            }

            if (!string.Equals(context.Request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
            {
                response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding ?? Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                await WriteJsonAsync(response, new { success = false, message = "İstek gövdesi boş." }, HttpStatusCode.BadRequest);
                return;
            }

            LocalDisplayStateRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<LocalDisplayStateRequest>(body, JsonOptions);
            }
            catch (JsonException ex)
            {
                await WriteJsonAsync(response, new { success = false, message = $"Geçersiz JSON: {ex.Message}" }, HttpStatusCode.BadRequest);
                return;
            }

            if (request == null || request.State.ValueKind != JsonValueKind.Object)
            {
                await WriteJsonAsync(response, new { success = false, message = "state alanı gerekli." }, HttpStatusCode.BadRequest);
                return;
            }

            var result = await _stateHandler(request);
            await WriteJsonAsync(response, result, result.Success ? HttpStatusCode.OK : HttpStatusCode.Accepted);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Lokal müşteri ekranı köprüsü isteği işlenemedi");
        }
    }

    private void ApplyCors(HttpListenerResponse response, string? origin)
    {
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Max-Age"] = "86400";
        response.Headers["Access-Control-Allow-Private-Network"] = "true";

        if (origin != null && _allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            response.Headers["Access-Control-Allow-Origin"] = origin;
            return;
        }

        response.Headers["Access-Control-Allow-Origin"] = "null";
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        response.ContentType = "application/json; charset=utf-8";
        response.StatusCode = (int)statusCode;
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var buffer = Encoding.UTF8.GetBytes(json);
        await response.OutputStream.WriteAsync(buffer);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
        catch
        {
            // ignored
        }

        _listener.Close();
        _cts.Dispose();
    }
}

public sealed class LocalDisplayStateRequest
{
    public int BusinessId { get; set; }
    public JsonElement State { get; set; }
}

public sealed class LocalDisplayStateResult
{
    public bool Success { get; set; }
    public bool DisplayOpen { get; set; }
    public string? Message { get; set; }
}

public sealed class LocalDisplayStatusResult
{
    public bool Success { get; set; } = true;
    public bool DisplayOpen { get; set; }
    public int BusinessId { get; set; }
    public string Version { get; set; } = MenuBuPrinterAgent.Program.AppVersion;
}
