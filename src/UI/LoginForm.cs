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
        Size = new Size(400, 320);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;

        // Logo / Başlık
        var titleLabel = new Label
        {
            Text = "🍽️ MenuBu Printer Agent",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            ForeColor = Color.FromArgb(139, 92, 246), // Purple
            AutoSize = true,
            Location = new Point(90, 20)
        };
        Controls.Add(titleLabel);

        var subtitleLabel = new Label
        {
            Text = "Siparişlerinizi anında yazdırın",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gray,
            AutoSize = true,
            Location = new Point(115, 50)
        };
        Controls.Add(subtitleLabel);

        // Email
        var emailLabel = new Label
        {
            Text = "E-posta",
            Font = new Font("Segoe UI", 10),
            Location = new Point(50, 90),
            AutoSize = true
        };
        Controls.Add(emailLabel);

        _emailTextBox = new TextBox
        {
            Location = new Point(50, 115),
            Size = new Size(280, 30),
            Font = new Font("Segoe UI", 11),
            PlaceholderText = "ornek@menubu.com"
        };
        if (!string.IsNullOrEmpty(_settings.Settings.UserEmail))
        {
            _emailTextBox.Text = _settings.Settings.UserEmail;
        }
        Controls.Add(_emailTextBox);

        // Şifre
        var passwordLabel = new Label
        {
            Text = "Şifre",
            Font = new Font("Segoe UI", 10),
            Location = new Point(50, 150),
            AutoSize = true
        };
        Controls.Add(passwordLabel);

        _passwordTextBox = new TextBox
        {
            Location = new Point(50, 175),
            Size = new Size(280, 30),
            Font = new Font("Segoe UI", 11),
            UseSystemPasswordChar = true,
            PlaceholderText = "••••••••"
        };
        Controls.Add(_passwordTextBox);

        // Beni hatırla
        _rememberCheckBox = new CheckBox
        {
            Text = "Beni hatırla",
            Font = new Font("Segoe UI", 9),
            Location = new Point(50, 210),
            Checked = true,
            AutoSize = true
        };
        Controls.Add(_rememberCheckBox);

        // Giriş butonu
        _loginButton = new Button
        {
            Text = "Giriş Yap",
            Location = new Point(50, 240),
            Size = new Size(280, 40),
            BackColor = Color.FromArgb(139, 92, 246),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        _loginButton.FlatAppearance.BorderSize = 0;
        _loginButton.Click += async (s, e) => await LoginAsync();
        Controls.Add(_loginButton);

        // Durum
        _statusLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Red,
            Location = new Point(50, 285),
            Size = new Size(280, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };
        Controls.Add(_statusLabel);

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
