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
    private bool _navigationReady;
    private string? _pendingStateJson;

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
        _webView.NavigationCompleted += async (_, _) =>
        {
            _navigationReady = true;
            await ApplyPendingStateAsync();
        };

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
            _navigationReady = false;
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

    public void HideDisplay()
    {
        _pendingStateJson = null;
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

    public async Task ApplyStateJsonAsync(string stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
        {
            return;
        }

        _pendingStateJson = stateJson;
        await ApplyPendingStateAsync();
    }

    private async Task ApplyPendingStateAsync()
    {
        var stateJson = _pendingStateJson;
        if (string.IsNullOrWhiteSpace(stateJson))
        {
            return;
        }

        if (!_navigationReady)
        {
            return;
        }

        await EnsureWebViewReadyAsync();
        _webView.CoreWebView2.PostWebMessageAsJson(stateJson);
        _pendingStateJson = null;
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
