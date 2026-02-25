using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using System.Text.Json;
using MenuBuPrinterAgent.Services;
using MenuBuPrinterAgent.Models;
using Serilog;

namespace MenuBuPrinterAgent.UI;

/// <summary>
/// Yazıcı ayarları formu - Çoklu yazıcı eşleştirme desteği
/// </summary>
public class PrinterSettingsForm : Form
{
    private readonly SettingsManager _settings;
    private readonly ApiClient? _apiClient;
    private ComboBox _defaultPrinterComboBox = null!;
    private ComboBox _widthComboBox = null!;
    private TrackBar _fontSizeTrackBar = null!;
    private Label _fontSizeLabel = null!;
    private Button _saveButton = null!;
    private Button _testButton = null!;
    private Button _refreshButton = null!;
    private Panel _mappingsPanel = null!;
    private List<CloudPrinter> _cloudPrinters = new();
    private Dictionary<int, ComboBox> _printerMappingCombos = new();
    private Dictionary<int, CheckBox> _printerActiveCheckboxes = new();

    public PrinterSettingsForm(SettingsManager settings, ApiClient? apiClient = null)
    {
        _settings = settings;
        _apiClient = apiClient;
        InitializeComponent();
        LoadLocalPrinters();
        _ = LoadCloudPrintersAsync();
    }

    private void InitializeComponent()
    {
        Text = "Yazıcı Ayarları";
        Size = new Size(500, 600);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;
        AutoScroll = true;

        int yPos = 20;

        // === VARSAYILAN YAZICI ===
        var defaultLabel = new Label
        {
            Text = "Varsayılan Yazıcı",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(20, yPos),
            AutoSize = true
        };
        Controls.Add(defaultLabel);
        yPos += 25;

        _defaultPrinterComboBox = new ComboBox
        {
            Location = new Point(20, yPos),
            Size = new Size(440, 30),
            Font = new Font("Segoe UI", 10),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        Controls.Add(_defaultPrinterComboBox);
        yPos += 45;

        // === KAĞIT GENİŞLİĞİ ===
        var widthLabel = new Label
        {
            Text = "Varsayılan Kağıt Genişliği",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, yPos),
            AutoSize = true
        };
        Controls.Add(widthLabel);
        yPos += 25;

        _widthComboBox = new ComboBox
        {
            Location = new Point(20, yPos),
            Size = new Size(200, 30),
            Font = new Font("Segoe UI", 10),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _widthComboBox.Items.AddRange(new object[] { "58mm (Standart)", "80mm (Geniş)" });
        _widthComboBox.SelectedIndex = _settings.Settings.PrinterWidth.StartsWith("80") ? 1 : 0;
        Controls.Add(_widthComboBox);
        yPos += 45;

        // === YAZI BOYUTU ===
        var fontLabel = new Label
        {
            Text = "Yazı Boyutu Ayarı",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, yPos),
            AutoSize = true
        };
        Controls.Add(fontLabel);
        yPos += 25;

        _fontSizeTrackBar = new TrackBar
        {
            Location = new Point(20, yPos),
            Size = new Size(300, 45),
            Minimum = -3,
            Maximum = 3,
            Value = _settings.Settings.FontSizeAdjustment,
            TickStyle = TickStyle.Both
        };
        _fontSizeTrackBar.ValueChanged += (s, e) =>
        {
            _fontSizeLabel.Text = _fontSizeTrackBar.Value switch
            {
                < 0 => $"Küçük ({_fontSizeTrackBar.Value})",
                > 0 => $"Büyük (+{_fontSizeTrackBar.Value})",
                _ => "Normal (0)"
            };
        };
        Controls.Add(_fontSizeTrackBar);

        _fontSizeLabel = new Label
        {
            Text = "Normal (0)",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            Location = new Point(330, yPos + 10),
            AutoSize = true
        };
        Controls.Add(_fontSizeLabel);
        yPos += 55;

        // === ÇOKLU YAZICI EŞLEŞTİRME ===
        var separator = new Label
        {
            Text = "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.LightGray,
            Location = new Point(20, yPos),
            AutoSize = true
        };
        Controls.Add(separator);
        yPos += 20;

        var mappingHeader = new Label
        {
            Text = "Yazıcı Eşleştirmeleri (Panel'deki Yazıcılar)",
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Location = new Point(20, yPos),
            AutoSize = true
        };
        Controls.Add(mappingHeader);
        yPos += 25;

        var mappingInfo = new Label
        {
            Text = "Panel'de tanımladığınız her yazıcıyı bilgisayarınızdaki yazıcıyla eşleştirin.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            Location = new Point(20, yPos),
            AutoSize = true
        };
        Controls.Add(mappingInfo);
        yPos += 25;

        _refreshButton = new Button
        {
            Text = "Yazıcıları Yenile",
            Location = new Point(20, yPos),
            Size = new Size(150, 30),
            BackColor = Color.FromArgb(241, 245, 249),
            ForeColor = Color.FromArgb(71, 85, 105),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9),
            Cursor = Cursors.Hand
        };
        _refreshButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        _refreshButton.Click += async (s, e) => await LoadCloudPrintersAsync();
        Controls.Add(_refreshButton);
        yPos += 40;

