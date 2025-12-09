using System.Net.Http;
using System.Net.Http.Headers;
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
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        _http.DefaultRequestHeaders.Add("User-Agent", $"MenuBuPrinterAgent/{Program.AppVersion}");
    }

    private string BaseUrl => _settings.Settings.ApiBaseUrl;

    /// <summary>
    /// Email/şifre ile giriş yap ve token al
    /// </summary>
    public async Task<LoginResponse> LoginAsync(string email, string password)
    {
        try
        {
            Log.Information("Giriş yapılıyor: {Email}", email);

            var content = new StringContent(
                JsonSerializer.Serialize(new { email, password }),
                Encoding.UTF8,
                "application/json");

            var response = await _http.PostAsync($"{BaseUrl}/api/printer-agent-login.php", content);
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
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/printer-agent-validate.php");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request);
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
            var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/print-jobs.php?agent_version={Program.AppVersion}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request);
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

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/print-jobs.php?id={jobId}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = content;

            var response = await _http.SendAsync(request);
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
    public async Task SendHeartbeatAsync()
    {
        var token = _settings.Settings.AuthToken;
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        try
        {
            var payload = new
            {
                agent_version = Program.AppVersion,
                printer_name = _settings.Settings.DefaultPrinterName,
                printer_width = _settings.Settings.PrinterWidth
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/printer-agent-heartbeat.php");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = content;

            await _http.SendAsync(request);
            Log.Debug("Heartbeat gönderildi");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Heartbeat gönderilemedi");
        }
    }

    /// <summary>
    /// HTML içeriği URL'den al
    /// </summary>
    public async Task<string?> FetchHtmlFromUrlAsync(string url)
    {
        try
        {
            var response = await _http.GetAsync(url);
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}
