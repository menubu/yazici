using System.Drawing;
using System.Windows.Forms;
using MenuBuPrinterAgent.Services;

namespace MenuBuPrinterAgent.UI;

/// <summary>
/// Genel ayarlar formu
/// </summary>
public class SettingsForm : Form
{
    private readonly SettingsManager _settings;
    private CheckBox _launchAtStartupCheckBox = null!;
    private CheckBox _enableNotificationsCheckBox = null!;
    private CheckBox _enableWebSocketCheckBox = null!;
    private CheckBox _autoDisableWebSocketCheckBox = null!;
    private CheckBox _enableSoundCheckBox = null!;
    private CheckBox _showPreviewCheckBox = null!;
    private NumericUpDown _pollingIntervalNumeric = null!;
    private ComboBox _printModeComboBox = null!;

    public SettingsForm(SettingsManager settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Ayarlar";
        Size = new Size(400, 520);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        var yPos = 20;
        var xPos = 30;

        // Yazdırma Modu - ÖNEMLİ, EN ÜSTE
        var printModeLabel = new Label
        {
            Text = "Yazdırma Modu:",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(printModeLabel);
        yPos += 25;

        _printModeComboBox = new ComboBox
        {
            Location = new Point(xPos, yPos),
            Size = new Size(320, 30),
            Font = new Font("Segoe UI", 10),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _printModeComboBox.Items.AddRange(new object[]
        {
            "Zengin Tasarım (10-15 saniye)",
            "Hızlı Basit (2-3 saniye)"
        });
        Controls.Add(_printModeComboBox);
        yPos += 40;

        var modeInfoLabel = new Label
        {
            Text = "Zengin: Güzel fiş, logo, özel fontlar\nHızlı: Basit text, anında yazdırma",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.Gray,
            Location = new Point(xPos, yPos),
            Size = new Size(320, 30)
        };
        Controls.Add(modeInfoLabel);
        yPos += 40;

        // Ayırıcı
        var sep1 = new Label { BorderStyle = BorderStyle.Fixed3D, Location = new Point(xPos, yPos), Size = new Size(320, 2) };
        Controls.Add(sep1);
        yPos += 15;

        // Başlangıçta çalıştır
        _launchAtStartupCheckBox = new CheckBox
        {
            Text = "Windows başlangıcında otomatik başlat",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_launchAtStartupCheckBox);
        yPos += 30;

        // Bildirimler
        _enableNotificationsCheckBox = new CheckBox
        {
            Text = "Masaüstü bildirimleri göster",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_enableNotificationsCheckBox);
        yPos += 30;

        // Bildirim sesi
        _enableSoundCheckBox = new CheckBox
        {
            Text = "Bildirim sesi çal",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_enableSoundCheckBox);
        yPos += 30;

        // WebSocket
        _enableWebSocketCheckBox = new CheckBox
        {
            Text = "Anlık bağlantı (WebSocket) kullan",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_enableWebSocketCheckBox);
        yPos += 30;

        // WebSocket fallback
        _autoDisableWebSocketCheckBox = new CheckBox
        {
            Text = "WebSocket hatasında otomatik polling'e geç",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_autoDisableWebSocketCheckBox);
        yPos += 30;

        // Önizleme
        _showPreviewCheckBox = new CheckBox
        {
            Text = "Yazdırmadan önce önizleme göster",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_showPreviewCheckBox);
        yPos += 40;

        // Polling aralığı
        var pollingLabel = new Label
        {
            Text = "Kontrol aralığı (saniye):",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(pollingLabel);

        _pollingIntervalNumeric = new NumericUpDown
        {
            Location = new Point(xPos + 180, yPos - 3),
            Size = new Size(80, 30),
            Font = new Font("Segoe UI", 10),
            Minimum = 1,
            Maximum = 60,
            Value = 1
        };
        Controls.Add(_pollingIntervalNumeric);
        yPos += 50;

        // Kaydet butonu
        var saveButton = new Button
        {
            Text = "Kaydet",
            Location = new Point(xPos, yPos),
            Size = new Size(320, 40),
            BackColor = Color.FromArgb(34, 197, 94),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += SaveSettings;
        Controls.Add(saveButton);
    }

    private void LoadSettings()
    {
        _launchAtStartupCheckBox.Checked = _settings.Settings.LaunchAtStartup;
        _enableNotificationsCheckBox.Checked = _settings.Settings.EnableNotifications;
        _enableWebSocketCheckBox.Checked = _settings.Settings.EnableWebSocket;
        _autoDisableWebSocketCheckBox.Checked = _settings.Settings.AutoDisableWebSocketOnErrors;
        _enableSoundCheckBox.Checked = _settings.Settings.EnableNotificationSound;
        _showPreviewCheckBox.Checked = _settings.Settings.ShowPreviewBeforePrint;
        _pollingIntervalNumeric.Value = _settings.Settings.PollingIntervalSeconds;
        
        // Print mode
        _printModeComboBox.SelectedIndex = _settings.Settings.PrintMode == "fast" ? 1 : 0;
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        _settings.Settings.LaunchAtStartup = _launchAtStartupCheckBox.Checked;
        _settings.Settings.EnableNotifications = _enableNotificationsCheckBox.Checked;
        _settings.Settings.EnableNotificationSound = _enableSoundCheckBox.Checked;
        _settings.Settings.EnableWebSocket = _enableWebSocketCheckBox.Checked;
        _settings.Settings.AutoDisableWebSocketOnErrors = _autoDisableWebSocketCheckBox.Checked;
        _settings.Settings.ShowPreviewBeforePrint = _showPreviewCheckBox.Checked;
        _settings.Settings.PollingIntervalSeconds = (int)_pollingIntervalNumeric.Value;
        _settings.Settings.PrintMode = _printModeComboBox.SelectedIndex == 1 ? "fast" : "rich";
        _settings.Save();

        // Startup ayarını uygula
        UpdateStartupSetting(_settings.Settings.LaunchAtStartup);

        MessageBox.Show("Ayarlar kaydedildi.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }

    private void UpdateStartupSetting(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null) return;

            const string appName = "MenuBuPrinterAgent";

            if (enabled)
            {
                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue(appName, $"\"{exePath}\"");
                }
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        catch
        {
            // Registry erişim hatası - sessizce devam et
        }
    }
}
