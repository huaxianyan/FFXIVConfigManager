using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FFXIVConfigManager.Application.Updates;

namespace FFXIVConfigManager.Platform.Windows.Updates;

public sealed record WindowsUpdatePlan(
    int ParentProcessId,
    string TargetExecutablePath,
    string PackageExecutablePath,
    string WorkingDirectory,
    string PackageSha256);

public interface IWindowsUpdateFaultInjector
{
    void BeforeCommit();

    void AfterBackupCreated();
}

public sealed class NoWindowsUpdateFaultInjector : IWindowsUpdateFaultInjector
{
    public void BeforeCommit()
    {
    }

    public void AfterBackupCreated()
    {
    }
}

public static class WindowsSelfUpdate
{
    public const string ApplyArgument = "--apply-update";
    public const string CleanupArgument = "--cleanup-update";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static bool IsSupportedInstallation =>
        OperatingSystem.IsWindows() &&
        Environment.ProcessPath is { } processPath &&
        string.Equals(
            Path.GetFileName(processPath),
            "FFXIVConfigManager.exe",
            StringComparison.OrdinalIgnoreCase) &&
        !File.Exists(Path.Combine(AppContext.BaseDirectory, "FFXIVConfigManager.dll"));

    public static bool TryRunUpdater(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length != 2 || args[0] != ApplyArgument)
        {
            return false;
        }

        WindowsUpdatePlan? plan = null;
        try
        {
            var planPath = Path.GetFullPath(args[1]);
            plan = JsonSerializer.Deserialize<WindowsUpdatePlan>(
                    File.ReadAllText(planPath),
                    SerializerOptions)
                ?? throw new InvalidDataException("自动更新计划为空。");
            var logPath = Path.Combine(plan.WorkingDirectory, "update.log");
            WriteLog(logPath, "Updater started.");
            new WindowsUpdateApplier().ApplyAsync(plan, restartApplication: true)
                .GetAwaiter()
                .GetResult();
            WriteLog(logPath, "Update completed.");
        }
        catch (Exception exception)
        {
            exitCode = 1;
            try
            {
                var fallbackLog = Path.Combine(
                    Path.GetTempPath(),
                    "FFXIVConfigManager-update-error.log");
                WriteLog(fallbackLog, exception.ToString());
            }
            catch
            {
            }

            if (plan is not null && File.Exists(plan.TargetExecutablePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = plan.TargetExecutablePath,
                        WorkingDirectory = Path.GetDirectoryName(plan.TargetExecutablePath)!,
                        UseShellExecute = false,
                    });
                }
                catch
                {
                }
            }
        }

        return true;
    }

    public static void Launch(PreparedApplicationUpdate preparedUpdate)
    {
        if (!IsSupportedInstallation)
        {
            throw new NotSupportedException("自动更新仅支持 Windows 单文件发布版。");
        }

        var targetExecutablePath = Path.GetFullPath(Environment.ProcessPath!);
        var targetDirectory = Path.GetDirectoryName(targetExecutablePath)!;
        var writeProbePath = Path.Combine(
            targetDirectory,
            $".ffxivconfigmanager-update-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(writeProbePath, []);
        }
        finally
        {
            if (File.Exists(writeProbePath))
            {
                File.Delete(writeProbePath);
            }
        }

        var plan = new WindowsUpdatePlan(
            Environment.ProcessId,
            targetExecutablePath,
            Path.GetFullPath(preparedUpdate.PackageExecutablePath),
            Path.GetFullPath(preparedUpdate.WorkingDirectory),
            preparedUpdate.PackageSha256);
        var planPath = Path.Combine(preparedUpdate.WorkingDirectory, "update-plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan, SerializerOptions));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.GetFullPath(preparedUpdate.UpdaterExecutablePath),
            WorkingDirectory = preparedUpdate.WorkingDirectory,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ApplyArgument);
        startInfo.ArgumentList.Add(planPath);
        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动自动更新程序。");
    }

    public static string[] ScheduleCleanupAndRemoveArguments(string[] args)
    {
        if (args.Length != 3 ||
            args[0] != CleanupArgument ||
            !int.TryParse(args[2], out var updaterProcessId))
        {
            return args;
        }

        var workingDirectory = Path.GetFullPath(args[1]);
        _ = Task.Run(async () =>
        {
            try
            {
                using var updater = Process.GetProcessById(updaterProcessId);
                await updater.WaitForExitAsync();
            }
            catch (ArgumentException)
            {
            }

            for (var attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (File.Exists(Path.Combine(workingDirectory, ".ffxiv-update")))
                    {
                        Directory.Delete(workingDirectory, recursive: true);
                    }

                    return;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    await Task.Delay(500);
                }
            }
        });
        return [];
    }

    private static void WriteLog(string path, string message)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(
            path,
            $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
    }
}

