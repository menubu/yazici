using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MenuBuPrinterAgent.Models;
using Serilog;

namespace MenuBuPrinterAgent.Services;

/// <summary>
/// WebSocket istemcisi - anlık yazdırma için
/// </summary>
public class WebSocketClient : IDisposable
{
    private static readonly JsonSerializerOptions PrintJobJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SettingsManager _settings;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _disposed;
    private bool _isConnecting;
    private DateTime _lastConnectAttempt = DateTime.MinValue;
    private int _reconnectDelaySeconds = 5;

    public event Action<List<PrintJob>>? OnJobsReceived;
    public event Action<string>? OnError;
    public event Action? OnConnected;
    public event Action? OnDisconnected;

    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    public WebSocketClient(SettingsManager settings)
    {
        _settings = settings;
    }

    public async Task ConnectAsync()
    {
        if (_isConnecting || IsConnected)
        {
            return;
        }

        var token = _settings.Settings.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            Log.Warning("WebSocket: Token yok, bağlanılamıyor");
            return;
        }

        // Reconnect delay kontrolü
        var timeSinceLastAttempt = DateTime.UtcNow - _lastConnectAttempt;
        if (timeSinceLastAttempt.TotalSeconds < _reconnectDelaySeconds)
        {
            return;
        }

        _isConnecting = true;
        _lastConnectAttempt = DateTime.UtcNow;

