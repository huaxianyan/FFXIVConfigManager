using System.Buffers.Binary;
using System.Text;
using FFXIVConfigManager.Domain.Appearances;
using FFXIVConfigManager.Infrastructure.Appearances;

namespace FFXIVConfigManager.IntegrationTests;

public sealed class AppearanceBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FFXIVConfigManager.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CreateAndScanAsync_PreservesMetadataWithoutUsingSlotAsIdentity()
    {
        var configRoot = Path.Combine(_root, "config");
        var libraryRoot = Path.Combine(_root, "library");
        Directory.CreateDirectory(configRoot);
        var source = Path.Combine(configRoot, "FFXIV_CHARA_07.dat");
        await File.WriteAllBytesAsync(source, CreateData(
            AppearanceRace.AuRa,
            11,
            AppearanceGender.Female,
            "用于搜索的备注"));
        var service = new ZipAppearanceBackupService();

        var created = await service.CreateBackupAsync(source, libraryRoot);
        var scanned = await service.ScanBackupsAsync(libraryRoot);

        var entry = Assert.Single(scanned);
        Assert.Equal(created.Manifest!.BackupId, entry.Manifest!.BackupId);
        Assert.Equal("FFXIV_CHARA_07.dat", entry.Manifest.SourceFileName);
        Assert.Equal("用于搜索的备注", entry.Manifest.Appearance.Comment);
        Assert.DoesNotContain("FFXIV_CHARA", Path.GetFileName(entry.ArchivePath));
    }

    [Fact]
    public async Task RestoreAsync_WritesSelectedSlotAndCreatesRecoveryPointForOccupiedSlot()
    {
        var configRoot = Path.Combine(_root, "config");
        var libraryRoot = Path.Combine(_root, "library");
        Directory.CreateDirectory(configRoot);
        var source = Path.Combine(configRoot, "FFXIV_CHARA_01.dat");
        var target = Path.Combine(configRoot, "FFXIV_CHARA_12.dat");
        var sourceData = CreateData(AppearanceRace.Viera, 15, AppearanceGender.Female, "新数据");
        var targetData = CreateData(AppearanceRace.Hyur, 1, AppearanceGender.Male, "原数据");
        await File.WriteAllBytesAsync(source, sourceData);
        await File.WriteAllBytesAsync(target, targetData);
        var service = new ZipAppearanceBackupService();
        var backup = await service.CreateBackupAsync(source, libraryRoot);

        var result = await service.RestoreAsync(backup, configRoot, 12, libraryRoot);

        Assert.Equal(sourceData, await File.ReadAllBytesAsync(target));
        Assert.NotNull(result.RecoveryPoint);
        Assert.Equal(AppearanceBackupReason.BeforeRestore, result.RecoveryPoint.Manifest!.Reason);
        Assert.Equal("原数据", result.RecoveryPoint.Manifest.Appearance.Comment);
    }

    [Fact]
    public async Task RestoreAsync_FailureAfterReplacementRollsBackEmptySlot()
    {
        var configRoot = Path.Combine(_root, "config");
        var libraryRoot = Path.Combine(_root, "library");
        Directory.CreateDirectory(configRoot);
        var source = Path.Combine(configRoot, "FFXIV_CHARA_01.dat");
        var target = Path.Combine(configRoot, "FFXIV_CHARA_02.dat");
        await File.WriteAllBytesAsync(source, CreateData(
            AppearanceRace.Viera, 15, AppearanceGender.Female, "新数据"));
        var normalService = new ZipAppearanceBackupService();
        var backup = await normalService.CreateBackupAsync(source, libraryRoot);
        var failingService = new ZipAppearanceBackupService(new ThrowAfterReplace());

        await Assert.ThrowsAsync<IOException>(() =>
            failingService.RestoreAsync(backup, configRoot, 2, libraryRoot));

        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task RestoreAsync_FailureAfterReplacementRollsBackOccupiedSlot()
    {
        var configRoot = Path.Combine(_root, "config");
        var libraryRoot = Path.Combine(_root, "library");
        Directory.CreateDirectory(configRoot);
        var source = Path.Combine(configRoot, "FFXIV_CHARA_01.dat");
        var target = Path.Combine(configRoot, "FFXIV_CHARA_02.dat");
        await File.WriteAllBytesAsync(source, CreateData(
            AppearanceRace.Viera, 15, AppearanceGender.Female, "新数据"));
        var original = CreateData(AppearanceRace.Hyur, 1, AppearanceGender.Male, "原数据");
        await File.WriteAllBytesAsync(target, original);
        var normalService = new ZipAppearanceBackupService();
        var backup = await normalService.CreateBackupAsync(source, libraryRoot);
        var failingService = new ZipAppearanceBackupService(new ThrowAfterReplace());

        await Assert.ThrowsAsync<IOException>(() =>
            failingService.RestoreAsync(backup, configRoot, 2, libraryRoot));

        Assert.Equal(original, await File.ReadAllBytesAsync(target));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] CreateData(
        AppearanceRace race,
        byte tribe,
        AppearanceGender gender,
        string comment)
    {
        var data = new byte[AppearanceData.FileSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, AppearanceData.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        data[0x10] = (byte)race;
        data[0x11] = (byte)gender;
        data[0x12] = 1;
        data[0x14] = tribe;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2C), 1_700_000_000);
        Encoding.UTF8.GetBytes(comment).CopyTo(data.AsSpan(0x30));
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(8),
            AppearanceData.CalculateChecksum(data.AsSpan(0x10)));
        return data;
    }

    private sealed class ThrowAfterReplace : IAppearanceRestoreFaultInjector
    {
        public void AfterTargetReplaced() => throw new IOException("模拟替换失败。");
    }
}
