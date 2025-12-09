using System.Drawing;
using System.Drawing.Printing;
using MenuBuPrinterAgent.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;

namespace MenuBuPrinterAgent.Services;

/// <summary>
/// Yazdırma servisi - HTML ve text desteği
/// </summary>
public class PrintService : IDisposable
{
    private readonly SettingsManager _settings;
    private WebView2? _webView;
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private bool _webViewInitialized;
    private bool _disposed;
    private CoreWebView2Environment? _environment;

    public PrintService(SettingsManager settings)
    {
        _settings = settings;
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
        // Tag'e göre yazıcı seç
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

        // Varsayılan yazıcı
        return _settings.Settings.DefaultPrinterName;
    }

    /// <summary>
    /// HTML içeriği yazdır
    /// </summary>
    public async Task<PrintResult> PrintHtmlAsync(string html, string printerName, string printerWidth)
    {
        await _printLock.WaitAsync();
        try
        {
            await EnsureWebViewInitializedAsync();

            if (_webView == null)
            {
                return new PrintResult { Success = false, Error = "WebView2 başlatılamadı" };
            }

            // HTML'i hazırla
            var preparedHtml = PrepareHtml(html, printerWidth);

            // HTML'i yükle
            var loadTcs = new TaskCompletionSource<bool>();
            void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                loadTcs.TrySetResult(e.IsSuccess);
            }

            _webView.NavigationCompleted += OnNavCompleted;
            try
            {
                _webView.NavigateToString(preparedHtml);
                var loaded = await Task.WhenAny(loadTcs.Task, Task.Delay(10000)) == loadTcs.Task && await loadTcs.Task;
                if (!loaded)
                {
                    return new PrintResult { Success = false, Error = "HTML yüklenemedi" };
                }
            }
            finally
            {
                _webView.NavigationCompleted -= OnNavCompleted;
            }

            // Yazdır
            var printSettings = _environment!.CreatePrintSettings();
            printSettings.ShouldPrintBackgrounds = true;
            printSettings.ShouldPrintHeaderAndFooter = false;
            printSettings.PrinterName = printerName;
            printSettings.ScaleFactor = 1.0;

            var status = await _webView.CoreWebView2.PrintAsync(printSettings);

            if (status == CoreWebView2PrintStatus.Succeeded)
            {
                Log.Information("Yazdırma başarılı: {Printer}", printerName);
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
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetStringAsync(url);

            // JSON olabilir, HTML çıkart
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
                // JSON değilse direkt HTML olabilir
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
        try
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

                        var y = e.MarginBounds.Top;
                        var lineHeight = font.GetHeight(e.Graphics);

                        while (lineIndex < lines.Count && y + lineHeight < e.MarginBounds.Bottom)
                        {
                            e.Graphics.DrawString(lines[lineIndex], font, Brushes.Black, e.MarginBounds.Left, y);
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
        catch (Exception ex)
        {
            return Task.FromResult(new PrintResult { Success = false, Error = ex.Message });
        }
    }

    private async Task EnsureWebViewInitializedAsync()
    {
        if (_webViewInitialized) return;

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MenuBuPrinterAgent",
                "WebView2");

            _environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            _webView = new WebView2
            {
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = userDataFolder
                }
            };

            await _webView.EnsureCoreWebView2Async(_environment);
            _webViewInitialized = true;

            Log.Information("WebView2 başlatıldı");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WebView2 başlatma hatası");
            throw;
        }
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

        // <head> içine ekle
        var headIndex = html.IndexOf("<head", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            var headClose = html.IndexOf('>', headIndex);
            if (headClose >= 0)
            {
                return html.Insert(headClose + 1, styleBlock);
            }
        }

        // <html> bulunursa <head> ekle
        var htmlIndex = html.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlIndex >= 0)
        {
            var htmlClose = html.IndexOf('>', htmlIndex);
            if (htmlClose >= 0)
            {
                return html.Insert(htmlClose + 1, $"<head>{styleBlock}</head>");
            }
        }

        // Hiçbiri yoksa sarmala
        return $"<!DOCTYPE html><html><head>{styleBlock}</head><body>{html}</body></html>";
    }

    /// <summary>
    /// Sistemdeki yazıcıları listele
    /// </summary>
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
            _printLock.Dispose();
        }
    }
}

public class PrintResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
