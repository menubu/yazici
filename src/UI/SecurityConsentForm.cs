using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace MenuBuPrinterAgent.UI;

/// <summary>
/// İlk açılışta güvenlik ve gizlilik onayı alır.
/// </summary>
public class SecurityConsentForm : Form
{
    private readonly CheckBox _privacyCheckBox = null!;
    private readonly CheckBox _localStorageCheckBox = null!;
    private readonly Button _acceptButton = null!;

    public SecurityConsentForm(string policyVersion, DateTime? previousAcceptedAt)
    {
        Text = "Güvenlik ve Gizlilik Onayı";
        Size = new Size(700, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(246, 248, 252);

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        Controls.Add(headerPanel);

        var titleLabel = new Label
        {
            Text = "MenuBu Printer Agent",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(24, 16),
            AutoSize = true
        };
        headerPanel.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = $"Güvenlik ve Gizlilik Politikası Onayı (Sürüm: {policyVersion})",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(191, 219, 254),
            Location = new Point(24, 50),
            AutoSize = true
        };
        headerPanel.Controls.Add(subtitleLabel);

        var cardPanel = new Panel
        {
            Location = new Point(20, 105),
            Size = new Size(644, 345),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(cardPanel);

        var infoLabel = new Label
        {
            Text = "Uygulamayı kullanmadan önce aşağıdaki maddeleri onaylamanız gerekir:\n\n" +
                   "1. Sipariş ve yazdırma kayıtları yerel bilgisayarınızda log dosyası olarak tutulur.\n" +
                   "2. Kimlik doğrulama için güvenli token kullanılır, şifre bu ajan tarafından saklanmaz.\n" +
                   "3. Bağlantı, durum ve hata verileri servis sürekliliği için işlenir.\n" +
                   "4. Yazdırma işlerinin doğruluğu için sunucu ile veri alışverişi yapılır.\n",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(30, 41, 59),
            Location = new Point(20, 18),
            Size = new Size(600, 130)
        };
        cardPanel.Controls.Add(infoLabel);

        var linksLabel = new Label
        {
            Text = "Politikaları inceleyin:",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Location = new Point(20, 150),
            AutoSize = true
        };
        cardPanel.Controls.Add(linksLabel);

        var privacyLink = new LinkLabel
        {
            Text = "Gizlilik Politikası",
            Font = new Font("Segoe UI", 9),
            Location = new Point(20, 172),
            AutoSize = true
        };
        privacyLink.LinkClicked += (_, _) => OpenUrl("https://menubu.com.tr/gizlilik-politikasi");
        cardPanel.Controls.Add(privacyLink);

        var securityLink = new LinkLabel
        {
            Text = "Güvenlik Bilgilendirmesi",
            Font = new Font("Segoe UI", 9),
            Location = new Point(170, 172),
            AutoSize = true
        };
        securityLink.LinkClicked += (_, _) => OpenUrl("https://menubu.com.tr");
        cardPanel.Controls.Add(securityLink);

        _privacyCheckBox = new CheckBox
        {
            Text = "Gizlilik politikasını okudum ve kabul ediyorum.",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 210),
            Size = new Size(590, 24)
        };
        _privacyCheckBox.CheckedChanged += (_, _) => UpdateAcceptButtonState();
        cardPanel.Controls.Add(_privacyCheckBox);

        _localStorageCheckBox = new CheckBox
        {
            Text = "Yerel log ve güvenlik kayıtlarının tutulmasına onay veriyorum.",
            Font = new Font("Segoe UI", 10),
            Location = new Point(20, 238),
            Size = new Size(590, 24)
        };
        _localStorageCheckBox.CheckedChanged += (_, _) => UpdateAcceptButtonState();
        cardPanel.Controls.Add(_localStorageCheckBox);

        var previousInfo = previousAcceptedAt.HasValue
            ? $"Önceki onay tarihi: {previousAcceptedAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Bu cihazda henüz gizlilik onayı verilmedi.";
        var previousLabel = new Label
        {
            Text = previousInfo,
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(20, 270),
            Size = new Size(590, 22)
        };
        cardPanel.Controls.Add(previousLabel);

        var footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            BackColor = Color.FromArgb(240, 244, 251)
        };
        Controls.Add(footerPanel);

        var exitButton = new Button
        {
            Text = "Reddet ve Çık",
            Font = new Font("Segoe UI", 10),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(51, 65, 85),
            Location = new Point(360, 14),
            Size = new Size(130, 36),
            DialogResult = DialogResult.Cancel
        };
        exitButton.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        footerPanel.Controls.Add(exitButton);

        _acceptButton = new Button
        {
            Text = "Kabul Et ve Devam Et",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Location = new Point(500, 14),
            Size = new Size(160, 36),
            Enabled = false
        };
        _acceptButton.FlatAppearance.BorderSize = 0;
        _acceptButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        footerPanel.Controls.Add(_acceptButton);

        CancelButton = exitButton;
    }

    private void UpdateAcceptButtonState()
    {
        _acceptButton.Enabled = _privacyCheckBox.Checked && _localStorageCheckBox.Checked;
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
                "Bağlantı açılamadı. Lütfen URL'yi tarayıcıdan manuel açın:\n" + url,
                "Bilgi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
