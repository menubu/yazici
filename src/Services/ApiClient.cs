using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using MenuBuPrinterAgent.Models;
using Serilog;

namespace MenuBuPrinterAgent.Services;

/// <summary>
/// MenuBu API istemcisi - token tabanlı kimlik doğrulama
/// </summary>
public class ApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly SettingsManager _settings;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(SettingsManager settings)
    {
        _settings = settings;
        
        // Daha dayanıklı HTTP bağlantısı için SocketsHttpHandler kullan
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(3),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            MaxConnectionsPerServer = 8,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate,
            KeepAlivePingDelay = TimeSpan.FromSeconds(25),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
        };
        
        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _http.DefaultRequestVersion = HttpVersion.Version20;
        _http.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        _http.DefaultRequestHeaders.Add("User-Agent", $"MenuBuPrinterAgent/{Program.AppVersion}");
    }

    private string BaseUrl => _settings.Settings.ApiBaseUrl;

    private static bool IsTransientNetworkError(Exception ex)
    {
        if (ex is TimeoutException || ex is TaskCanceledException || ex is HttpRequestException)
        {
            return true;
        }

        if (ex.InnerException is SocketException socketEx)
        {
            return socketEx.SocketErrorCode is SocketError.ConnectionReset
                or SocketError.ConnectionAborted
                or SocketError.HostNotFound
                or SocketError.TimedOut
                or SocketError.NetworkDown
                or SocketError.NetworkUnreachable
                or SocketError.HostUnreachable
                or SocketError.TryAgain;
        }

        return false;
    }

    private static TimeSpan RetryDelay(int attempt)
    {
        return attempt switch
        {
            1 => TimeSpan.FromMilliseconds(300),
            2 => TimeSpan.FromMilliseconds(1000),
            _ => TimeSpan.FromMilliseconds(2000)
        };
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        int maxAttempts = 3,
        CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = requestFactory();
            try
            {
                var response = await _http.SendAsync(request, cancellationToken);

                if ((int)response.StatusCode >= 500 && attempt < maxAttempts)
                {
                    response.Dispose();
                    await Task.Delay(RetryDelay(attempt), cancellationToken);
                    continue;
                }

                return response;
            }
            catch (Exception ex) when (IsTransientNetworkError(ex) && attempt < maxAttempts)
            {
                lastError = ex;
                await Task.Delay(RetryDelay(attempt), cancellationToken);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw lastError ?? new HttpRequestException("HTTP isteği başarısız oldu");
    }

    /// <summary>
    /// Email/şifre ile giriş yap ve token al
    /// </summary>
    public async Task<LoginResponse> LoginAsync(string email, string password)
    {
        try
        {
            Log.Information("Giriş yapılıyor: {Email}", email);

            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/printer-agent-login.php");
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new { email, password }),
                    Encoding.UTF8,
                    "application/json");
                return request;
            });
            var json = await response.Content.ReadAsStringAsync();

            Log.Debug("Giriş yanıtı: {Status}, {Body}", response.StatusCode, json.Length > 200 ? json[..200] + "..." : json);

            var result = JsonSerializer.Deserialize<LoginResponse>(json, JsonOptions);
            
            if (result?.Success == true && !string.IsNullOrEmpty(result.Token))
            {
                // Token'ı kaydet
                DateTime? expiry = null;
                if (!string.IsNullOrEmpty(result.ExpiresAt) && DateTime.TryParse(result.ExpiresAt, out var exp))
                {
                    expiry = exp;
                }
                
                _settings.UpdateToken(result.Token, result.BusinessId, result.BusinessName ?? "", expiry);
                _settings.Settings.UserEmail = email;
                _settings.Save();

                Log.Information("Giriş başarılı: {BusinessName}", result.BusinessName);
            }
            else
            {
                Log.Warning("Giriş başarısız: {Message}", result?.Message ?? "Bilinmeyen hata");
            }

            return result ?? new LoginResponse { Success = false, Message = "Yanıt işlenemedi" };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Giriş hatası");
            return new LoginResponse { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Token ile doğrulama yap
    /// </summary>
    public async Task<bool> ValidateTokenAsync()
    {
        var token = _settings.Settings.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/printer-agent-validate.php");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            });
            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<ApiResponse<object>>(json, JsonOptions);

            return result?.Success == true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Token doğrulama hatası");
            return false;
        }
    }

    /// <summary>
    /// Bekleyen yazdırma işlerini al
    /// </summary>
    public async Task<JobsResponse?> GetPendingJobsAsync()
    {
        var token = _settings.Settings.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            Log.Warning("Token yok, işler alınamıyor");
            return null;
        }

        try
        {
            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/print-jobs.php?agent_version={Program.AppVersion}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            });
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("İşler alınamadı: {Status}", response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<JobsResponse>(json, JsonOptions);

            if (result?.Jobs != null)
            {
                // Payload'ları parse et
                foreach (var job in result.Jobs)
                {
                    if (!string.IsNullOrEmpty(job.PayloadJson))
                    {
                        try
                        {
                            job.Payload = JsonSerializer.Deserialize<PrintPayload>(job.PayloadJson, JsonOptions);
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, "Job {Id} payload parse hatası", job.Id);
                        }
                    }
                }

                Log.Debug("İşler alındı: {Count} adet", result.Jobs.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "İşler alınırken hata");
            return null;
        }
    }

    /// <summary>
    /// İş durumunu güncelle
    /// </summary>
    public async Task<bool> UpdateJobStatusAsync(int jobId, string status, string? errorMessage = null)
    {
        var token = _settings.Settings.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["error_message"] = errorMessage
            };

            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/print-jobs.php?id={jobId}");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");
                return request;
            });
            var json = await response.Content.ReadAsStringAsync();

            Log.Debug("Job {Id} durum güncellendi: {Status} -> {Response}", jobId, status, response.StatusCode);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Job {Id} durum güncellenirken hata", jobId);
            return false;
        }
    }

    /// <summary>
    /// Heartbeat gönder
    /// </summary>
    public async Task<bool> SendHeartbeatAsync()
    {
        var token = _settings.Settings.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        try
        {
            var payload = new
            {
                agent_version = Program.AppVersion,
                printer_name = _settings.Settings.DefaultPrinterName,
                printer_width = _settings.Settings.PrinterWidth
            };

            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/printer-agent-heartbeat.php");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");
                return request;
            });
            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Heartbeat başarısız döndü: {Status}", response.StatusCode);
                return false;
            }

            Log.Debug("Heartbeat gönderildi");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Heartbeat gönderilemedi");
            return false;
        }
    }

    /// <summary>
    /// HTML içeriği URL'den al
    /// </summary>
    public async Task<string?> FetchHtmlFromUrlAsync(string url)
    {
        try
        {
            using var response = await SendWithRetryAsync(() => new HttpRequestMessage(HttpMethod.Get, url));
            var json = await response.Content.ReadAsStringAsync();

            // JSON olarak dene
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions);
                if (data != null && data.TryGetValue("html", out var htmlObj) && htmlObj is JsonElement elem)
                {
                    return elem.GetString();
                }
            }
            catch
            {
                // JSON değilse direkt HTML olabilir
                if (json.TrimStart().StartsWith("<"))
                {
                    return json;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HTML alınamadı: {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Panel'de tanımlı yazıcıları al
    /// </summary>
    public async Task<UI.CloudPrintersResponse?> GetCloudPrintersAsync()
    {
        var token = _settings.Settings.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            Log.Warning("Token yok, cloud yazıcıları alınamıyor");
            return null;
        }

        try
        {
            using var response = await SendWithRetryAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/cloud-printers.php");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            });
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("Cloud yazıcıları alınamadı: {Status}", response.StatusCode);
                return null;
            }

            var result = JsonSerializer.Deserialize<UI.CloudPrintersResponse>(json, JsonOptions);
            
            if (result?.Printers != null)
            {
                Log.Debug("Cloud yazıcılar alındı: {Count} adet", result.Printers.Count);
            }

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cloud yazıcıları alınırken hata");
            return null;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}
