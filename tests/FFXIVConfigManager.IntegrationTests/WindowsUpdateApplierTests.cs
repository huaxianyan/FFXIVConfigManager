using System.Security.Cryptography;
using System.Text;
using FFXIVConfigManager.Platform.Windows.Updates;

namespace FFXIVConfigManager.IntegrationTests;

public sealed class WindowsUpdateApplierTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-update-applier-{Guid.NewGuid():N}");

    [Fact]
    public async Task ApplyAsync_ReplacesExecutableAndRemovesBackup()
    {
        var plan = await CreatePlanAsync();

        await new WindowsUpdateApplier().ApplyAsync(plan, restartApplication: false);

        Assert.Equal("new version", await File.ReadAllTextAsync(plan.TargetExecutablePath));
        Assert.False(File.Exists($"{plan.TargetExecutablePath}.update-backup"));
    }

    [Fact]
    public async Task ApplyAsync_FailureAfterBackupRestoresOriginalExecutable()
    {
        var plan = await CreatePlanAsync();
        var applier = new WindowsUpdateApplier(new ThrowAfterBackupFaultInjector());

        await Assert.ThrowsAsync<IOException>(() =>
            applier.ApplyAsync(plan, restartApplication: false));

        Assert.Equal("old version", await File.ReadAllTextAsync(plan.TargetExecutablePath));
        Assert.False(File.Exists($"{plan.TargetExecutablePath}.update-backup"));
        Assert.Empty(Directory.EnumerateFiles(
            Path.GetDirectoryName(plan.TargetExecutablePath)!,
            "*.update"));
    }

    [Fact]
    public async Task ApplyAsync_CorruptedStagedExecutableDoesNotModifyTarget()
    {
        var plan = await CreatePlanAsync();
        await File.WriteAllTextAsync(plan.PackageExecutablePath, "tampered");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new WindowsUpdateApplier().ApplyAsync(plan, restartApplication: false));

        Assert.Equal("old version", await File.ReadAllTextAsync(plan.TargetExecutablePath));
        Assert.False(File.Exists($"{plan.TargetExecutablePath}.update-backup"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task<WindowsUpdatePlan> CreatePlanAsync()
    {
        var installDirectory = Path.Combine(_root, "arbitrary", "install", "directory");
        var workingDirectory = Path.Combine(_root, "updates", Guid.NewGuid().ToString("N"));
        var packageDirectory = Path.Combine(workingDirectory, "package");
        Directory.CreateDirectory(installDirectory);
        Directory.CreateDirectory(packageDirectory);
        await File.WriteAllTextAsync(Path.Combine(workingDirectory, ".ffxiv-update"), "v1.1.0");

        var targetPath = Path.Combine(installDirectory, "FFXIVConfigManager.exe");
        var packagePath = Path.Combine(packageDirectory, "FFXIVConfigManager.exe");
        await File.WriteAllTextAsync(targetPath, "old version");
        await File.WriteAllTextAsync(packagePath, "new version");
        var packageHash = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(packagePath)));

        return new WindowsUpdatePlan(
            int.MaxValue,
            targetPath,
            packagePath,
            workingDirectory,
            packageHash);
    }

    private sealed class ThrowAfterBackupFaultInjector : IWindowsUpdateFaultInjector
    {
        public void BeforeCommit()
        {
        }

        public void AfterBackupCreated() => throw new IOException("模拟替换失败。");
    }
}
