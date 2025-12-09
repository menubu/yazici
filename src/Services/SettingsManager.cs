using System.IO;
using System.Text.Json;
using MenuBuPrinterAgent.Models;
using Serilog;

namespace MenuBuPrinterAgent.Services;

/// <summary>
/// Kullanıcı ayarlarını yönetir - güvenli token saklama
/// </summary>
public class SettingsManager
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MenuBuPrinterAgent");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private UserSettings _settings = new();
    private readonly object _lock = new();

    public UserSettings Settings
    {
        get
        {
            lock (_lock)
            {
                return _settings;
            }
        }
    }

    public SettingsManager()
    {
        EnsureSettingsFolder();
        Load();
    }

    private void EnsureSettingsFolder()
    {
        if (!Directory.Exists(SettingsFolder))
        {
            Directory.CreateDirectory(SettingsFolder);
            Log.Information("Ayarlar klasörü oluşturuldu: {Path}", SettingsFolder);
        }

        // Logs klasörünü de oluştur
        var logsFolder = Path.Combine(SettingsFolder, "logs");
        if (!Directory.Exists(logsFolder))
        {
            Directory.CreateDirectory(logsFolder);
        }
    }

    public void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    _settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions) ?? new UserSettings();
                    Log.Debug("Ayarlar yüklendi: BusinessId={BusinessId}", _settings.BusinessId);
                }
                else
                {
                    _settings = new UserSettings();
                    Log.Information("Varsayılan ayarlar oluşturuldu");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ayarlar yüklenirken hata oluştu");
                _settings = new UserSettings();
            }
        }
    }

    public void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_settings, JsonOptions);
                File.WriteAllText(SettingsFile, json);
                Log.Debug("Ayarlar kaydedildi");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ayarlar kaydedilirken hata oluştu");
            }
        }
    }

    public void UpdateToken(string token, int businessId, string businessName, DateTime? expiry = null)
    {
        lock (_lock)
        {
            _settings.AuthToken = token;
            _settings.BusinessId = businessId;
            _settings.BusinessName = businessName;
            _settings.TokenExpiry = expiry ?? DateTime.UtcNow.AddDays(30);
            Save();
            Log.Information("Token güncellendi: BusinessId={BusinessId}, Name={Name}", businessId, businessName);
        }
    }

    public void ClearToken()
    {
        lock (_lock)
        {
            _settings.AuthToken = null;
            _settings.BusinessId = 0;
            _settings.BusinessName = null;
            _settings.UserEmail = null;
            _settings.TokenExpiry = null;
            Save();
            Log.Information("Token temizlendi");
        }
    }

    public void UpdatePrinter(string printerName, string width)
    {
        lock (_lock)
        {
            _settings.DefaultPrinterName = printerName;
            _settings.PrinterWidth = width;
            Save();
            Log.Information("Yazıcı güncellendi: {Printer}, {Width}", printerName, width);
        }
    }

    public void UpdatePrinterMapping(string tag, string printerName)
    {
        lock (_lock)
        {
            _settings.PrinterMappings[tag] = printerName;
            Save();
            Log.Information("Yazıcı eşleşmesi güncellendi: {Tag} -> {Printer}", tag, printerName);
        }
    }

    public string? GetPrinterForTag(string tag)
    {
        lock (_lock)
        {
            if (_settings.PrinterMappings.TryGetValue(tag, out var printer))
            {
                return printer;
            }
            return _settings.DefaultPrinterName;
        }
    }

    public static string GetLogsFolder() => Path.Combine(SettingsFolder, "logs");
}
