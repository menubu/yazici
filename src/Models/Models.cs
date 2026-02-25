using System.Text.Json;
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
    
    // Yazıcı ID eşleştirmeleri (cloud_printers tablosundaki id -> Windows printer name)
    public Dictionary<int, string> PrinterIdMappings { get; set; } = new();
    
    // Ajan tarafında devre dışı bırakılan yazıcı ID'leri
    public HashSet<int> DisabledPrinterIds { get; set; } = new();

    // Uygulama ayarları
    public bool LaunchAtStartup { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool EnableNotificationSound { get; set; } = true;
    public bool ShowPreviewBeforePrint { get; set; } = false;
    public bool EnableWebSocket { get; set; } = true;
    public bool AutoDisableWebSocketOnErrors { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 1;  // Hızlı yazdırma için 1 saniye

    // Güvenlik / gizlilik onayları
    public bool SecurityConsentAccepted { get; set; }
    public DateTime? SecurityConsentAcceptedAt { get; set; }
    public string SecurityConsentVersion { get; set; } = "";
    
    // Yazdırma modu: "rich" = Zengin HTML (yavaş), "fast" = Basit text (hızlı)
    public string PrintMode { get; set; } = "rich";

    // Bağlantı ayarları
    public string ApiBaseUrl { get; set; } = "https://menubu.com.tr";
    public string WebSocketUrl { get; set; } = "wss://menubu.com.tr/ws/";

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
    public JsonElement? PayloadRaw { get; set; }
    
    // String olarak gelen payload için
    [JsonIgnore]
    public string? PayloadJson 
    { 
        get 
        {
            if (PayloadRaw == null) return null;
            if (PayloadRaw.Value.ValueKind == JsonValueKind.String)
                return PayloadRaw.Value.GetString();
            if (PayloadRaw.Value.ValueKind == JsonValueKind.Object)
                return PayloadRaw.Value.GetRawText();
            return null;
        }
    }

    [JsonPropertyName("printer_tags")]
    public JsonElement? PrinterTagsRaw { get; set; }
    
    // printer_tags hem string hem array olabilir - esnek parse
    [JsonIgnore]
    public List<string>? PrinterTags
    {
        get
        {
            if (PrinterTagsRaw == null) return null;
            
            try
            {
                if (PrinterTagsRaw.Value.ValueKind == JsonValueKind.Array)
                {
                    var list = new List<string>();
                    foreach (var item in PrinterTagsRaw.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            list.Add(item.GetString() ?? "");
                    }
                    return list.Count > 0 ? list : null;
                }
                else if (PrinterTagsRaw.Value.ValueKind == JsonValueKind.String)
                {
                    var str = PrinterTagsRaw.Value.GetString();
                    if (!string.IsNullOrEmpty(str))
                    {
                        // JSON string olarak gelmiş olabilir
                        try
                        {
                            return JsonSerializer.Deserialize<List<string>>(str);
                        }
                        catch
                        {
                            return new List<string> { str };
                        }
                    }
                }
            }
            catch
            {
                // Parse hatası - null dön
            }
            return null;
        }
    }

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
    [JsonConverter(typeof(FlexibleStringListConverter))]
    public List<string>? Lines { get; set; }

    [JsonPropertyName("escpos")]
    public string? EscPos { get; set; }

    [JsonPropertyName("printer_width")]
    public string PrinterWidth { get; set; } = "58mm";

    [JsonPropertyName("printer_tags")]
    [JsonConverter(typeof(FlexibleStringListConverter))]
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

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("job")]
    public PrintJob? Job { get; set; }

    [JsonPropertyName("jobs")]
    public List<PrintJob>? Jobs { get; set; }
}

/// <summary>
/// Accepts arrays/strings/scalars and normalizes to list of strings.
/// Prevents payload parse failures when WS payload shape is inconsistent.
/// </summary>
public sealed class FlexibleStringListConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            return ParseElement(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }

    private static List<string>? ParseElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
            {
                var list = new List<string>();
                foreach (var item in element.EnumerateArray())
                {
                    var str = ToStringValue(item);
                    if (!string.IsNullOrWhiteSpace(str))
                    {
                        list.Add(str);
                    }
                }
                return list.Count > 0 ? list : null;
            }
            case JsonValueKind.String:
            {
                var raw = element.GetString();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return null;
                }

                try
                {
                    var nested = JsonSerializer.Deserialize<List<string>>(raw);
                    if (nested is { Count: > 0 })
                    {
                        return nested.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                    }
                }
                catch
                {
                    // Not a JSON array string. Fall back to single value.
                }

                return new List<string> { raw };
            }
            default:
            {
                var scalar = ToStringValue(element);
                if (string.IsNullOrWhiteSpace(scalar))
                {
                    return null;
                }
                return new List<string> { scalar };
            }
        }
    }

    private static string? ToStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Object => element.GetRawText(),
            JsonValueKind.Array => element.GetRawText(),
            _ => null
        };
    }
}