        // Yazıcı eşleştirme paneli
        _mappingsPanel = new Panel
        {
            Location = new Point(20, yPos),
            Size = new Size(440, 150),
            AutoScroll = true,
            BackColor = Color.FromArgb(248, 250, 252),
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(_mappingsPanel);
        yPos += 165;

        // === BUTONLAR ===
        _testButton = new Button
        {
            Text = "Test Yazdır",
            Location = new Point(20, yPos),
            Size = new Size(140, 45),
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10),
            Cursor = Cursors.Hand
        };
        _testButton.FlatAppearance.BorderSize = 0;
        _testButton.Click += TestPrint;
        Controls.Add(_testButton);

        _saveButton = new Button
        {
            Text = "Kaydet",
            Location = new Point(320, yPos),
            Size = new Size(140, 45),
            BackColor = Color.FromArgb(34, 197, 94),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _saveButton.FlatAppearance.BorderSize = 0;
        _saveButton.Click += SaveSettings;
        Controls.Add(_saveButton);
    }

    private void LoadLocalPrinters()
    {
        _defaultPrinterComboBox.Items.Clear();

        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            _defaultPrinterComboBox.Items.Add(printer);
        }

        var currentPrinter = _settings.Settings.DefaultPrinterName;
        if (!string.IsNullOrEmpty(currentPrinter))
        {
            var index = _defaultPrinterComboBox.Items.IndexOf(currentPrinter);
            if (index >= 0)
            {
                _defaultPrinterComboBox.SelectedIndex = index;
            }
        }

