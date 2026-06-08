using System.Drawing;
using System.Drawing.Printing;
using System.Text.Json;
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
    private static readonly System.Text.Json.JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SettingsManager _settings;
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private bool _disposed;
    private static readonly HttpClient UrlFetchHttpClient = CreateUrlFetchHttpClient();

    public PrintService(SettingsManager settings)
    {
        _settings = settings;
    }

    private static HttpClient CreateUrlFetchHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(3),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingDelay = TimeSpan.FromSeconds(25),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always
        };

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
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
                job.Payload = System.Text.Json.JsonSerializer.Deserialize<PrintPayload>(job.PayloadJson, PayloadJsonOptions);
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

        var printMode = _settings.Settings.PrintMode == "rich" ? "rich" : "fast";
        var preferLinesOverEscpos = GetPayloadBoolOption(job.Payload, "prefer_lines_over_escpos");
        Log.Information("Yazdırılıyor: Job {Id}, Yazıcı: {Printer}, Mode: {Mode}", job.Id, printerName, printMode);

        // Compatibility mode: lines destekleyen cihazlarda ESC/POS yerine text satırlarını tercih et.
        if (preferLinesOverEscpos && job.Payload.Lines?.Count > 0)
        {
            var linesResult = await PrintTextLinesAsync(job.Payload.Lines, printerName, job.Payload.PrinterWidth);
            if (linesResult.Success)
            {
                return linesResult;
            }
            Log.Warning("Compatibility lines yazdırma başarısız, diğer moda düşülüyor: {Error}", linesResult.Error);
        }

        // ESC/POS varsa normalde öncelikle kullan (en hızlı yol)
        if (!preferLinesOverEscpos && !string.IsNullOrEmpty(job.Payload.EscPos))
        {
            var escposResult = await PrintEscPosAsync(job.Payload.EscPos, printerName);
            if (escposResult.Success)
            {
                return escposResult;
            }
            Log.Warning("ESC/POS yazdırma başarısız, diğer moda düşülüyor: {Error}", escposResult.Error);
        }

        // Hızlı mod - ESC/POS veya lines kullan
        if (printMode == "fast")
        {
            return await PrintFastFallbackAsync(job.Payload, printerName);
        }

        // Zengin mod - HTML varsa WebView2 ile yazdır
        if (job.Payload.HasHtml)
        {
            var richResult = await PrintHtmlAsync(job.Payload.Html!, printerName, job.Payload.PrinterWidth);
            if (richResult.Success)
            {
                return richResult;
            }

            Log.Warning("Rich yazdırma başarısız, hızlı moda düşülüyor: {Error}", richResult.Error);
            var fastFallback = await PrintFastFallbackAsync(job.Payload, printerName);
            return fastFallback.Success ? fastFallback : richResult;
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

    private async Task<PrintResult> PrintFastFallbackAsync(PrintPayload payload, string printerName)
    {
        var preferLinesOverEscpos = GetPayloadBoolOption(payload, "prefer_lines_over_escpos");

        if (preferLinesOverEscpos && payload.Lines?.Count > 0)
        {
            return await PrintTextLinesAsync(payload.Lines, printerName, payload.PrinterWidth);
        }

        // Önce ESC/POS varsa onu kullan (en hızlı)
        if (!string.IsNullOrEmpty(payload.EscPos))
        {
            return await PrintEscPosAsync(payload.EscPos, printerName);
        }

        // ESC/POS yoksa lines kullan
        if (payload.Lines?.Count > 0)
        {
            return await PrintTextLinesAsync(payload.Lines, printerName, payload.PrinterWidth);
        }

        // Lines yoksa HTML'den text çıkar
        if (payload.HasHtml)
        {
            var lines = ExtractTextFromHtml(payload.Html!, payload.PrinterWidth);
            if (lines.Count > 0)
            {
                return await PrintTextLinesAsync(lines, printerName, payload.PrinterWidth);
            }
        }

        // En son URL fallback
        if (!string.IsNullOrEmpty(payload.EffectiveUrl))
        {
            return await PrintFromUrlAsync(payload.EffectiveUrl, printerName, payload.PrinterWidth);
        }

        return new PrintResult { Success = false, Error = "Hızlı modda yazdırılacak içerik bulunamadı" };
    }

    private static bool GetPayloadBoolOption(PrintPayload payload, string key)
    {
        if (payload.Options == null || !payload.Options.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        try
        {
            if (raw is bool b)
            {
                return b;
            }
            if (raw is string s)
            {
                var parsed = s.Trim().ToLowerInvariant();
                return parsed is "1" or "true" or "yes" or "on";
            }
            if (raw is JsonElement je)
            {
                return je.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.String => GetPayloadBoolOption(new PrintPayload
                    {
                        Options = new Dictionary<string, object> { [key] = je.GetString() ?? string.Empty }
                    }, key),
                    JsonValueKind.Number => je.TryGetInt64(out var n) && n != 0,
                    _ => false
                };
            }
            if (raw is int i)
            {
                return i != 0;
            }
            if (raw is long l)
            {
                return l != 0;
            }
            if (raw is double d)
            {
                return Math.Abs(d) > double.Epsilon;
            }
        }
        catch
        {
            // Ignore malformed option values.
        }

        return false;
    }

    private string? SelectPrinter(PrintJob job)
    {
        // 1. Önce printer_id kontrol et (cloud_printers tablosundaki ID)
        if (job.PrinterId.HasValue && job.PrinterId.Value > 0)
        {
            // Yazıcı devre dışı mı kontrol et
            if (_settings.IsPrinterDisabled(job.PrinterId.Value))
            {
                Log.Information("Yazıcı devre dışı, iş atlanıyor: PrinterId={PrinterId}", job.PrinterId.Value);
                return null; // Bu yazıcı için yazdırma yapılmayacak
            }
            
            var mappedPrinter = _settings.GetPrinterForId(job.PrinterId.Value);
            if (!string.IsNullOrEmpty(mappedPrinter))
            {
                Log.Debug("Yazıcı ID ile seçildi: {PrinterId} -> {Printer}", job.PrinterId.Value, mappedPrinter);
                return mappedPrinter;
            }
            Log.Warning("Yazıcı ID eşleşmesi bulunamadı: {PrinterId}, varsayılan kullanılacak", job.PrinterId.Value);
        }

        // 2. Sonra printer_tags kontrol et
        var tags = job.PrinterTags ?? job.Payload?.PrinterTags;
        
        if (tags?.Count > 0)
        {
            foreach (var tag in tags)
            {
                var mapped = _settings.GetPrinterForTag(tag);
                if (!string.IsNullOrEmpty(mapped))
                {
                    Log.Debug("Yazıcı tag ile seçildi: {Tag} -> {Printer}", tag, mapped);
                    return mapped;
                }
            }
        }

        // 3. Varsayılan yazıcıyı kullan
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
        var tcs = new TaskCompletionSource<PrintResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var thread = new Thread(() =>
        {
            Form? hiddenForm = null;
            WebView2? webView = null;
            bool lockAcquired = false;

            try
            {
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

                hiddenForm.Shown += (_, _) =>
                {
                    _ = RunPrintPipelineAsync();
                };

                hiddenForm.FormClosed += (_, _) =>
                {
                    webView?.Dispose();
                    Application.ExitThread();
                };

                hiddenForm.Show();
                hiddenForm.Hide();
                Application.Run(hiddenForm);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "HTML yazdırma thread başlatma hatası");
                tcs.TrySetResult(new PrintResult { Success = false, Error = ex.Message });
                if (lockAcquired)
                {
                    _printLock.Release();
                }
            }

            async Task RunPrintPipelineAsync()
            {
                try
                {
                    if (hiddenForm == null || webView == null)
                    {
                        tcs.TrySetResult(new PrintResult { Success = false, Error = "HTML yazdırma başlatılamadı" });
                        return;
                    }

                    lockAcquired = await _printLock.WaitAsync(TimeSpan.FromSeconds(60));
                    if (!lockAcquired)
                    {
                        Log.Warning("PrintLock alınamadı - timeout");
                        tcs.TrySetResult(new PrintResult { Success = false, Error = "Yazdırma kuyruğu meşgul" });
                        hiddenForm.BeginInvoke(new Action(() => hiddenForm.Close()));
                        return;
                    }

                    var preparedHtml = PrepareHtml(html, printerWidth);
                    Log.Debug("HTML hazırlandı: {Length} karakter", preparedHtml.Length);

                    var userDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MenuBuPrinterAgent",
                        "WebView2");
                    Directory.CreateDirectory(userDataFolder);

                    var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                    await webView.EnsureCoreWebView2Async(env);
                    Log.Debug("WebView2 hazır");

                    var navTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    webView.NavigationCompleted += (_, e) => navTcs.TrySetResult(e.IsSuccess);

                    webView.NavigateToString(preparedHtml);

                    var completed = await Task.WhenAny(navTcs.Task, Task.Delay(TimeSpan.FromSeconds(15)));
                    if (completed != navTcs.Task)
                    {
                        tcs.TrySetResult(new PrintResult { Success = false, Error = "HTML yükleme zaman aşımı" });
                        hiddenForm.BeginInvoke(new Action(() => hiddenForm.Close()));
                        return;
                    }

                    if (!await navTcs.Task)
                    {
                        tcs.TrySetResult(new PrintResult { Success = false, Error = "HTML yüklenemedi" });
                        hiddenForm.BeginInvoke(new Action(() => hiddenForm.Close()));
                        return;
                    }

                    await Task.Delay(100);

                    await Task.Delay(200); // DOM render'ın tam oturmasını bekle

                    // 1. HTML içeriğin gerçek yüksekliğini JavaScript ile al
                    var heightJson = await webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.scrollHeight;");
                    var contentHeight = 600; // Varsayılan
                    if (double.TryParse(
                        heightJson?.Trim('"'),
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var h))
                    {
                        contentHeight = (int)h;
                    }

                    // 2. Form ve WebView boyutunu fiş boyutuna getir
                    // 80mm ≈ 300px, 58mm ≈ 210px
                    var width = printerWidth.StartsWith("80", StringComparison.OrdinalIgnoreCase) ? 300 : 210;
                    hiddenForm.Size = new Size(width, contentHeight);
                    webView.Size = new Size(width, contentHeight);

                    await Task.Delay(100); // Boyutlandırmanın uygulanmasını bekle

                    // 3. HTML'i PNG olarak hafızaya al
                    using var ms = new MemoryStream();
                    await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                    ms.Position = 0;
                    using var imageToPrint = Image.FromStream(ms);

                    // 4. GDI+ PrintDocument ile yazdır
                    using var pd = new PrintDocument();
                    pd.PrinterSettings.PrinterName = printerName;
                    pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

                    pd.PrintPage += (_, args) =>
                    {
                        if (args.Graphics != null)
                        {
                            args.Graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                            args.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            args.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                            args.Graphics.DrawImage(imageToPrint, 0, 0, width, contentHeight);
                        }
                        args.HasMorePages = false;
                    };

                    Log.Information("GDI yazdırma başlatılıyor: {Printer}, {Width}x{Height}px", printerName, width, contentHeight);
                    pd.Print();
                    Log.Information("GDI yazdırma başarılı");
                    tcs.TrySetResult(new PrintResult { Success = true });
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "HTML yazdırma hatası");
                    tcs.TrySetResult(new PrintResult { Success = false, Error = ex.Message });
                }
                finally
                {
                    if (lockAcquired)
                    {
                        _printLock.Release();
                    }
                    if (hiddenForm is { IsDisposed: false, IsHandleCreated: true })
                    {
                        hiddenForm.BeginInvoke(new Action(() => hiddenForm.Close()));
                    }
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
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
            Log.Information("URL'den içerik alınıyor: {Url}", url);
            var response = await UrlFetchHttpClient.GetStringAsync(url);

            try
            {
                var json = System.Text.Json.JsonDocument.Parse(response);
                
                // Fast mode: Önce ESC/POS kontrol et (en hızlı!)
                if (_settings.Settings.PrintMode == "fast")
                {
                    if (json.RootElement.TryGetProperty("escpos", out var escposProp))
                    {
                        var escpos = escposProp.GetString();
                        if (!string.IsNullOrEmpty(escpos))
                        {
                            Log.Information("URL'den ESC/POS alındı, yazdırılıyor");
                            return await PrintEscPosAsync(escpos, printerName);
                        }
                    }
                    
                    // ESC/POS yoksa lines kontrol et
                    if (json.RootElement.TryGetProperty("lines", out var linesProp) && linesProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        var lines = new List<string>();
                        foreach (var line in linesProp.EnumerateArray())
                        {
                            lines.Add(line.GetString() ?? "");
                        }
                        if (lines.Count > 0)
                        {
                            Log.Information("URL'den {Count} satır alındı, yazdırılıyor", lines.Count);
                            return await PrintTextLinesAsync(lines, printerName, printerWidth);
                        }
                    }
                }
                
                // HTML kontrol et
                if (json.RootElement.TryGetProperty("html", out var htmlProp))
                {
                    var html = htmlProp.GetString();
                    if (!string.IsNullOrEmpty(html))
                    {
                        Log.Information("URL'den HTML alındı, yazdırılıyor");
                        return await PrintHtmlAsync(html, printerName, printerWidth);
                    }
                }
            }
            catch
            {
                if (response.TrimStart().StartsWith("<"))
                {
                    Log.Information("URL'den raw HTML alındı, yazdırılıyor");
                    return await PrintHtmlAsync(response, printerName, printerWidth);
                }
            }

            Log.Warning("URL'den yazdırılabilir içerik alınamadı");
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
