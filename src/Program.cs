using System;
using System.Threading;
using System.Windows.Forms;
using Serilog;

namespace MenuBuPrinterAgent;

static class Program
{
    private static Mutex? _mutex;
    public const string AppName = "MenuBu Printer Agent";
    public const string AppVersion = "2.0.13";

    [STAThread]
    static void Main()
    {
        // Tek örnek kontrolü
        const string mutexName = "MenuBuPrinterAgent_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show(
                "MenuBu Printer Agent zaten çalışıyor.\nSistem tepsisinde (saat yanında) simgeyi kontrol edin.",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Loglama sistemini başlat
        ConfigureLogging();
        Log.Information("=== {AppName} v{Version} başlatılıyor ===", AppName, AppVersion);

        try
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Ana uygulama context'ini başlat
            using var context = new AppContext();
            Application.Run(context);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Uygulama beklenmeyen bir hata ile sonlandı");
            MessageBox.Show(
                $"Uygulama başlatılamadı:\n{ex.Message}",
                AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            Log.Information("=== Uygulama kapatılıyor ===");
            Log.CloseAndFlush();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
        }
    }

    private static void ConfigureLogging()
    {
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MenuBuPrinterAgent",
            "logs",
            "agent-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()  // Debug yerine Information - daha az log
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,        // Son 7 gün
                fileSizeLimitBytes: 10_000_000,   // Max 10 MB per file
                rollOnFileSizeLimit: true,        // Boyut aşılırsa yeni dosya
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }
}