        if (_defaultPrinterComboBox.SelectedIndex < 0 && _defaultPrinterComboBox.Items.Count > 0)
        {
            _defaultPrinterComboBox.SelectedIndex = 0;
        }
    }

    private async Task LoadCloudPrintersAsync()
    {
        _mappingsPanel.Controls.Clear();
        _printerMappingCombos.Clear();
        _printerActiveCheckboxes.Clear();

        var loadingLabel = new Label
        {
            Text = "⏳ Yazıcılar yükleniyor...",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            Location = new Point(10, 10),
            AutoSize = true
        };
        _mappingsPanel.Controls.Add(loadingLabel);

        try
        {
            if (_apiClient == null)
            {
                loadingLabel.Text = "⚠️ API bağlantısı yok. Lütfen önce giriş yapın.";
                loadingLabel.ForeColor = Color.Orange;
                return;
            }

            var response = await _apiClient.GetCloudPrintersAsync();
            
            if (response == null || !response.Success || response.Printers == null)
            {
                loadingLabel.Text = "⚠️ Panel'de tanımlı yazıcı bulunamadı.";
                loadingLabel.ForeColor = Color.Orange;
                return;
            }

            _cloudPrinters = response.Printers;
            _mappingsPanel.Controls.Clear();

            if (_cloudPrinters.Count == 0)
            {
                var noDataLabel = new Label
                {
                    Text = "Panel'de henüz yazıcı tanımlanmamış.\nPanel → Yazıcı Ayarları'ndan yazıcı ekleyin.",
                    Font = new Font("Segoe UI", 9),
                    ForeColor = Color.Gray,
                    Location = new Point(10, 10),
                    Size = new Size(400, 40)
                };
                _mappingsPanel.Controls.Add(noDataLabel);
                return;
            }

            int yPos = 10;
            foreach (var cloudPrinter in _cloudPrinters)
            {
                // Aktif/Pasif Checkbox
                var activeCheckbox = new CheckBox
                {
                    Text = "",
                    Checked = !_settings.IsPrinterDisabled(cloudPrinter.Id),
                    Location = new Point(10, yPos),
                    Size = new Size(20, 20),
                    Tag = cloudPrinter.Id
                };
                _mappingsPanel.Controls.Add(activeCheckbox);
                _printerActiveCheckboxes[cloudPrinter.Id] = activeCheckbox;

                // Cloud yazıcı adı
                var nameLabel = new Label
                {
                    Text = $"📋 {cloudPrinter.PrinterName} ({cloudPrinter.PrinterWidth})",
                    Font = new Font("Segoe UI", 9, FontStyle.Bold),
                    Location = new Point(35, yPos + 2),
                    AutoSize = true
                };
                if (cloudPrinter.IsDefault)
                {
                    nameLabel.Text += " ⭐";
                }
                _mappingsPanel.Controls.Add(nameLabel);
                yPos += 25;

                // Windows yazıcı seçimi label
                var selectLabel = new Label
                {
                    Text = "→ Lokal yazıcı:",
                    Font = new Font("Segoe UI", 8),
                    ForeColor = Color.Gray,
                    Location = new Point(35, yPos),
                    AutoSize = true
                };
                _mappingsPanel.Controls.Add(selectLabel);

                // Windows yazıcı seçimi
                var printerCombo = new ComboBox
                {
                    Location = new Point(130, yPos - 3),
                    Size = new Size(280, 25),
                    Font = new Font("Segoe UI", 9),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Tag = cloudPrinter.Id
                };

                printerCombo.Items.Add("-- Seçin --");
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    printerCombo.Items.Add(printer);
                }

                // Mevcut eşleşmeyi seç
                var existingMapping = _settings.Settings.PrinterIdMappings.GetValueOrDefault(cloudPrinter.Id, "");
                if (!string.IsNullOrEmpty(existingMapping))
                {
                    var index = printerCombo.Items.IndexOf(existingMapping);
                    printerCombo.SelectedIndex = index >= 0 ? index : 0;
                }
                else
                {
                    printerCombo.SelectedIndex = 0;
                }

                _mappingsPanel.Controls.Add(printerCombo);
                _printerMappingCombos[cloudPrinter.Id] = printerCombo;
                yPos += 35;
            }

            // Panel yüksekliğini ayarla
            if (yPos > 150)
            {
                _mappingsPanel.AutoScrollMinSize = new Size(0, yPos);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cloud yazıcıları yüklenirken hata");
            loadingLabel.Text = "❌ Yazıcılar yüklenemedi: " + ex.Message;
            loadingLabel.ForeColor = Color.Red;
        }
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        var defaultPrinter = _defaultPrinterComboBox.SelectedItem?.ToString() ?? "";
        var width = _widthComboBox.SelectedIndex == 1 ? "80mm" : "58mm";

        // Varsayılan yazıcı ayarları
        _settings.UpdatePrinter(defaultPrinter, width);
        _settings.Settings.FontSizeAdjustment = _fontSizeTrackBar.Value;

        int enabledCount = 0;
        int disabledCount = 0;

        // Yazıcı eşleştirmelerini ve aktif/pasif durumlarını kaydet
        foreach (var kvp in _printerMappingCombos)
        {
            var cloudPrinterId = kvp.Key;
            var combo = kvp.Value;
            var selectedPrinter = combo.SelectedItem?.ToString() ?? "";
            
            if (selectedPrinter != "-- Seçin --" && !string.IsNullOrEmpty(selectedPrinter))
            {
                _settings.UpdatePrinterIdMapping(cloudPrinterId, selectedPrinter);
            }
            else
            {
                // Eşleşme kaldırıldıysa sil
                if (_settings.Settings.PrinterIdMappings.ContainsKey(cloudPrinterId))
                {
                    _settings.Settings.PrinterIdMappings.Remove(cloudPrinterId);
                }
            }

            // Aktif/Pasif durumunu kaydet
            if (_printerActiveCheckboxes.TryGetValue(cloudPrinterId, out var checkbox))
            {
                bool isDisabled = !checkbox.Checked;
                _settings.SetPrinterDisabled(cloudPrinterId, isDisabled);
                if (isDisabled) disabledCount++;
                else enabledCount++;
            }
        }
        
        _settings.Save();

        Log.Information("Yazıcı ayarları kaydedildi: Varsayılan={Printer}, Aktif={Enabled}, Pasif={Disabled}", 
            defaultPrinter, enabledCount, disabledCount);
        
        MessageBox.Show(
            $"Ayarlar kaydedildi!\n\n" +
            $"Varsayılan yazıcı: {defaultPrinter}\n" +
            $"Aktif yazıcı: {enabledCount}\n" +
            $"Pasif yazıcı: {disabledCount}",
            "Başarılı", 
            MessageBoxButtons.OK, 
            MessageBoxIcon.Information);
        
        Close();
    }

    private void TestPrint(object? sender, EventArgs e)
    {
        var printer = _defaultPrinterComboBox.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(printer))
        {
            MessageBox.Show("Lütfen bir yazıcı seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var mappingsText = _settings.Settings.PrinterIdMappings.Count > 0
                ? string.Join("\n", _settings.Settings.PrinterIdMappings.Select(kv => $"  • ID {kv.Key} → {kv.Value}"))
                : "  (Henüz eşleşme yok)";

            var testHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: Arial; font-size: 12px; text-align: center; padding: 10px; }}
        h2 {{ margin: 10px 0; color: #2563eb; }}
        .line {{ border-top: 1px dashed #000; margin: 10px 0; }}
        .info {{ font-size: 10px; color: #666; }}
        .success {{ color: #16a34a; font-weight: bold; }}
    </style>
</head>
<body>
    <h2>🖨️ MenuBu Printer Agent</h2>
    <div class=""line""></div>
    <p class=""success"">✓ Test Yazdırma Başarılı</p>
    <p><strong>Yazıcı:</strong> {printer}</p>
    <p><strong>Tarih:</strong> {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>
    <div class=""line""></div>
    <p class=""info"">Eşleştirmeler:</p>
    <pre style=""font-size:9px;text-align:left;"">{mappingsText}</pre>
    <div class=""line""></div>
    <p class=""info"">Versiyon: {Program.AppVersion}</p>
</body>
</html>";

            var width = _widthComboBox.SelectedIndex == 1 ? "80mm" : "58mm";
            var printService = new PrintService(_settings);
            var result = printService.PrintHtmlAsync(testHtml, printer, width).GetAwaiter().GetResult();

            if (result.Success)
            {
                MessageBox.Show("Test fişi yazdırıldı!", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show($"Yazdırma hatası: {result.Error}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Test yazdırma hatası");
            MessageBox.Show($"Hata: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>
/// Cloud yazıcı modeli
/// </summary>
public class CloudPrinter
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public int Id { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("printer_name")]
    public string PrinterName { get; set; } = "";
    
    [System.Text.Json.Serialization.JsonPropertyName("printer_type")]
    public string PrinterType { get; set; } = "all";
    
    [System.Text.Json.Serialization.JsonPropertyName("printer_width")]
    public string PrinterWidth { get; set; } = "58mm";
    
    [System.Text.Json.Serialization.JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}

/// <summary>
/// Cloud yazıcıları API yanıtı
/// </summary>
public class CloudPrintersResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("success")]
    public bool Success { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("business_id")]
    public int BusinessId { get; set; }
    
    [System.Text.Json.Serialization.JsonPropertyName("printers")]
    public List<CloudPrinter>? Printers { get; set; }
}
