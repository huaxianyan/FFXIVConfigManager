using System.Text;

namespace FFXIVConfigManager.Infrastructure.Logging;

public sealed class FileDiagnosticLogger(string logDirectory)
{
    private readonly object _sync = new();

    public void Initialize()
    {
        Directory.CreateDirectory(logDirectory);
        DeleteExpiredLogs();
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            Write("FATAL", eventArgs.ExceptionObject?.ToString() ?? "Unknown unhandled exception");
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Write("ERROR", eventArgs.Exception.ToString());
            eventArgs.SetObserved();
        };
        Write("INFO", $"Application started; version={GetType().Assembly.GetName().Version}");
    }

    public void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}";
            lock (_sync)
            {
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(GetCurrentLogPath(), line, Encoding.UTF8);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string GetCurrentLogPath() =>
        Path.Combine(logDirectory, $"app-{DateTimeOffset.Now:yyyyMMdd}.log");

    private void DeleteExpiredLogs()
    {
        var threshold = DateTime.UtcNow.AddDays(-14);
        foreach (var path in Directory.EnumerateFiles(logDirectory, "app-*.log"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) < threshold)
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
