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
/// Her yazdırma işlemi kendi thread'inde WebView2 kullanır
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
    /// WebView2'yi önceden başlat (opsiyonel)
    /// </summary>
    public Task PreInitializeAsync()
    {
        // WebView2 Runtime'ın hazır olduğundan emin ol
        return Task.Run(async () =>
        {
            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MenuBuPrinterAgent",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);
                
                // Environment'ı önceden oluştur
                await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                Log.Information("WebView2 environment hazır");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WebView2 pre-init hatası");
            }
        });
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

        Log.Information("Yazdırılıyor: Job {Id}, Yazıcı: {Printer}, Mode: {Mode}", job.Id, printerName, _settings.Settings.PrintMode);

        // Hızlı mod - önce lines varsa text olarak yazdır
        if (_settings.Settings.PrintMode == "fast")
        {
            if (job.Payload.Lines?.Count > 0)
            {
                return await PrintTextLinesAsync(job.Payload.Lines, printerName, job.Payload.PrinterWidth);
            }
            // Lines yoksa HTML'den text çıkar
            if (job.Payload.HasHtml)
            {
                var lines = ExtractTextFromHtml(job.Payload.Html!);
                if (lines.Count > 0)
                {
                    return await PrintTextLinesAsync(lines, printerName, job.Payload.PrinterWidth);
                }
            }
        }

        // Zengin mod - HTML varsa WebView2 ile yazdır
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

    /// <summary>
    /// HTML içeriği yazdır - Her baskı kendi STA thread'inde
    /// </summary>
    public Task<PrintResult> PrintHtmlAsync(string html, string printerName, string printerWidth)
    {
        var tcs = new TaskCompletionSource<PrintResult>();

        var thread = new Thread(async () =>
        {
            Form? hiddenForm = null;
            WebView2? webView = null;
            
            try
            {
                await _printLock.WaitAsync();
                
                var preparedHtml = PrepareHtml(html, printerWidth);
                Log.Debug("HTML hazırlandı: {Length} karakter", preparedHtml.Length);

                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MenuBuPrinterAgent",
                    "WebView2");
                Directory.CreateDirectory(userDataFolder);

                // Gizli form oluştur
                hiddenForm = new Form
                {
                    ShowInTaskbar = false,
                    WindowState = FormWindowState.Minimized,
                    FormBorderStyle = FormBorderStyle.None,
                    Opacity = 0,
                    Size = new Size(800, 600)
                };

                webView = new WebView2 { Dock = DockStyle.Fill };
                hiddenForm.Controls.Add(webView);
                hiddenForm.Show();
                hiddenForm.Hide();

                // Environment oluştur
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                
                // WebView2'yi başlat
                await webView.EnsureCoreWebView2Async(env);
                Log.Debug("WebView2 hazır");

                // Navigation için event
                var navTcs = new TaskCompletionSource<bool>();
                webView.NavigationCompleted += (s, e) => navTcs.TrySetResult(e.IsSuccess);

                // HTML yükle
                webView.NavigateToString(preparedHtml);

                // Navigation bekle (max 15 saniye)
                var navTask = navTcs.Task;
                var timeoutTask = Task.Delay(15000);
                
                if (await Task.WhenAny(navTask, timeoutTask) == timeoutTask)
                {
                    tcs.SetResult(new PrintResult { Success = false, Error = "HTML yükleme zaman aşımı" });
                    return;
                }

                if (!await navTask)
                {
                    tcs.SetResult(new PrintResult { Success = false, Error = "HTML yüklenemedi" });
                    return;
                }

                // Kısa bekleme - sayfa render olsun
                await Task.Delay(50);

                // Yazdır
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
                    tcs.SetResult(new PrintResult { Success = true });
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
                    tcs.SetResult(new PrintResult { Success = false, Error = errorMsg });
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HTML yazdırma hatası");
                tcs.TrySetResult(new PrintResult { Success = false, Error = ex.Message });
            }
            finally
            {
                webView?.Dispose();
                hiddenForm?.Close();
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

    /// <summary>
    /// HTML'den basit text satırları çıkar (hızlı yazdırma için)
    /// </summary>
    private List<string> ExtractTextFromHtml(string html)
    {
        var lines = new List<string>();
        try
        {
            // HTML tag'lerini temizle
            var text = System.Text.RegularExpressions.Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<script[^>]*>[\s\S]*?</script>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"</div>|</p>|</tr>|</li>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<hr\s*/?>", "--------------------------------\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
            text = System.Net.WebUtility.HtmlDecode(text);
            
            // Satırlara böl ve temizle
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    // Çok uzun satırları kes (58mm için ~32 karakter)
                    if (trimmed.Length > 32)
                    {
                        // Kelime sınırından kes
                        var words = trimmed.Split(' ');
                        var currentLine = "";
                        foreach (var word in words)
                        {
                            if ((currentLine + " " + word).Trim().Length <= 32)
                            {
                                currentLine = (currentLine + " " + word).Trim();
                            }
                            else
                            {
                                if (!string.IsNullOrEmpty(currentLine))
                                    lines.Add(currentLine);
                                currentLine = word;
                            }
                        }
                        if (!string.IsNullOrEmpty(currentLine))
                            lines.Add(currentLine);
                    }
                    else
                    {
                        lines.Add(trimmed);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HTML'den text çıkarma hatası");
        }
        return lines;
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
            _printLock.Dispose();
        }
    }
}

public class PrintResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}
