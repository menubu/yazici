using System.Drawing;
using System.Windows.Forms;
using MenuBuPrinterAgent.Services;
using Serilog;

namespace MenuBuPrinterAgent.UI;

/// <summary>
/// Giriş formu - modern tasarım
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

    public LoginForm(SettingsManager settings, ApiClient api)
    {
        _settings = settings;
        _api = api;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "MenuBu Printer Agent - Giriş";
        Size = new Size(420, 360);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(243, 247, 252);

        var cardPanel = new Panel
        {
            Location = new Point(20, 20),
            Size = new Size(364, 292),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(cardPanel);

        // Başlık
        var titleLabel = new Label
        {
            Text = "MenuBu Printer Agent",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 23, 42),
            AutoSize = true,
            Location = new Point(70, 20)
        };
        cardPanel.Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = "Siparişlerinizi anında yazdırın",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(71, 85, 105),
            AutoSize = true,
            Location = new Point(102, 52)
        };
        cardPanel.Controls.Add(subtitleLabel);

        // Email
        var emailLabel = new Label
        {
            Text = "E-posta",
            Font = new Font("Segoe UI", 10),
            Location = new Point(35, 92),
            AutoSize = true
        };
        cardPanel.Controls.Add(emailLabel);

        _emailTextBox = new TextBox
        {
            Location = new Point(35, 115),
            Size = new Size(292, 30),
            Font = new Font("Segoe UI", 11),
            PlaceholderText = "ornek@menubu.com"
        };
        if (!string.IsNullOrEmpty(_settings.Settings.UserEmail))
        {
            _emailTextBox.Text = _settings.Settings.UserEmail;
        }
        cardPanel.Controls.Add(_emailTextBox);

        // Şifre
        var passwordLabel = new Label
        {
            Text = "Şifre",
            Font = new Font("Segoe UI", 10),
            Location = new Point(35, 153),
            AutoSize = true
        };
        cardPanel.Controls.Add(passwordLabel);

        _passwordTextBox = new TextBox
        {
            Location = new Point(35, 176),
            Size = new Size(292, 30),
            Font = new Font("Segoe UI", 11),
            UseSystemPasswordChar = true,
            PlaceholderText = "••••••••"
        };
        cardPanel.Controls.Add(_passwordTextBox);

        // Beni hatırla
        _rememberCheckBox = new CheckBox
        {
            Text = "Beni hatırla",
            Font = new Font("Segoe UI", 9),
            Location = new Point(35, 214),
            Checked = true,
            AutoSize = true
        };
        cardPanel.Controls.Add(_rememberCheckBox);

        // Giriş butonu
        _loginButton = new Button
        {
            Text = "Giriş Yap",
            Location = new Point(35, 240),
            Size = new Size(292, 40),
            BackColor = Color.FromArgb(37, 99, 235),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.Click += async (s, e) => await LoginAsync();
        cardPanel.Controls.Add(_loginButton);

        // Durum
        _statusLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(185, 28, 28),
            Location = new Point(35, 284),
            Size = new Size(292, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };
        cardPanel.Controls.Add(_statusLabel);

        // Enter tuşu
        AcceptButton = _loginButton;
        _passwordTextBox.KeyDown += (s, e) =>
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
}