        try
        {
            await DisconnectInternalAsync();

            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {token}");
            _webSocket.Options.SetRequestHeader("User-Agent", $"MenuBuPrinterAgent/{Program.AppVersion}");

            _cts = new CancellationTokenSource();

            var wsUrl = _settings.Settings.WebSocketUrl;
            Log.Information("WebSocket bağlanıyor: {Url}", wsUrl);

            // Bazı ortamlarda ConnectAsync süresiz bekleyebiliyor; timeout ekle.
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(TimeSpan.FromSeconds(8));
            await _webSocket.ConnectAsync(new Uri(wsUrl), connectCts.Token);

            if (_webSocket.State == WebSocketState.Open)
            {
                Log.Information("WebSocket bağlantısı kuruldu");
                _reconnectDelaySeconds = 5; // Başarılı bağlantıda sıfırla
                
                // Auth mesajı gönder
                await SendAuthAsync();
                
                OnConnected?.Invoke();

                // Mesaj dinlemeye başla
                _receiveTask = ReceiveLoopAsync(_cts.Token);
            }
        }
        catch (OperationCanceledException ex)
        {
            Log.Warning(ex, "WebSocket bağlantı zaman aşımı");
            _reconnectDelaySeconds = Math.Min(60, _reconnectDelaySeconds * 2); // Exponential backoff
            OnError?.Invoke("WebSocket bağlantı zaman aşımı");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebSocket bağlantı hatası");
            _reconnectDelaySeconds = Math.Min(60, _reconnectDelaySeconds * 2); // Exponential backoff
            OnError?.Invoke(ex.Message);
        }
        finally
        {
            _isConnecting = false;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        try
        {
            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                messageBuffer.Clear();

                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Log.Information("WebSocket sunucu tarafından kapatıldı");
                        await DisconnectInternalAsync();
                        OnDisconnected?.Invoke();
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                }
                while (!result.EndOfMessage);

                if (messageBuffer.Length > 0)
                {
                    await ProcessMessageAsync(messageBuffer.ToString());
                }
            }
        }
        catch (OperationCanceledException)
        {
            Log.Debug("WebSocket receive iptal edildi");
        }
        catch (WebSocketException ex)
        {
            Log.Warning(ex, "WebSocket receive hatası");
            OnError?.Invoke(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebSocket receive beklenmeyen hata");
            OnError?.Invoke(ex.Message);
        }
        finally
        {
            OnDisconnected?.Invoke();
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            Log.Debug("WebSocket mesaj alındı: {Length} karakter", message.Length);

            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var messageType = GetPropertyAsString(root, "type")?.ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(messageType))
            {
                return;
            }

            switch (messageType)
            {
                case "jobs":
                case "new_jobs":
                    var jobs = ParseJobs(root);
                    if (jobs.Count > 0)
                    {
                        Log.Information("WebSocket: {Count} yeni iş alındı", jobs.Count);
                        OnJobsReceived?.Invoke(jobs);
                    }
                    break;

                case "job":
                case "new_job":
                    var singleJob = ParseSingleJob(root);
                    if (singleJob != null)
                    {
                        Log.Information("WebSocket: Yeni iş alındı: {Id}", singleJob.Id);
                        OnJobsReceived?.Invoke(new List<PrintJob> { singleJob });
                    }
                    break;

                case "ping":
                    await SendPongAsync();
                    break;

                case "ready":
                    Log.Information("WebSocket hazır: {BusinessInfo}", GetPropertyAsString(root, "message") ?? "ok");
                    break;

                case "error":
                    var errorMessage = string.IsNullOrWhiteSpace(GetPropertyAsString(root, "message"))
                        ? "WebSocket sunucu hatası"
                        : GetPropertyAsString(root, "message");
                    Log.Warning("WebSocket sunucu hatası: {Message}", errorMessage);
                    OnError?.Invoke(errorMessage);
                    break;

                default:
                    Log.Debug("WebSocket: Bilinmeyen mesaj tipi: {Type}", messageType);
                    break;
            }
        }
        catch (Exception ex)
        {
            var preview = message.Length > 200 ? message[..200] + "..." : message;
            Log.Warning(ex, "WebSocket mesaj işleme hatası. Preview: {Preview}", preview);
            OnError?.Invoke("WebSocket mesaj parse hatası");
        }
    }

    private static List<PrintJob> ParseJobs(JsonElement root)
    {
        if (TryGetPropertyCaseInsensitive(root, "jobs", out var jobsElement))
        {
            return ParseJobsFromElement(jobsElement);
        }

        var single = ParseSingleJob(root);
        return single == null ? new List<PrintJob>() : new List<PrintJob> { single };
    }

    private static PrintJob? ParseSingleJob(JsonElement root)
    {
        if (!TryGetPropertyCaseInsensitive(root, "job", out var jobElement))
        {
            return null;
        }

        return ParseJobFromElement(jobElement);
    }

    private static List<PrintJob> ParseJobsFromElement(JsonElement element)
    {
        var list = new List<PrintJob>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var parsed = ParseJobFromElement(item);
                    if (parsed != null)
                    {
                        list.Add(parsed);
                    }
                }
                break;
            default:
                var single = ParseJobFromElement(element);
                if (single != null)
                {
                    list.Add(single);
                }
                break;
        }

        return list;
    }

    private static PrintJob? ParseJobFromElement(JsonElement element)
    {
        try
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return JsonSerializer.Deserialize<PrintJob>(element.GetRawText(), PrintJobJsonOptions);
                case JsonValueKind.String:
                    var raw = element.GetString();
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        return null;
                    }

                    using (var nestedDoc = JsonDocument.Parse(raw))
                    {
                        if (nestedDoc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            return JsonSerializer.Deserialize<PrintJob>(nestedDoc.RootElement.GetRawText(), PrintJobJsonOptions);
                        }
                    }
                    break;
            }
        }
        catch
        {
            // Parse edilemeyen tekil job'ı atla; üst akış devam etsin.
        }

        return null;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetPropertyAsString(JsonElement root, string propertyName)
    {
        if (!TryGetPropertyCaseInsensitive(root, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };
    }

    private async Task SendPongAsync()
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                var pong = Encoding.UTF8.GetBytes("{\"type\":\"pong\"}");
                await _webSocket.SendAsync(new ArraySegment<byte>(pong), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Pong gönderilemedi");
            }
        }
    }

    private async Task SendAuthAsync()
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                var authMessage = new
                {
                    type = "auth",
                    api_key = _settings.Settings.AuthToken,
                    business_id = _settings.Settings.BusinessId,
                    agent_version = Program.AppVersion
                };
                var json = JsonSerializer.Serialize(authMessage);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                Log.Information("WebSocket auth mesajı gönderildi");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auth mesajı gönderilemedi");
            }
        }
    }

    public async Task DisconnectAsync()
    {
        await DisconnectInternalAsync();
        OnDisconnected?.Invoke();
    }

    private async Task DisconnectInternalAsync()
    {
        try
        {
            _cts?.Cancel();

            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnecting", CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WebSocket kapatma hatası");
        }
        finally
        {
            _webSocket?.Dispose();
            _webSocket = null;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            DisconnectInternalAsync().Wait(TimeSpan.FromSeconds(2));
        }
    }
}
