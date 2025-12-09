using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using MenuBuPrinterAgent.Services;
using Serilog;

namespace MenuBuPrinterAgent.UI;

/// <summary>
/// Yazıcı ayarları formu
/// </summary>
public class PrinterSettingsForm : Form
{
    private readonly SettingsManager _settings;
    private ComboBox _printerComboBox = null!;
    private ComboBox _widthComboBox = null!;
    private TrackBar _fontSizeTrackBar = null!;
    private Label _fontSizeLabel = null!;
    private Button _saveButton = null!;
    private Button _testButton = null!;

    public PrinterSettingsForm(SettingsManager settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadPrinters();
    }

    private void InitializeComponent()
    {
        Text = "Yazıcı Ayarları";
        Size = new Size(400, 350);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        // Yazıcı seçimi
        var printerLabel = new Label
        {
            Text = "Varsayılan Yazıcı",
            Font = new Font("Segoe UI", 10),
            Location = new Point(30, 25),
            AutoSize = true
        };
        Controls.Add(printerLabel);

        _printerComboBox = new ComboBox
        {
            Location = new Point(30, 50),
            Size = new Size(320, 30),
            Font = new Font("Segoe UI", 10),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        Controls.Add(_printerComboBox);

        // Kağıt genişliği
        var widthLabel = new Label
        {
            Text = "Kağıt Genişliği",
            Font = new Font("Segoe UI", 10),
            Location = new Point(30, 95),
            AutoSize = true
        };
        Controls.Add(widthLabel);

        _widthComboBox = new ComboBox
        {
            Location = new Point(30, 120),
            Size = new Size(320, 30),
            Font = new Font("Segoe UI", 10),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _widthComboBox.Items.AddRange(new object[] { "58mm (Standart)", "80mm (Geniş)" });
        _widthComboBox.SelectedIndex = _settings.Settings.PrinterWidth.StartsWith("80") ? 1 : 0;
        Controls.Add(_widthComboBox);

        // Yazı boyutu
        var fontLabel = new Label
        {
            Text = "Yazı Boyutu Ayarı",
            Font = new Font("Segoe UI", 10),
            Location = new Point(30, 165),
            AutoSize = true
        };
        Controls.Add(fontLabel);

        _fontSizeTrackBar = new TrackBar
        {
            Location = new Point(30, 190),
            Size = new Size(250, 45),
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
            Location = new Point(290, 200),
            AutoSize = true
        };
        Controls.Add(_fontSizeLabel);

        // Test butonu
        _testButton = new Button
        {
            Text = "Test Yazdır",
            Location = new Point(30, 250),
            Size = new Size(150, 40),
            BackColor = Color.FromArgb(59, 130, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10),
            Cursor = Cursors.Hand
        };
        _testButton.FlatAppearance.BorderSize = 0;
        _testButton.Click += TestPrint;
        Controls.Add(_testButton);

        // Kaydet butonu
        _saveButton = new Button
        {
            Text = "Kaydet",
            Location = new Point(200, 250),
            Size = new Size(150, 40),
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

    private void LoadPrinters()
    {
        _printerComboBox.Items.Clear();

        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            _printerComboBox.Items.Add(printer);
        }

        // Mevcut yazıcıyı seç
        var currentPrinter = _settings.Settings.DefaultPrinterName;
        if (!string.IsNullOrEmpty(currentPrinter))
        {
            var index = _printerComboBox.Items.IndexOf(currentPrinter);
            if (index >= 0)
            {
                _printerComboBox.SelectedIndex = index;
            }
        }

        // Hiç seçili değilse varsayılanı seç
        if (_printerComboBox.SelectedIndex < 0 && _printerComboBox.Items.Count > 0)
        {
            _printerComboBox.SelectedIndex = 0;
        }
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        var printer = _printerComboBox.SelectedItem?.ToString() ?? "";
        var width = _widthComboBox.SelectedIndex == 1 ? "80mm" : "58mm";

        _settings.UpdatePrinter(printer, width);
        _settings.Settings.FontSizeAdjustment = _fontSizeTrackBar.Value;
        _settings.Save();

        Log.Information("Yazıcı ayarları kaydedildi: {Printer}, {Width}", printer, width);
        MessageBox.Show("Ayarlar kaydedildi.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }

    private void TestPrint(object? sender, EventArgs e)
    {
        var printer = _printerComboBox.SelectedItem?.ToString();
        if (string.IsNullOrEmpty(printer))
        {
            MessageBox.Show("Lütfen bir yazıcı seçin.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var testHtml = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{ font-family: Arial; font-size: 12px; text-align: center; padding: 10px; }}
        h2 {{ margin: 10px 0; }}
        .line {{ border-top: 1px dashed #000; margin: 10px 0; }}
        .info {{ font-size: 10px; color: #666; }}
    </style>
</head>
<body>
    <h2>🖨️ MenuBu Printer Agent</h2>
    <div class=""line""></div>
    <p><strong>Test Yazdırma</strong></p>
    <p>Yazıcı: {printer}</p>
    <p>Tarih: {DateTime.Now:dd.MM.yyyy HH:mm:ss}</p>
    <div class=""line""></div>
    <p class=""info"">Bu bir test fişidir.</p>
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
