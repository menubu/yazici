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

        // Hızlı mod - ESC/POS veya lines kullan
        if (_settings.Settings.PrintMode == "fast")
        {
            // Önce ESC/POS varsa onu kullan (en hızlı!)
            if (!string.IsNullOrEmpty(job.Payload.EscPos))
            {
                return await PrintEscPosAsync(job.Payload.EscPos, printerName);
            }
            
            // ESC/POS yoksa lines kullan
            if (job.Payload.Lines?.Count > 0)
            {
                return await PrintTextLinesAsync(job.Payload.Lines, printerName, job.Payload.PrinterWidth);
            }
            
            // Lines yoksa HTML'den text çıkar
            if (job.Payload.HasHtml)
            {
                var lines = ExtractTextFromHtml(job.Payload.Html!, job.Payload.PrinterWidth);
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
        // Önce job düzeyindeki printer_tags kontrol et
        var tags = job.PrinterTags ?? job.Payload?.PrinterTags;
        
        if (tags?.Count > 0)
        {
            foreach (var tag in tags)
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
    /// ESC/POS binary yazdır - EN HIZLI YOL!
    /// Windows yazıcıya doğrudan RAW veri gönderir
    /// </summary>
    public Task<PrintResult> PrintEscPosAsync(string base64Data, string printerName)
    {
        return Task.Run(() =>
        {
            try
            {
                var bytes = Convert.FromBase64String(base64Data);
                Log.Information("ESC/POS yazdırılıyor: {Printer}, {Bytes} bytes", printerName, bytes.Length);

                var success = RawPrinterHelper.SendBytesToPrinter(printerName, bytes);
                
                if (success)
                {
                    Log.Information("ESC/POS yazdırma başarılı");
                    return new PrintResult { Success = true };
                }
                else
                {
                    Log.Warning("ESC/POS yazdırma başarısız");
                    return new PrintResult { Success = false, Error = "RAW yazdırma başarısız" };
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ESC/POS yazdırma hatası");
                return new PrintResult { Success = false, Error = ex.Message };
            }
        });
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
            bool lockAcquired = false;
            
            try
            {
                // 60 saniye timeout ile lock al
                lockAcquired = await _printLock.WaitAsync(TimeSpan.FromSeconds(60));
                if (!lockAcquired)
                {
                    Log.Warning("PrintLock alınamadı - timeout");
                    tcs.TrySetResult(new PrintResult { Success = false, Error = "Yazdırma kuyruğu meşgul" });
                    return;
                }
                
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
                if (lockAcquired)
                {
                    _printLock.Release();
                }
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

                // Termal yazıcı için margin'leri 0'a ayarla
                doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                
                // 58mm = ~203 pixels (72 DPI), 80mm = ~283 pixels
                var pageWidth = printerWidth.StartsWith("80") ? 283 : 203;
                var fontSize = printerWidth.StartsWith("80") ? 9f : 7f;
                fontSize += _settings.Settings.FontSizeAdjustment;

                var font = new Font("Consolas", fontSize); // Monospace font daha iyi hizalama
                var lineIndex = 0;

                doc.PrintPage += (s, e) =>
                {
                    if (e.Graphics == null) return;

                    float x = 5; // Sol margin - 5 pixel
                    float y = 5; // Üst margin - 5 pixel
                    float lineHeight = font.GetHeight(e.Graphics);
                    float maxY = e.PageBounds.Height - 10;

                    while (lineIndex < lines.Count && y + lineHeight < maxY)
                    {
                        var line = lines[lineIndex];
                        
                        // Ortalama kontrolü (= veya - ile başlayan satırlar genelde separator)
                        if (line.StartsWith("===") || line.StartsWith("---"))
                        {
                            // Separator - tam genişlikte çiz
                            e.Graphics.DrawString(line, font, Brushes.Black, x, y);
                        }
                        else
                        {
                            // Normal satır
                            e.Graphics.DrawString(line, font, Brushes.Black, x, y);
                        }
                        
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
    private List<string> ExtractTextFromHtml(string html, string printerWidth)
    {
        var lines = new List<string>();
        
        // 58mm = 32 karakter, 80mm = 48 karakter
        var maxLineLength = printerWidth.StartsWith("80") ? 48 : 32;
        var separator = printerWidth.StartsWith("80") 
            ? "------------------------------------------------" 
            : "--------------------------------";
        
        try
        {
            // HTML tag'lerini temizle
            var text = System.Text.RegularExpressions.Regex.Replace(html, @"<style[^>]*>[\s\S]*?</style>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<script[^>]*>[\s\S]*?</script>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<br\s*/?>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"</div>|</p>|</tr>|</li>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<hr\s*/?>", separator + "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", "");
            text = System.Net.WebUtility.HtmlDecode(text);
            
            // Satırlara böl ve temizle
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    // Çok uzun satırları kes
                    if (trimmed.Length > maxLineLength)
                    {
                        // Kelime sınırından kes
                        var words = trimmed.Split(' ');
                        var currentLine = "";
                        foreach (var word in words)
                        {
                            if ((currentLine + " " + word).Trim().Length <= maxLineLength)
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
