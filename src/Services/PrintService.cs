using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
using MenuBuPrinterAgent.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;

namespace MenuBuPrinterAgent.Services;

/// <summary>
/// Yazdırma servisi - WebView2 singleton ile hızlı HTML yazdırma
/// </summary>
public class PrintService : IDisposable
{
    private readonly SettingsManager _settings;
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private bool _disposed;

    // Singleton WebView2 - uygulama boyunca açık kalır
    private Form? _hiddenForm;
    private WebView2? _webView;
    private CoreWebView2Environment? _environment;
    private bool _isInitialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public PrintService(SettingsManager settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// WebView2'yi önceden başlat (uygulama açılışında çağrılır)
    /// </summary>
    public async Task PreInitializeAsync()
    {
        await InitializeWebViewAsync();
    }

    /// <summary>
    /// İşi yazdır
    /// </summary>
    public async Task<PrintResult> PrintJobAsync(PrintJob job)
    {
        if (job.Payload == null && !string.IsNullOrEmpty(job.PayloadJson))
        {
            try
            {
                job.Payload = System.Text.Json.JsonSerializer.Deserialize<PrintPayload>(job.PayloadJson);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Job {Id} payload parse hatası", job.Id);
                return new PrintResult { Success = false, Error = "Payload parse hatası" };
            }
        }

        if (job.Payload == null)
        {
            return new PrintResult { Success = false, Error = "Payload bulunamadı" };
        }

        var printerName = SelectPrinter(job);
        if (string.IsNullOrEmpty(printerName))
        {
            return new PrintResult { Success = false, Error = "Yazıcı seçilmedi" };
        }

        Log.Information("Yazdırılıyor: Job {Id}, Yazıcı: {Printer}", job.Id, printerName);

        // HTML varsa WebView2 ile yazdır
        if (job.Payload.HasHtml)
        {
            return await PrintHtmlAsync(job.Payload.Html!, printerName, job.Payload.PrinterWidth);
        }

        // URL varsa içeriği al
        if (!string.IsNullOrEmpty(job.Payload.EffectiveUrl))
        {
            return await PrintFromUrlAsync(job.Payload.EffectiveUrl, printerName, job.Payload.PrinterWidth);
        }

        // Lines varsa text olarak yazdır
        if (job.Payload.Lines?.Count > 0)
        {
            return await PrintTextLinesAsync(job.Payload.Lines, printerName, job.Payload.PrinterWidth);
        }

        return new PrintResult { Success = false, Error = "Yazdırılacak içerik bulunamadı" };
    }

    private string? SelectPrinter(PrintJob job)
    {
        if (job.Payload?.PrinterTags?.Count > 0)
        {
            foreach (var tag in job.Payload.PrinterTags)
            {
                var mapped = _settings.GetPrinterForTag(tag);
                if (!string.IsNullOrEmpty(mapped))
                {
                    return mapped;
                }
            }
        }
        return _settings.Settings.DefaultPrinterName;
    }

    private async Task InitializeWebViewAsync()
    {
        if (_isInitialized) return;

        await _initLock.WaitAsync();
        try
        {
            if (_isInitialized) return;

            Log.Information("WebView2 başlatılıyor (tek seferlik)...");

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MenuBuPrinterAgent",
                "WebView2");

            Directory.CreateDirectory(userDataFolder);

            _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            // UI thread'de form oluştur
            if (Application.OpenForms.Count > 0)
            {
                var mainForm = Application.OpenForms[0];
                await Task.Run(() => mainForm.Invoke(() => CreateHiddenForm()));
            }
            else
            {
                // Fallback - form yoksa yeni thread'de oluştur
                var tcs = new TaskCompletionSource<bool>();
                var thread = new Thread(() =>
                {
                    CreateHiddenForm();
                    tcs.SetResult(true);
                    Application.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                await tcs.Task;
            }

            _isInitialized = true;
            Log.Information("WebView2 hazır!");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebView2 başlatma hatası");
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void CreateHiddenForm()
    {
        _hiddenForm = new Form
        {
            ShowInTaskbar = false,
            WindowState = FormWindowState.Minimized,
            FormBorderStyle = FormBorderStyle.None,
            Opacity = 0,
            Size = new Size(800, 600)
        };

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

        _hiddenForm.Controls.Add(_webView);
        _hiddenForm.Show();
        _hiddenForm.Hide();

        _webView.EnsureCoreWebView2Async(_environment).ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                Log.Debug("WebView2 CoreWebView2 hazır");
            }
        });
    }

    /// <summary>
    /// HTML içeriği yazdır - önceden başlatılmış WebView2 kullanır
    /// </summary>
    public async Task<PrintResult> PrintHtmlAsync(string html, string printerName, string printerWidth)
    {
        await _printLock.WaitAsync();
        try
        {
            // WebView2 hazır değilse başlat
            if (!_isInitialized || _webView?.CoreWebView2 == null)
            {
                await InitializeWebViewAsync();
                
                // WebView2'nin tam hazır olmasını bekle
                for (int i = 0; i < 50 && _webView?.CoreWebView2 == null; i++)
                {
                    await Task.Delay(100);
                }

                if (_webView?.CoreWebView2 == null)
                {
                    return new PrintResult { Success = false, Error = "WebView2 başlatılamadı" };
                }
            }

            var preparedHtml = PrepareHtml(html, printerWidth);
            Log.Debug("HTML hazırlandı: {Length} karakter", preparedHtml.Length);

            // Navigation complete event için TaskCompletionSource
            var navTcs = new TaskCompletionSource<bool>();
            
            void OnNavCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                navTcs.TrySetResult(e.IsSuccess);
            }

            _webView.NavigationCompleted += OnNavCompleted;

            try
            {
                // HTML'i yükle
                if (_hiddenForm?.InvokeRequired == true)
                {
                    _hiddenForm.Invoke(() => _webView.NavigateToString(preparedHtml));
                }
                else
                {
                    _webView.NavigateToString(preparedHtml);
                }

                // Navigation tamamlanmasını bekle (max 10 saniye)
                var completed = await Task.WhenAny(navTcs.Task, Task.Delay(10000));
                if (completed != navTcs.Task || !await navTcs.Task)
                {
                    return new PrintResult { Success = false, Error = "HTML yüklenemedi" };
                }

                // Kısa render bekleme
                await Task.Delay(50);

                // Yazdır
                var printSettings = _environment!.CreatePrintSettings();
                printSettings.ShouldPrintBackgrounds = true;
                printSettings.ShouldPrintHeaderAndFooter = false;
                printSettings.PrinterName = printerName;
                printSettings.ScaleFactor = 1.0;

                Log.Information("Yazdırma başlatılıyor: {Printer}", printerName);

                CoreWebView2PrintStatus status;
                if (_hiddenForm?.InvokeRequired == true)
                {
                    status = await (Task<CoreWebView2PrintStatus>)_hiddenForm.Invoke(
                        async () => await _webView.CoreWebView2.PrintAsync(printSettings));
                }
                else
                {
                    status = await _webView.CoreWebView2.PrintAsync(printSettings);
                }

                if (status == CoreWebView2PrintStatus.Succeeded)
                {
                    Log.Information("Yazdırma başarılı");
                    return new PrintResult { Success = true };
                }

                var errorMsg = status switch
                {
                    CoreWebView2PrintStatus.PrinterUnavailable => "Yazıcıya ulaşılamadı",
                    CoreWebView2PrintStatus.OtherError => "Yazdırma hatası",
                    _ => $"Bilinmeyen hata: {status}"
                };
                Log.Warning("Yazdırma başarısız: {Error}", errorMsg);
                return new PrintResult { Success = false, Error = errorMsg };
            }
            finally
            {
                _webView.NavigationCompleted -= OnNavCompleted;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "HTML yazdırma hatası");
            return new PrintResult { Success = false, Error = ex.Message };
        }
        finally
        {
            _printLock.Release();
        }
    }

