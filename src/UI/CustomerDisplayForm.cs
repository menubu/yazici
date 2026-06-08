using System.Drawing;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using Serilog;

namespace MenuBuPrinterAgent.UI;

public class CustomerDisplayForm : Form
{
    private readonly WebView2 _webView;
    private string _currentUrl = "";
    private bool _webViewConfigured;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExNoActivate = 0x08000000;
            const int wsExToolWindow = 0x00000080;

            var createParams = base.CreateParams;
            createParams.ExStyle |= wsExNoActivate | wsExToolWindow;
            return createParams;
        }
    }

    public CustomerDisplayForm()
    {
        Text = "MenuBu Müşteri Ekranı";
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.Black;
        KeyPreview = true;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Black
        };
        Controls.Add(_webView);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
            }
        };
    }

    public async Task OpenOrReloadAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _currentUrl = url;
        PlaceOnCustomerScreen();

        if (!Visible)
        {
            Show();
        }

        WindowState = FormWindowState.Normal;
        PlaceOnCustomerScreen();

        try
        {
            await EnsureWebViewReadyAsync();
            _webView.Source = new Uri(url);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Müşteri ekranı açılamadı");
            MessageBox.Show(
                "Müşteri ekranı açılamadı:\n" + ex.Message,
                "Müşteri Ekranı",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public async Task RefreshDisplayAsync(string? fallbackUrl = null)
    {
        var url = !string.IsNullOrWhiteSpace(fallbackUrl) ? fallbackUrl : _currentUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        await OpenOrReloadAsync(url);
    }

    public async Task PreInitializeAsync()
    {
        try
        {
            CreateControl();
            await EnsureWebViewReadyAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Müşteri ekranı WebView ön hazırlığı başarısız oldu");
        }
    }

    public void HideDisplay()
    {
        Hide();
        try
        {
            if (_webView.CoreWebView2 != null)
            {
                _webView.CoreWebView2.Navigate("about:blank");
            }
            else
            {
                _webView.Source = new Uri("about:blank");
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Müşteri ekranı gizlenirken WebView temizlenemedi");
        }
    }

    private async Task EnsureWebViewReadyAsync()
    {
        await _webView.EnsureCoreWebView2Async();
        if (_webViewConfigured || _webView.CoreWebView2 == null)
        {
            return;
        }

        _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        _webViewConfigured = true;
    }

    private void PlaceOnCustomerScreen()
    {
        var target = ResolveCustomerScreen();
        Bounds = target.Bounds;
        MaximumSize = Size.Empty;
        MinimumSize = Size.Empty;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
    }

    private static Screen ResolveCustomerScreen()
    {
        var screens = Screen.AllScreens;
        if (screens.Length <= 1)
        {
            return Screen.PrimaryScreen ?? screens[0];
        }

        return screens.FirstOrDefault(s => !s.Primary) ?? Screen.PrimaryScreen ?? screens[0];
    }
}
