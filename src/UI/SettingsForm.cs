using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MenuBuPrinterAgent.Services;

namespace MenuBuPrinterAgent.UI;

/// <summary>
/// Genel ayarlar formu - modern sekmeli görünüm.
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
    private Label _consentStatusLabel = null!;

    public SettingsForm(SettingsManager settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "Ayarlar";
        Size = new Size(800, 610);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(241, 245, 249);

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 84,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        Controls.Add(header);

        var titleLabel = new Label
        {
            Text = "MenuBu Printer Agent Ayarları",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(24, 18),
            AutoSize = true
        };
        header.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = "Yazdırma, bağlantı ve güvenlik tercihlerini bu ekrandan yönetin.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(191, 219, 254),
            Location = new Point(24, 50),
            AutoSize = true
        };
        header.Controls.Add(subtitleLabel);

        var tabs = new TabControl
        {
            Location = new Point(18, 100),
            Size = new Size(748, 440),
            Font = new Font("Segoe UI", 10)
        };
        Controls.Add(tabs);

        var generalTab = new TabPage("Yazdırma");
        var connectionTab = new TabPage("Bağlantı");
        var securityTab = new TabPage("Güvenlik");

        tabs.TabPages.Add(generalTab);
        tabs.TabPages.Add(connectionTab);
        tabs.TabPages.Add(securityTab);

        BuildPrintTab(generalTab);
        BuildConnectionTab(connectionTab);
        BuildSecurityTab(securityTab);

        var saveButton = new Button
        {
            Text = "Kaydet",
            Size = new Size(130, 38),
            Location = new Point(636, 550),
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 163, 74),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        saveButton.FlatAppearance.BorderSize = 0;
        saveButton.Click += SaveSettings;
        Controls.Add(saveButton);

        var cancelButton = new Button
        {
            Text = "Vazgeç",
            Size = new Size(110, 38),
            Location = new Point(516, 550),
            Font = new Font("Segoe UI", 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 65, 85),
            DialogResult = DialogResult.Cancel
        };
        cancelButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        Controls.Add(cancelButton);

        CancelButton = cancelButton;
    }

    private void BuildPrintTab(TabPage tab)
    {
        tab.BackColor = Color.FromArgb(248, 250, 252);

        var card = CreateCardPanel(20, 20, 690, 350);
        tab.Controls.Add(card);

        var title = new Label
        {
            Text = "Yazdırma Davranışı",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(20, 16),
            AutoSize = true
        };
        card.Controls.Add(title);

        var description = new Label
        {
            Text = "Hız ve çıktı kalitesi dengesini belirleyin.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(20, 42),
            AutoSize = true
        };
        card.Controls.Add(description);

        var printModeLabel = new Label
        {
            Text = "Yazdırma modu",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 84),
            AutoSize = true
        };
        card.Controls.Add(printModeLabel);

        _printModeComboBox = new ComboBox
        {
            Location = new Point(20, 108),
            Size = new Size(420, 32),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 10)
        };
        _printModeComboBox.Items.AddRange(new object[]
        {
            "Zengin Tasarım (10-15 saniye)",
            "Hızlı Basit (2-3 saniye)"
        });
        card.Controls.Add(_printModeComboBox);

        var modeInfoLabel = new Label
        {
            Text = "Zengin mod: marka görünümü güçlü fişler\nHızlı mod: minimum bekleme ile seri baskı",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(20, 146),
            Size = new Size(450, 34)
        };
        card.Controls.Add(modeInfoLabel);

        _showPreviewCheckBox = new CheckBox
        {
            Text = "Yazdırmadan önce önizleme göster",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 196),
            AutoSize = true
        };
        card.Controls.Add(_showPreviewCheckBox);

        var pollingLabel = new Label
        {
            Text = "Kuyruk kontrol aralığı (saniye)",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 236),
            AutoSize = true
        };
        card.Controls.Add(pollingLabel);

        _pollingIntervalNumeric = new NumericUpDown
        {
            Location = new Point(20, 260),
            Size = new Size(110, 32),
            Minimum = 1,
            Maximum = 60,
            Font = new Font("Segoe UI", 10)
        };
        card.Controls.Add(_pollingIntervalNumeric);
    }

    private void BuildConnectionTab(TabPage tab)
    {
        tab.BackColor = Color.FromArgb(248, 250, 252);

        var card = CreateCardPanel(20, 20, 690, 350);
        tab.Controls.Add(card);

        var title = new Label
        {
            Text = "Bağlantı ve Bildirim",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(20, 16),
            AutoSize = true
        };
        card.Controls.Add(title);

        var description = new Label
        {
            Text = "Ajanın sunucu ile iletişim ve kullanıcı bildirimi ayarları.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(20, 42),
            AutoSize = true
        };
        card.Controls.Add(description);

        _enableWebSocketCheckBox = new CheckBox
        {
            Text = "Anlık bağlantı (WebSocket) kullan",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 86),
            AutoSize = true
        };
        card.Controls.Add(_enableWebSocketCheckBox);

        _autoDisableWebSocketCheckBox = new CheckBox
        {
            Text = "Hata olursa bu oturumda otomatik polling'e geç",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 116),
            AutoSize = true
        };
        card.Controls.Add(_autoDisableWebSocketCheckBox);

        _enableNotificationsCheckBox = new CheckBox
        {
            Text = "Masaüstü bildirimleri göster",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 160),
            AutoSize = true
        };
        card.Controls.Add(_enableNotificationsCheckBox);

        _enableSoundCheckBox = new CheckBox
        {
            Text = "Bildirim sesi çal",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 190),
            AutoSize = true
        };
        card.Controls.Add(_enableSoundCheckBox);

        _launchAtStartupCheckBox = new CheckBox
        {
            Text = "Windows başlangıcında otomatik başlat",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 234),
            AutoSize = true
        };
        card.Controls.Add(_launchAtStartupCheckBox);
    }

    private void BuildSecurityTab(TabPage tab)
    {
        tab.BackColor = Color.FromArgb(248, 250, 252);

        var card = CreateCardPanel(20, 20, 690, 350);
        tab.Controls.Add(card);

        var title = new Label
        {
            Text = "Güvenlik ve Gizlilik",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(20, 16),
            AutoSize = true
        };
        card.Controls.Add(title);

        var info = new Label
        {
            Text = "Bu ajan; yazdırma, bağlantı ve hata kayıtlarını yerel bilgisayarınızda tutar.\n" +
                   "Politika onayı ilk açılışta alınır ve sürüm bazlı takip edilir.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(20, 44),
            Size = new Size(620, 40)
        };
        card.Controls.Add(info);

        var consentTitle = new Label
        {
            Text = "Onay Durumu",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(20, 104),
            AutoSize = true
        };
        card.Controls.Add(consentTitle);

        _consentStatusLabel = new Label
        {
            Text = "-",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(30, 41, 59),
            Location = new Point(20, 128),
            Size = new Size(640, 42)
        };
        card.Controls.Add(_consentStatusLabel);

        var privacyLink = new LinkLabel
        {
            Text = "Gizlilik politikasını aç",
            Font = new Font("Segoe UI", 9),
            Location = new Point(20, 180),
            AutoSize = true
        };
        privacyLink.LinkClicked += (_, _) => OpenUrl("https://menubu.com.tr/gizlilik-politikasi");
        card.Controls.Add(privacyLink);

        var securityLink = new LinkLabel
        {
            Text = "Güvenlik bilgilendirmesini aç",
            Font = new Font("Segoe UI", 9),
            Location = new Point(190, 180),
            AutoSize = true
        };
        securityLink.LinkClicked += (_, _) => OpenUrl("https://menubu.com.tr");
        card.Controls.Add(securityLink);

        var resetConsentButton = new Button
        {
            Text = "Gizlilik Onayını Sıfırla",
            Font = new Font("Segoe UI", 9),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(185, 28, 28),
            Location = new Point(20, 224),
            Size = new Size(190, 34),
            Cursor = Cursors.Hand
        };
        resetConsentButton.FlatAppearance.BorderColor = Color.FromArgb(254, 202, 202);
        resetConsentButton.Click += (_, _) =>
        {
            _settings.Settings.SecurityConsentAccepted = false;
            _settings.Settings.SecurityConsentAcceptedAt = null;
            _settings.Settings.SecurityConsentVersion = string.Empty;
            _settings.Save();
            UpdateConsentStatusLabel();

            MessageBox.Show(
                "Gizlilik onayı sıfırlandı. Uygulamayı bir sonraki açılışta tekrar onay ekranı gösterilecektir.",
                "Bilgi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        card.Controls.Add(resetConsentButton);
    }

    private static Panel CreateCardPanel(int x, int y, int width, int height)
    {
        return new Panel
        {
            Location = new Point(x, y),
            Size = new Size(width, height),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
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
        _printModeComboBox.SelectedIndex = _settings.Settings.PrintMode == "fast" ? 1 : 0;

        UpdateConsentStatusLabel();
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

        UpdateStartupSetting(_settings.Settings.LaunchAtStartup);

        MessageBox.Show("Ayarlar kaydedildi.", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
    }

    private void UpdateConsentStatusLabel()
    {
        if (!_settings.Settings.SecurityConsentAccepted)
        {
            _consentStatusLabel.Text = "Gizlilik onayı henüz verilmemiş.\nUygulama açılışında onay ekranı gösterilir.";
            _consentStatusLabel.ForeColor = Color.FromArgb(185, 28, 28);
            return;
        }

        var acceptedAt = _settings.Settings.SecurityConsentAcceptedAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "Bilinmiyor";
        var version = string.IsNullOrWhiteSpace(_settings.Settings.SecurityConsentVersion)
            ? "-"
            : _settings.Settings.SecurityConsentVersion;

        _consentStatusLabel.Text = $"Gizlilik onayı aktif.\nOnay tarihi: {acceptedAt} | Politika sürümü: {version}";
        _consentStatusLabel.ForeColor = Color.FromArgb(22, 101, 52);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show(
                "Bağlantı açılamadı. Lütfen URL'yi manuel açın:\n" + url,
                "Bilgi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void UpdateStartupSetting(bool enabled)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);

            if (key == null)
            {
                return;
            }

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
            // Registry erişim hatası durumunda sessizce devam et.
        }
    }
}
