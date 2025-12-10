using System.Windows.Forms;
using MenuBuPrinterAgent.Models;
using MenuBuPrinterAgent.Services;
using MenuBuPrinterAgent.UI;
using Microsoft.Win32;
using Serilog;

namespace MenuBuPrinterAgent;

/// <summary>
/// Ana uygulama context'i - tray icon ve iş akışı yönetimi
/// </summary>
public class AppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly SettingsManager _settings;
    private readonly ApiClient _api;
    private readonly WebSocketClient _wsClient;
    private readonly PrintService _printService;
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _heartbeatTimer;
    private readonly SynchronizationContext _syncContext;
    private readonly SemaphoreSlim _processingLock = new(1, 1);

    private bool _isConnected;
    private bool _isProcessing;
    private int _consecutiveErrors;
    private const int MaxConsecutiveErrors = 5;
    private readonly HashSet<int> _processedJobIds = new();

    public AppContext()
    {
        _syncContext = SynchronizationContext.Current ?? new SynchronizationContext();
        
        // Servisleri başlat
        _settings = new SettingsManager();
        _api = new ApiClient(_settings);
        _wsClient = new WebSocketClient(_settings);
        _printService = new PrintService(_settings);

        // Tray icon oluştur
        _trayIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = Program.AppName,
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };

        _trayIcon.DoubleClick += (s, e) => ShowStatus();

        // Timer'lar
        _pollTimer = new System.Windows.Forms.Timer { Interval = _settings.Settings.PollingIntervalSeconds * 1000 };
        _pollTimer.Tick += async (s, e) => await PollJobsAsync();

        _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 30000 }; // 30 saniye
        _heartbeatTimer.Tick += async (s, e) => await SendHeartbeatWithRetryAsync();

        // WebSocket olayları
        _wsClient.OnJobsReceived += jobs => _syncContext.Post(_ => ProcessJobsAsync(jobs).ConfigureAwait(false), null);
        _wsClient.OnConnected += () => { _isConnected = true; UpdateTrayStatus(); };
        _wsClient.OnDisconnected += () => { _isConnected = false; UpdateTrayStatus(); };
        _wsClient.OnError += msg => Log.Warning("WebSocket hatası: {Message}", msg);

        // Sistem olayları
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionSwitch += OnSessionSwitch;

        // Başlat
        InitializeAsync();
    }

    private Icon LoadIcon()
    {
        try
        {
            // Önce output klasöründen dene
            var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "icon.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
            
            // Sonra aynı dizinden dene
            iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
            
            // Embedded resource'tan dene
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("icon.ico", StringComparison.OrdinalIgnoreCase));
            
            if (resourceName != null)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    return new Icon(stream);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "İkon yüklenemedi");
        }
        return SystemIcons.Application;
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var statusItem = new ToolStripMenuItem("Durum: Bağlantı bekleniyor...")
        {
            Enabled = false,
            Name = "statusItem"
        };
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Giriş Yap", null, (s, e) => ShowLoginForm());
        menu.Items.Add("Çıkış Yap", null, async (s, e) => await LogoutAsync());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Yazıcı Ayarla", null, (s, e) => ShowPrinterSettings());
        menu.Items.Add("Ayarlar", null, (s, e) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Yeniden Bağlan", null, async (s, e) => await ReconnectAsync());
        menu.Items.Add("Kuyruğu Temizle", null, async (s, e) => await ClearQueueAsync());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Log Klasörünü Aç", null, (s, e) => OpenLogsFolder());
        menu.Items.Add("Hakkında", null, (s, e) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());

        menu.Items.Add("Çıkış", null, (s, e) => ExitApplication());

        return menu;
    }

    private async void InitializeAsync()
    {
        Log.Information("Uygulama başlatılıyor...");

        // WebView2'yi hemen başlat (ilk yazdırma hızlı olsun)
        _ = _printService.PreInitializeAsync();

        // Önceki oturum varsa otomatik giriş yap
        if (_settings.Settings.IsLoggedIn && !_settings.Settings.IsTokenExpired)
        {
            Log.Information("Önceki oturum bulundu, doğrulanıyor...");
            
            var valid = await _api.ValidateTokenAsync();
            if (valid)
            {
                Log.Information("Oturum geçerli");
                
                // Bekleyen eski işler var mı kontrol et
                await CheckAndHandlePendingJobsAsync();
                
                Log.Information("Bağlanılıyor...");
                await ConnectAsync();
            }
            else
            {
                Log.Warning("Oturum geçersiz, giriş gerekli");
                _settings.ClearToken();
                ShowNotification("Oturum Süresi Doldu", "Lütfen tekrar giriş yapın.", ToolTipIcon.Warning);
            }
        }
        else
        {
            Log.Information("Giriş gerekli");
            UpdateTrayStatus();
        }
    }

    /// <summary>
    /// Başlangıçta bekleyen eski işleri kontrol et ve kullanıcıya sor
    /// </summary>
    private async Task CheckAndHandlePendingJobsAsync()
    {
        try
        {
            var response = await _api.GetPendingJobsAsync();
            if (response?.Jobs == null || response.Jobs.Count == 0)
            {
                Log.Information("Bekleyen iş yok");
                return;
            }

            var oldJobCount = response.Jobs.Count;
            Log.Information("Başlangıçta {Count} bekleyen iş bulundu", oldJobCount);

            // Kullanıcıya sor
            var result = MessageBox.Show(
                $"Kuyrukta {oldJobCount} adet bekleyen yazdırma işi var.\n\n" +
                "• EVET = Hepsini yazdır\n" +
                "• HAYIR = Hepsini atla (temizle)\n" +
                "• İPTAL = Şimdilik bir şey yapma\n\n" +
                "Not: Eski sipariş fişleri tekrar basılmasın istiyorsanız HAYIR'ı seçin.",
                "Bekleyen Yazdırma İşleri",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (result == DialogResult.No)
            {
                // Tüm işleri atla
                Log.Information("Kullanıcı eski işleri temizlemeyi seçti");
                foreach (var job in response.Jobs)
                {
                    await _api.UpdateJobStatusAsync(job.Id, "skipped", "Agent başlangıcında atlandı");
                    _processedJobIds.Add(job.Id);
                }
                ShowNotification("Kuyruk Temizlendi", $"{oldJobCount} eski iş atlandı.", ToolTipIcon.Info);
            }
            else if (result == DialogResult.Yes)
            {
                Log.Information("Kullanıcı eski işleri yazdırmayı seçti");
                // İşler ConnectAsync içinde yazdırılacak
            }
            else
            {
                // İptal - işleri işlenmiş olarak işaretle ki tekrar sorulmasın
                Log.Information("Kullanıcı erteledi, işler kuyrukta kalacak");
                foreach (var job in response.Jobs)
                {
                    _processedJobIds.Add(job.Id);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Bekleyen iş kontrolü hatası");
        }
    }

    private async Task ConnectAsync()
    {
        try
        {
            _isConnected = true;
            UpdateTrayStatus();

            // Heartbeat başlat
            _heartbeatTimer.Start();
            await _api.SendHeartbeatAsync();

            // WebSocket bağlantısı
            if (_settings.Settings.EnableWebSocket)
            {
                await _wsClient.ConnectAsync();
            }

            // Polling başlat (WebSocket yedek olarak)
            _pollTimer.Start();

            // İlk işleri al
            await PollJobsAsync();

            ShowNotification("Bağlandı", $"MenuBu Printer Agent hazır.\nYazıcı: {_settings.Settings.DefaultPrinterName}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bağlantı hatası");
            _isConnected = false;
            UpdateTrayStatus();
        }
    }

    private async Task PollJobsAsync()
    {
        if (!_settings.Settings.IsLoggedIn || _isProcessing)
        {
            return;
        }

        try
        {
            var response = await _api.GetPendingJobsAsync();
            if (response?.Jobs?.Count > 0)
            {
                Log.Information("Polling: {Count} iş bulundu", response.Jobs.Count);
                await ProcessJobsAsync(response.Jobs);
                _consecutiveErrors = 0;
            }
        }
        catch (Exception ex)
        {
            _consecutiveErrors++;
            Log.Warning(ex, "Polling hatası ({Count}/{Max})", _consecutiveErrors, MaxConsecutiveErrors);

            if (_consecutiveErrors >= MaxConsecutiveErrors)
            {
                ShowNotification("Bağlantı Sorunu", "Sunucuya ulaşılamıyor, yeniden bağlanılıyor...", ToolTipIcon.Warning);
                _consecutiveErrors = 0;
                _ = ReconnectAsync();
            }
        }
    }

    private async Task ProcessJobsAsync(List<PrintJob> jobs)
    {
        if (!await _processingLock.WaitAsync(0))
        {
            Log.Debug("İş işleme zaten devam ediyor");
            return;
        }

        _isProcessing = true;

        try
        {
            foreach (var job in jobs)
            {
                // Zaten işlenmiş işleri atla
                if (_processedJobIds.Contains(job.Id))
                {
                    Log.Debug("İş zaten işlenmiş, atlanıyor: {Id}", job.Id);
                    continue;
                }
                
                Log.Information("İş işleniyor: {Id}", job.Id);
                _processedJobIds.Add(job.Id); // İşleniyor olarak işaretle

                try
                {
                    // Her iş için 90 saniye timeout
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    await ProcessSingleJobAsync(job, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log.Warning("İş zaman aşımına uğradı: {Id}", job.Id);
                    try { await _api.UpdateJobStatusAsync(job.Id, "failed", "Zaman aşımı"); } catch { }
                    ShowNotification("Yazdırma Hatası", "İş zaman aşımına uğradı", ToolTipIcon.Error);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "İş işleme hatası: {Id}", job.Id);
                    try { await _api.UpdateJobStatusAsync(job.Id, "failed", ex.Message); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Toplu iş işleme hatası");
        }
        finally
        {
            _isProcessing = false;
            _processingLock.Release();
        }
    }

    private async Task ProcessSingleJobAsync(PrintJob job, CancellationToken ct)
    {
        // Parsing
        if (job.Payload == null && !string.IsNullOrEmpty(job.PayloadJson))
        {
            try
            {
                job.Payload = System.Text.Json.JsonSerializer.Deserialize<PrintPayload>(job.PayloadJson);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Job {Id} payload parse hatası", job.Id);
            }
        }

        // Durumu "printing" yap (hata olursa devam et)
        try { await _api.UpdateJobStatusAsync(job.Id, "printing"); } catch { }

        ct.ThrowIfCancellationRequested();

        // Yazdır
        var result = await _printService.PrintJobAsync(job);

        ct.ThrowIfCancellationRequested();

        if (result.Success)
        {
            try { await _api.UpdateJobStatusAsync(job.Id, "printed"); } catch { }
            Log.Information("İş başarıyla yazdırıldı: {Id}", job.Id);

            if (_settings.Settings.EnableNotifications)
            {
                ShowNotification("Yazdırıldı", $"Sipariş fişi yazdırıldı.", ToolTipIcon.Info);
            }
        }
        else
        {
            try { await _api.UpdateJobStatusAsync(job.Id, "failed", result.Error); } catch { }
            Log.Warning("İş yazdırılamadı: {Id}, Hata: {Error}", job.Id, result.Error);

            ShowNotification("Yazdırma Hatası", result.Error ?? "Bilinmeyen hata", ToolTipIcon.Error);
        }
    }

    private void UpdateTrayStatus()
    {
        var statusText = _settings.Settings.IsLoggedIn
            ? (_isConnected ? $"Bağlı: {_settings.Settings.BusinessName}" : "Bağlantı bekleniyor...")
            : "Giriş yapılmadı";

        _trayIcon.Text = $"{Program.AppName}\n{statusText}";

        var statusItem = _trayIcon.ContextMenuStrip?.Items.Find("statusItem", false).FirstOrDefault() as ToolStripMenuItem;
        if (statusItem != null)
        {
            statusItem.Text = $"Durum: {statusText}";
        }
    }

    private void ShowNotification(string title, string message, ToolTipIcon icon)
    {
        if (!_settings.Settings.EnableNotifications) return;

        _trayIcon.ShowBalloonTip(3000, title, message, icon);
    }

    private void ShowLoginForm()
    {
        var form = new LoginForm(_settings, _api);
        if (form.ShowDialog() == DialogResult.OK)
        {
            _ = ConnectAsync();
        }
    }

    private async Task LogoutAsync()
    {
        _pollTimer.Stop();
        _heartbeatTimer.Stop();
        await _wsClient.DisconnectAsync();
        _settings.ClearToken();
        _isConnected = false;
        UpdateTrayStatus();
        Log.Information("Çıkış yapıldı");
    }

    private void ShowPrinterSettings()
    {
        var form = new PrinterSettingsForm(_settings);
        form.ShowDialog();
    }

    private void ShowSettings()
    {
        var form = new SettingsForm(_settings);
        form.ShowDialog();
    }

    private int _consecutiveHeartbeatFailures = 0;
    private const int MaxHeartbeatFailures = 3;

    private async Task SendHeartbeatWithRetryAsync()
    {
        try
        {
            await _api.SendHeartbeatAsync();
            _consecutiveHeartbeatFailures = 0; // Başarılı, sayacı sıfırla
        }
        catch (Exception ex)
        {
            _consecutiveHeartbeatFailures++;
            Log.Warning("Heartbeat başarısız ({Count}/{Max}): {Error}", 
                _consecutiveHeartbeatFailures, MaxHeartbeatFailures, ex.Message);

            if (_consecutiveHeartbeatFailures >= MaxHeartbeatFailures)
            {
                Log.Information("Çok fazla heartbeat hatası, yeniden bağlanılıyor...");
                _consecutiveHeartbeatFailures = 0;
                _ = ReconnectAsync();
            }
        }
    }

    private async Task ReconnectAsync()
    {
        Log.Information("Yeniden bağlanılıyor...");
        _pollTimer.Stop();
        _heartbeatTimer.Stop();
        await _wsClient.DisconnectAsync();
        await ConnectAsync();
    }

    private async Task ClearQueueAsync()
    {
        try
        {
            // İşlenen işler listesini temizle
            _processedJobIds.Clear();
            
            // Sunucudaki bekleyen işleri iptal et olarak işaretle
            var jobs = await _api.GetPendingJobsAsync();
            if (jobs?.Jobs != null)
            {
                foreach (var job in jobs.Jobs)
                {
                    await _api.UpdateJobStatusAsync(job.Id, "cancelled", "Kuyruk temizlendi");
                    _processedJobIds.Add(job.Id);
                }
            }
            
            ShowNotification("Kuyruk Temizlendi", $"{jobs?.Jobs?.Count ?? 0} iş iptal edildi.", ToolTipIcon.Info);
            Log.Information("Kuyruk temizlendi: {Count} iş iptal edildi", jobs?.Jobs?.Count ?? 0);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Kuyruk temizleme hatası");
            ShowNotification("Hata", "Kuyruk temizlenirken hata oluştu.", ToolTipIcon.Error);
        }
    }

    private void OpenLogsFolder()
    {
        var logsPath = SettingsManager.GetLogsFolder();
        if (Directory.Exists(logsPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", logsPath);
        }
    }

    private void ShowStatus()
    {
        var status = $@"MenuBu Printer Agent v{Program.AppVersion}

Durum: {(_isConnected ? "Bağlı" : "Bağlı Değil")}
İşletme: {_settings.Settings.BusinessName ?? "Giriş yapılmadı"}
Yazıcı: {_settings.Settings.DefaultPrinterName ?? "Seçilmedi"}
Genişlik: {_settings.Settings.PrinterWidth}

WebSocket: {(_settings.Settings.EnableWebSocket ? (_wsClient.IsConnected ? "Bağlı" : "Bağlı Değil") : "Kapalı")}
Bildirimler: {(_settings.Settings.EnableNotifications ? "Açık" : "Kapalı")}";

        MessageBox.Show(status, "Durum Bilgisi", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            $"{Program.AppName}\nSürüm: {Program.AppVersion}\n\n© 2024 MenuBu\nTüm hakları saklıdır.",
            "Hakkında",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Log.Information("Sistem uyandı, yeniden bağlanılıyor...");
            _ = ReconnectAsync();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            Log.Information("Oturum açıldı, bağlantı kontrol ediliyor...");
            _ = ReconnectAsync();
        }
    }

    private void ExitApplication()
    {
        Log.Information("Uygulama kapatılıyor...");

        _pollTimer.Stop();
        _heartbeatTimer.Stop();
        _wsClient.Dispose();
        _printService.Dispose();
        _api.Dispose();

        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;

        _trayIcon.Visible = false;
        _trayIcon.Dispose();

        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ExitApplication();
        }
        base.Dispose(disposing);
    }
}
