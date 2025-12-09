using System.Text.Json.Serialization;

namespace MenuBuPrinterAgent.Models;

/// <summary>
/// Kullanıcı ayarları - token tabanlı, şifre saklanmaz
/// </summary>
public class UserSettings
{
    public string? AuthToken { get; set; }
    public int BusinessId { get; set; }
    public string? BusinessName { get; set; }
    public string? UserEmail { get; set; }
    public DateTime? TokenExpiry { get; set; }

    // Yazıcı ayarları
    public string DefaultPrinterName { get; set; } = "";
    public string PrinterWidth { get; set; } = "58mm";
    public int FontSizeAdjustment { get; set; } = 0;

    // Yazıcı eşleştirmeleri (tag -> printer name)
    public Dictionary<string, string> PrinterMappings { get; set; } = new();

    // Uygulama ayarları
    public bool LaunchAtStartup { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool EnableNotificationSound { get; set; } = true;
    public bool ShowPreviewBeforePrint { get; set; } = false;
    public bool EnableWebSocket { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 3;

    // Bağlantı ayarları
    public string ApiBaseUrl { get; set; } = "https://menubu.com.tr";
    public string WebSocketUrl { get; set; } = "wss://menubu.com.tr/ws/print-jobs";

    [JsonIgnore]
    public bool IsLoggedIn => !string.IsNullOrEmpty(AuthToken) && BusinessId > 0;

    [JsonIgnore]
    public bool IsTokenExpired => TokenExpiry.HasValue && TokenExpiry.Value < DateTime.UtcNow;
}

/// <summary>
/// Sunucudan alınan yazdırma işi
/// </summary>
public class PrintJob
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("printer_id")]
    public int? PrinterId { get; set; }

    [JsonPropertyName("job_type")]
    public string JobType { get; set; } = "receipt";

    [JsonPropertyName("job_kind")]
    public string JobKind { get; set; } = "receipt";

    [JsonPropertyName("payload_version")]
    public int PayloadVersion { get; set; } = 2;

    [JsonPropertyName("payload")]
    public string? PayloadJson { get; set; }

    [JsonPropertyName("printer_tags")]
    public string? PrinterTagsJson { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    // Parsed payload
    [JsonIgnore]
    public PrintPayload? Payload { get; set; }
}

/// <summary>
/// Yazdırma payload'ı
/// </summary>
public class PrintPayload
{
    [JsonPropertyName("payload_version")]
    public int PayloadVersion { get; set; } = 2;

    [JsonPropertyName("job_kind")]
    public string JobKind { get; set; } = "receipt";

    [JsonPropertyName("html")]
    public string? Html { get; set; }

    [JsonPropertyName("print_url")]
    public string? PrintUrl { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("lines")]
    public List<string>? Lines { get; set; }

    [JsonPropertyName("printer_width")]
    public string PrinterWidth { get; set; } = "58mm";

    [JsonPropertyName("printer_tags")]
    public List<string>? PrinterTags { get; set; }

    [JsonPropertyName("options")]
    public Dictionary<string, object>? Options { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonIgnore]
    public string EffectiveUrl => PrintUrl ?? Url ?? "";

    [JsonIgnore]
    public bool HasHtml => !string.IsNullOrWhiteSpace(Html);
}

/// <summary>
/// API yanıtı
/// </summary>
public class ApiResponse<T>
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public T? Data { get; set; }
}

/// <summary>
/// Giriş yanıtı
/// </summary>
public class LoginResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("business_id")]
    public int BusinessId { get; set; }

    [JsonPropertyName("business_name")]
    public string? BusinessName { get; set; }

    [JsonPropertyName("expires_at")]
    public string? ExpiresAt { get; set; }
}

/// <summary>
/// İş listesi yanıtı
/// </summary>
public class JobsResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("business_id")]
    public int BusinessId { get; set; }

    [JsonPropertyName("business_name")]
    public string? BusinessName { get; set; }

    [JsonPropertyName("printer_width")]
    public string? PrinterWidth { get; set; }

    [JsonPropertyName("jobs")]
    public List<PrintJob>? Jobs { get; set; }
}

/// <summary>
/// WebSocket mesajı
/// </summary>
public class WebSocketMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("job")]
    public PrintJob? Job { get; set; }

    [JsonPropertyName("jobs")]
    public List<PrintJob>? Jobs { get; set; }
}
