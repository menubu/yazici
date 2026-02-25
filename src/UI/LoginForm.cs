using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MenuBuPrinterAgent.Services;
using Serilog;

namespace MenuBuPrinterAgent.UI;

/// <summary>
/// Giriş formu - modern iki kolonlu tasarım.
/// </summary>
public class LoginForm : Form
{
    private readonly SettingsManager _settings;
    private readonly ApiClient _api;
    private TextBox _emailTextBox = null!;
    private TextBox _passwordTextBox = null!;
    private Button _loginButton = null!;
    private Label _statusLabel = null!;
    private CheckBox _rememberCheckBox = null!;
    private CheckBox _showPasswordCheckBox = null!;

    public LoginForm(SettingsManager settings, ApiClient api)
    {
        _settings = settings;
        _api = api;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "MenuBu Printer Agent - Güvenli Giriş";
        Size = new Size(860, 500);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(241, 245, 249);

        var leftPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 320,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        Controls.Add(leftPanel);

        var brandLabel = new Label
        {
            Text = "MenuBu\nPrinter Agent",
            Font = new Font("Segoe UI", 24, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(28, 36),
            Size = new Size(260, 88)
        };
        leftPanel.Controls.Add(brandLabel);

        var tagLabel = new Label
        {
            Text = "Yeni nesil yazdırma altyapısı",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(148, 163, 184),
            Location = new Point(30, 130),
            AutoSize = true
        };
        leftPanel.Controls.Add(tagLabel);

        var featuresLabel = new Label
        {
            Text = "• Zengin ve hızlı baskı modu\n" +
                   "• WebSocket + polling otomatik dengeleme\n" +
                   "• Yerel güvenli log ve hata takibi\n" +
                   "• Çoklu yazıcı eşleştirme",
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(203, 213, 225),
            Location = new Point(30, 200),
            Size = new Size(260, 120)
        };
        leftPanel.Controls.Add(featuresLabel);

        var securityBox = new Panel
        {
            Location = new Point(20, 360),
            Size = new Size(280, 95),
            BackColor = Color.FromArgb(30, 41, 59)
        };
        leftPanel.Controls.Add(securityBox);

        var securityTitle = new Label
        {
            Text = "Güvenlik Notu",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(12, 10),
            AutoSize = true
        };
        securityBox.Controls.Add(securityTitle);

        var securityText = new Label
        {
            Text = "Şifreler ajan tarafında saklanmaz.\nToken tabanlı doğrulama kullanılır.",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(191, 219, 254),
            Location = new Point(12, 34),
            Size = new Size(250, 40)
        };
        securityBox.Controls.Add(securityText);

        var rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(241, 245, 249)
        };
        Controls.Add(rightPanel);

        var authCard = new Panel
        {
            Location = new Point(120, 42),
            Size = new Size(390, 400),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        rightPanel.Controls.Add(authCard);

        var welcomeTitle = new Label
        {
            Text = "Hesabınıza giriş yapın",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            Location = new Point(32, 28),
            AutoSize = true
        };
        authCard.Controls.Add(welcomeTitle);

        var welcomeText = new Label
        {
            Text = "Yazdırma kuyruğunu yönetmek için yetkili hesap bilgilerinizi girin.",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(32, 58),
            Size = new Size(320, 35)
        };
        authCard.Controls.Add(welcomeText);

        var emailLabel = new Label
        {
            Text = "E-posta",
            Font = new Font("Segoe UI", 10),
            Location = new Point(32, 108),
            AutoSize = true
        };
        authCard.Controls.Add(emailLabel);

        _emailTextBox = new TextBox
        {
            Location = new Point(32, 132),
            Size = new Size(324, 32),
            Font = new Font("Segoe UI", 11),
            PlaceholderText = "ornek@menubu.com"
        };
        if (!string.IsNullOrWhiteSpace(_settings.Settings.UserEmail))
        {
            _emailTextBox.Text = _settings.Settings.UserEmail;
        }
        authCard.Controls.Add(_emailTextBox);

        var passwordLabel = new Label
        {
            Text = "Şifre",
            Font = new Font("Segoe UI", 10),
            Location = new Point(32, 176),
            AutoSize = true
        };
        authCard.Controls.Add(passwordLabel);

        _passwordTextBox = new TextBox
        {
            Location = new Point(32, 200),
            Size = new Size(324, 32),
            Font = new Font("Segoe UI", 11),
            UseSystemPasswordChar = true,
            PlaceholderText = "Parolanız"
        };
        authCard.Controls.Add(_passwordTextBox);

        _rememberCheckBox = new CheckBox
        {
            Text = "E-postamı hatırla",
            Font = new Font("Segoe UI", 9),
            Location = new Point(32, 244),
            AutoSize = true,
            Checked = true
        };
        authCard.Controls.Add(_rememberCheckBox);

        _showPasswordCheckBox = new CheckBox
        {
            Text = "Şifreyi göster",
            Font = new Font("Segoe UI", 9),
            Location = new Point(226, 244),
            AutoSize = true
        };
        _showPasswordCheckBox.CheckedChanged += (_, _) =>
        {
            _passwordTextBox.UseSystemPasswordChar = !_showPasswordCheckBox.Checked;
        };
        authCard.Controls.Add(_showPasswordCheckBox);

        _loginButton = new Button
        {
            Text = "Giriş Yap",
            Location = new Point(32, 278),
            Size = new Size(324, 42),
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            Cursor = Cursors.Hand
        };
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.Click += async (_, _) => await LoginAsync();
        authCard.Controls.Add(_loginButton);

        _statusLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(185, 28, 28),
            Location = new Point(32, 326),
            Size = new Size(324, 22),
            TextAlign = ContentAlignment.MiddleCenter
        };
        authCard.Controls.Add(_statusLabel);

        var consentLabel = new Label
        {
            Text = "Devam ederek güvenlik ve gizlilik politikalarını kabul etmiş olursunuz.",
            Font = new Font("Segoe UI", 8),
            ForeColor = Color.FromArgb(100, 116, 139),
            Location = new Point(32, 354),
            Size = new Size(324, 16)
        };
        authCard.Controls.Add(consentLabel);

        var policyLink = new LinkLabel
        {
            Text = "Politikayı görüntüle",
            Font = new Font("Segoe UI", 8.5f),
            Location = new Point(32, 372),
            AutoSize = true
        };
        policyLink.LinkClicked += (_, _) => OpenUrl("https://menubu.com.tr/gizlilik-politikasi");
        authCard.Controls.Add(policyLink);

        AcceptButton = _loginButton;
        _passwordTextBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                _ = LoginAsync();
            }
        };
    }

    private async Task LoginAsync()
    {
        var email = _emailTextBox.Text.Trim();
        var password = _passwordTextBox.Text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
        {
            _statusLabel.Text = "E-posta ve şifre gerekli";
            return;
        }

        _loginButton.Enabled = false;
        _loginButton.Text = "Giriş yapılıyor...";
        _statusLabel.Text = "";

        try
        {
            var result = await _api.LoginAsync(email, password);

            if (result.Success)
            {
                _settings.Settings.UserEmail = _rememberCheckBox.Checked ? email : null;
                _settings.Save();
                Log.Information("Giriş başarılı");
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _statusLabel.Text = result.Message ?? "Giriş başarısız";
                Log.Warning("Giriş başarısız: {Message}", result.Message);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Bağlantı hatası";
            Log.Error(ex, "Giriş hatası");
        }
        finally
        {
            _loginButton.Enabled = true;
            _loginButton.Text = "Giriş Yap";
        }
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
}
