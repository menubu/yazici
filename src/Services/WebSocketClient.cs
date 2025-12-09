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

            await _webSocket.ConnectAsync(new Uri(wsUrl), _cts.Token);

            if (_webSocket.State == WebSocketState.Open)
            {
                Log.Information("WebSocket bağlantısı kuruldu");
                _reconnectDelaySeconds = 5; // Başarılı bağlantıda sıfırla
                OnConnected?.Invoke();

                // Mesaj dinlemeye başla
                _receiveTask = ReceiveLoopAsync(_cts.Token);
            }
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

            var wsMessage = JsonSerializer.Deserialize<WebSocketMessage>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (wsMessage == null)
            {
                return;
            }

            switch (wsMessage.Type?.ToLower())
            {
                case "jobs":
                case "new_jobs":
                    if (wsMessage.Jobs?.Count > 0)
                    {
                        Log.Information("WebSocket: {Count} yeni iş alındı", wsMessage.Jobs.Count);
                        OnJobsReceived?.Invoke(wsMessage.Jobs);
                    }
                    break;

                case "job":
                case "new_job":
                    if (wsMessage.Job != null)
                    {
                        Log.Information("WebSocket: Yeni iş alındı: {Id}", wsMessage.Job.Id);
                        OnJobsReceived?.Invoke(new List<PrintJob> { wsMessage.Job });
                    }
                    break;

                case "ping":
                    await SendPongAsync();
                    break;

                default:
                    Log.Debug("WebSocket: Bilinmeyen mesaj tipi: {Type}", wsMessage.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "WebSocket mesaj işleme hatası");
        }
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