public sealed class WindowsUpdateApplier(
    IWindowsUpdateFaultInjector? faultInjector = null)
{
    private readonly IWindowsUpdateFaultInjector _faultInjector =
        faultInjector ?? new NoWindowsUpdateFaultInjector();

    public async Task ApplyAsync(
        WindowsUpdatePlan plan,
        bool restartApplication,
        CancellationToken cancellationToken = default)
    {
        ValidatePlan(plan);
        await WaitForParentExitAsync(plan.ParentProcessId, cancellationToken);

        var targetPath = Path.GetFullPath(plan.TargetExecutablePath);
        var targetDirectory = Path.GetDirectoryName(targetPath)!;
        var temporaryPath = Path.Combine(
            targetDirectory,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.update");
        var backupPath = $"{targetPath}.update-backup";
        var backupCreated = false;

        try
        {
            var sourceHash = await ComputeSha256Async(
                plan.PackageExecutablePath,
                cancellationToken);
            if (!string.Equals(sourceHash, plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("暂存更新程序的 SHA-256 校验失败。");
            }

            File.Copy(plan.PackageExecutablePath, temporaryPath, overwrite: false);
            var temporaryHash = await ComputeSha256Async(temporaryPath, cancellationToken);
            if (!string.Equals(temporaryHash, plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("写入安装目录后的更新程序校验失败。");
            }

            _faultInjector.BeforeCommit();
            File.Move(targetPath, backupPath, overwrite: true);
            backupCreated = true;
            _faultInjector.AfterBackupCreated();
            File.Move(temporaryPath, targetPath, overwrite: false);

            var installedHash = await ComputeSha256Async(targetPath, cancellationToken);
            if (!string.Equals(installedHash, plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("替换后的应用程序校验失败。");
            }

            if (restartApplication)
            {
                await StartAndVerifyAsync(targetPath, plan.WorkingDirectory, cancellationToken);
            }

            File.Delete(backupPath);
            backupCreated = false;
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            if (backupCreated || File.Exists(backupPath))
            {
                TryDeleteFile(targetPath);
                File.Move(backupPath, targetPath, overwrite: true);
            }

            throw;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void ValidatePlan(WindowsUpdatePlan plan)
    {
        var targetPath = Path.GetFullPath(plan.TargetExecutablePath);
        var packagePath = Path.GetFullPath(plan.PackageExecutablePath);
        var workingDirectory = Path.GetFullPath(plan.WorkingDirectory);
        if (plan.ParentProcessId <= 0 ||
            !File.Exists(targetPath) ||
            !File.Exists(packagePath) ||
            !File.Exists(Path.Combine(workingDirectory, ".ffxiv-update")) ||
            !IsWithinDirectory(packagePath, workingDirectory) ||
            !string.Equals(
                Path.GetFileName(targetPath),
                "FFXIVConfigManager.exe",
                StringComparison.OrdinalIgnoreCase) ||
            plan.PackageSha256.Length != 64 ||
            !plan.PackageSha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("自动更新计划无效。");
        }
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relative);
    }

    private static async Task WaitForParentExitAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
        }
    }

    private static async Task StartAndVerifyAsync(
        string targetPath,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = targetPath,
            WorkingDirectory = Path.GetDirectoryName(targetPath)!,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(WindowsSelfUpdate.CleanupArgument);
        startInfo.ArgumentList.Add(workingDirectory);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("更新完成后无法重新启动应用程序。");

        using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupTimeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await process.WaitForExitAsync(startupTimeout.Token);
            throw new InvalidOperationException(
                $"更新后的应用程序在启动期间退出，退出代码：{process.ExitCode}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
