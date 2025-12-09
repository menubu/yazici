using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;
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
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private bool _disposed;

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
    /// HTML içeriği yazdır - Gizli form ile WebView2
    /// </summary>
    public Task<PrintResult> PrintHtmlAsync(string html, string printerName, string printerWidth)
    {
        var tcs = new TaskCompletionSource<PrintResult>();

        var thread = new Thread(() =>
        {
            Form? hiddenForm = null;
            WebView2? webView = null;
            
            try
            {
                _printLock.Wait();
                
                var preparedHtml = PrepareHtml(html, printerWidth);
                Log.Debug("HTML hazırlandı: {Length} karakter", preparedHtml.Length);

                // Gizli form oluştur (message pump için gerekli)
                hiddenForm = new Form
                {
                    ShowInTaskbar = false,
                    WindowState = FormWindowState.Minimized,
                    FormBorderStyle = FormBorderStyle.None,
                    Opacity = 0
                };

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MenuBuPrinterAgent",
                    "WebView2");
                
                Directory.CreateDirectory(userDataFolder);

                webView = new WebView2
                {
                    Dock = DockStyle.Fill,
                    CreationProperties = new CoreWebView2CreationProperties
                    {
                        UserDataFolder = userDataFolder
                    }
                };

                hiddenForm.Controls.Add(webView);
                hiddenForm.Show();
                hiddenForm.Hide();

                var initComplete = false;
                var printComplete = false;
                PrintResult? result = null;

                webView.CoreWebView2InitializationCompleted += async (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        Log.Debug("WebView2 initialized");
                        initComplete = true;
                        
                        webView.NavigationCompleted += async (ns, ne) =>
                        {
                            Log.Debug("Navigation completed: {Success}", ne.IsSuccess);
                            
                            if (!ne.IsSuccess)
                            {
                                result = new PrintResult { Success = false, Error = "HTML yüklenemedi" };
                                printComplete = true;
                                return;
                            }

                            try
                            {
                                // Kısa bekleme - sayfa tam render olsun
                                await Task.Delay(500);
                                
                                var env = webView.CoreWebView2.Environment;
                                var printSettings = env.CreatePrintSettings();
                                printSettings.ShouldPrintBackgrounds = true;
                                printSettings.ShouldPrintHeaderAndFooter = false;
                                printSettings.PrinterName = printerName;
                                printSettings.ScaleFactor = 1.0;

                                Log.Information("Yazdırma başlatılıyor: {Printer}", printerName);
                                var status = await webView.CoreWebView2.PrintAsync(printSettings);

                                if (status == CoreWebView2PrintStatus.Succeeded)
                                {
                                    Log.Information("Yazdırma başarılı");
                                    result = new PrintResult { Success = true };
                                }
                                else
                                {
                                    var errorMsg = status switch
                                    {
                                        CoreWebView2PrintStatus.PrinterUnavailable => "Yazıcıya ulaşılamadı",
                                        CoreWebView2PrintStatus.OtherError => "Yazdırma hatası",
                                        _ => $"Bilinmeyen hata: {status}"
                                    };
                                    Log.Warning("Yazdırma başarısız: {Error}", errorMsg);
                                    result = new PrintResult { Success = false, Error = errorMsg };
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Yazdırma hatası");
                                result = new PrintResult { Success = false, Error = ex.Message };
                            }
                            
                            printComplete = true;
                        };

                        webView.NavigateToString(preparedHtml);
                    }
                    else
                    {
                        Log.Error(e.InitializationException, "WebView2 init hatası");
                        result = new PrintResult { Success = false, Error = "WebView2 başlatılamadı" };
                        printComplete = true;
                    }
                };

                // WebView2'yi başlat
                var envTask = CoreWebView2Environment.CreateAsync(null, userDataFolder);
                envTask.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        hiddenForm.Invoke(() => webView.EnsureCoreWebView2Async(t.Result));
                    }
                    else
                    {
                        result = new PrintResult { Success = false, Error = "WebView2 environment oluşturulamadı" };
                        printComplete = true;
                    }
                });

                // Message pump ile bekle (timeout: 60 saniye)
                var startTime = DateTime.UtcNow;
                while (!printComplete && (DateTime.UtcNow - startTime).TotalSeconds < 60)
                {
                    Application.DoEvents();
                    Thread.Sleep(50);
                }

                if (!printComplete)
                {
                    result = new PrintResult { Success = false, Error = "Yazdırma zaman aşımı" };
                }

                tcs.SetResult(result ?? new PrintResult { Success = false, Error = "Bilinmeyen hata" });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HTML yazdırma hatası");
                tcs.SetResult(new PrintResult { Success = false, Error = ex.Message });
            }
            finally
            {
                webView?.Dispose();
                hiddenForm?.Dispose();
                _printLock.Release();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
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
            _printLock.Dispose();
        }
    }
}

public class PrintResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
