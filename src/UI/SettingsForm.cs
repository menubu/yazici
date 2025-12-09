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
    private CheckBox _enableSoundCheckBox = null!;
    private CheckBox _enableWebSocketCheckBox = null!;
    private CheckBox _showPreviewCheckBox = null!;
    private NumericUpDown _pollingIntervalNumeric = null!;

    public SettingsForm(SettingsManager settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Ayarlar";
        Size = new Size(400, 400);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        var yPos = 20;
        var xPos = 30;

        // Başlangıçta çalıştır
        _launchAtStartupCheckBox = new CheckBox
        {
            Text = "Windows başlangıcında otomatik başlat",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_launchAtStartupCheckBox);
        yPos += 35;

        // Bildirimler
        _enableNotificationsCheckBox = new CheckBox
        {
            Text = "Masaüstü bildirimleri göster",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_enableNotificationsCheckBox);
        yPos += 35;

        // Bildirim sesi
        _enableSoundCheckBox = new CheckBox
        {
            Text = "Bildirim sesi çal",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_enableSoundCheckBox);
        yPos += 35;

        // WebSocket
        _enableWebSocketCheckBox = new CheckBox
        {
            Text = "Anlık baskı (WebSocket) kullan",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_enableWebSocketCheckBox);
        yPos += 35;

        // Önizleme
        _showPreviewCheckBox = new CheckBox
        {
            Text = "Yazdırmadan önce önizleme göster",
            Font = new Font("Segoe UI", 10),
            Location = new Point(xPos, yPos),
            AutoSize = true
        };
        Controls.Add(_showPreviewCheckBox);
        yPos += 45;

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
            Value = 3
        };
        Controls.Add(_pollingIntervalNumeric);
        yPos += 50;

        // Ayırıcı
        var separator = new Label
        {
            BorderStyle = BorderStyle.Fixed3D,
            Location = new Point(xPos, yPos),
            Size = new Size(320, 2)
        };
        Controls.Add(separator);
        yPos += 20;

        // Bilgi
        var infoLabel = new Label
        {
            Text = "💡 WebSocket açıkken siparişler anında yazdırılır.\nKapalıyken belirtilen aralıkta kontrol edilir.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            Location = new Point(xPos, yPos),
            Size = new Size(320, 40)
        };
        Controls.Add(infoLabel);
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
        _enableSoundCheckBox.Checked = _settings.Settings.EnableNotificationSound;
        _enableWebSocketCheckBox.Checked = _settings.Settings.EnableWebSocket;
        _showPreviewCheckBox.Checked = _settings.Settings.ShowPreviewBeforePrint;
        _pollingIntervalNumeric.Value = _settings.Settings.PollingIntervalSeconds;
    }

    private void SaveSettings(object? sender, EventArgs e)
    {
        _settings.Settings.LaunchAtStartup = _launchAtStartupCheckBox.Checked;
        _settings.Settings.EnableNotifications = _enableNotificationsCheckBox.Checked;
        _settings.Settings.EnableNotificationSound = _enableSoundCheckBox.Checked;
        _settings.Settings.EnableWebSocket = _enableWebSocketCheckBox.Checked;
        _settings.Settings.ShowPreviewBeforePrint = _showPreviewCheckBox.Checked;
        _settings.Settings.PollingIntervalSeconds = (int)_pollingIntervalNumeric.Value;
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
