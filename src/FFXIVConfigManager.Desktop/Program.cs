using Avalonia;
using FFXIVConfigManager.Infrastructure.Logging;
using FFXIVConfigManager.Infrastructure.Settings;

namespace FFXIVConfigManager.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var logger = new FileDiagnosticLogger(
            Path.Combine(ApplicationDataPaths.GetDefaultDirectory(), "logs"));
        logger.Initialize();

        try
        {
            using var instanceMutex = new Mutex(
                initiallyOwned: true,
                "FFXIVConfigManager.SingleInstance",
                out var isFirstInstance);
            if (!isFirstInstance)
            {
                logger.Write("INFO", "A second application instance was rejected.");
                return;
            }

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            logger.Write("FATAL", exception.ToString());
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