    /// <summary>
    /// URL'den içerik alıp yazdır
    /// </summary>
    public async Task<PrintResult> PrintFromUrlAsync(string url, string printerName, string printerWidth)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var response = await http.GetStringAsync(url);

            try
            {
                var json = System.Text.Json.JsonDocument.Parse(response);
                if (json.RootElement.TryGetProperty("html", out var htmlProp))
                {
                    var html = htmlProp.GetString();
                    if (!string.IsNullOrEmpty(html))
                    {
                        return await PrintHtmlAsync(html, printerName, printerWidth);
                    }
                }
            }
            catch
            {
                if (response.TrimStart().StartsWith("<"))
                {
                    return await PrintHtmlAsync(response, printerName, printerWidth);
                }
            }

            return new PrintResult { Success = false, Error = "URL'den içerik alınamadı" };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "URL yazdırma hatası: {Url}", url);
            return new PrintResult { Success = false, Error = ex.Message };
        }
    }

    /// <summary>
    /// Text satırları yazdır
    /// </summary>
    public Task<PrintResult> PrintTextLinesAsync(List<string> lines, string printerName, string printerWidth)
    {
        var tcs = new TaskCompletionSource<PrintResult>();

        var thread = new Thread(() =>
        {
            try
            {
                using var doc = new PrintDocument();
                doc.PrinterSettings.PrinterName = printerName;

                if (!doc.PrinterSettings.IsValid)
                {
                    tcs.SetResult(new PrintResult { Success = false, Error = "Geçersiz yazıcı" });
                    return;
                }

                var fontSize = printerWidth.StartsWith("80") ? 10f : 8f;
                fontSize += _settings.Settings.FontSizeAdjustment;

                var font = new Font("Arial", fontSize);
                var lineIndex = 0;

                doc.PrintPage += (s, e) =>
                {
                    if (e.Graphics == null) return;

                    float y = e.MarginBounds.Top;
                    float lineHeight = font.GetHeight(e.Graphics);

                    while (lineIndex < lines.Count && y + lineHeight < e.MarginBounds.Bottom)
                    {
                        e.Graphics.DrawString(lines[lineIndex], font, Brushes.Black, (float)e.MarginBounds.Left, y);
                        y += lineHeight;
                        lineIndex++;
                    }

                    e.HasMorePages = lineIndex < lines.Count;
                };

                doc.Print();
                font.Dispose();
                tcs.SetResult(new PrintResult { Success = true });
            }
            catch (Exception ex)
            {
                tcs.SetResult(new PrintResult { Success = false, Error = ex.Message });
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }

    private string PrepareHtml(string html, string printerWidth)
    {
        var (pageWidth, bodyWidth) = printerWidth.StartsWith("80") ? ("300px", "300px") : ("219px", "219px");

        var styleBlock = $@"<style id=""menubu-print-style"">
            @page {{ size: {pageWidth} auto; margin: 0; }}
            html, body {{ margin: 0; padding: 0; background: #fff; }}
            body {{ margin: 0 auto; padding: 0; width: {bodyWidth}; max-width: {bodyWidth}; color: #000; }}
            img, table {{ max-width: 100%; }}
            * {{ box-sizing: border-box; word-break: break-word; -webkit-print-color-adjust: exact; }}
        </style>";

        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var headClose = html.IndexOf('>', headIndex);
            if (headClose >= 0)
            {
                return html.Insert(headClose + 1, styleBlock);
            }
        }

        var htmlIndex = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlIndex >= 0)
        {
            var htmlClose = html.IndexOf('>', htmlIndex);
            if (htmlClose >= 0)
            {
                return html.Insert(htmlClose + 1, $"<head>{styleBlock}</head>");
            }
        }

        return $"<!DOCTYPE html><html><head>{styleBlock}</head><body>{html}</body></html>";
    }

    public static List<string> GetAvailablePrinters()
    {
        var printers = new List<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
        {
            printers.Add(printer);
        }
        return printers;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _webView?.Dispose();
            _hiddenForm?.Dispose();
            _printLock.Dispose();
            _initLock.Dispose();
        }
    }
}

public class PrintResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
