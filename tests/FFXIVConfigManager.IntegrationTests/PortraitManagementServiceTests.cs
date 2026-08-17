using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using FFXIVConfigManager.Application.Portraits;
using FFXIVConfigManager.Domain.Portraits;
using FFXIVConfigManager.Infrastructure.Portraits;

namespace FFXIVConfigManager.IntegrationTests;

public sealed class PortraitManagementServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "FFXIVConfigManager.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScanCharacterAsync_MapsGearsetToBannerAndReadsIdentifyingFields()
    {
        var character = CreateCharacter("FFXIV_CHR1111111111111111", 7, "测试套装", 19, 0, 1_700_000_000);
        var service = new ZipPortraitManagementService();

        var portrait = Assert.Single(await service.ScanCharacterAsync(character));

        Assert.Equal(7, portrait.GearsetNumber);
        Assert.Equal(19, portrait.ClassJobId);
        Assert.Equal("测试套装", portrait.GearsetName);
        Assert.Equal(0, portrait.BannerIndex);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), portrait.Data.LastUpdatedUtc);
    }

    [Fact]
    public async Task CreateBackupAsync_RequiresAndPreservesSchemeNameAndNote()
    {
        var character = CreateCharacter("FFXIV_CHR1111111111111111", 1, "骑士", 19, 0, 1_700_000_000);
        var library = Path.Combine(_root, "library");
        var service = new ZipPortraitManagementService();
        var source = Assert.Single(await service.ScanCharacterAsync(character));

        var backup = await service.CreateBackupAsync(source, library, "高难肖像", "测试备注");
        var scanned = Assert.Single(await service.ScanBackupsAsync(library));

        Assert.Equal(backup.Manifest!.BackupId, scanned.Manifest!.BackupId);
        Assert.Equal("高难肖像", scanned.Manifest.SchemeName);
        Assert.Equal("测试备注", scanned.Manifest.Note);
        Assert.Equal(1, scanned.Manifest.Source.GearsetNumber);
        Assert.Equal("骑士", scanned.Manifest.Source.GearsetName);
        Assert.Equal(PortraitBackupReason.Manual, scanned.Manifest.Reason);
    }

    [Fact]
    public async Task UpdateBackupMetadataAsync_ChangesOnlySchemeNameAndNote()
    {
        var character = CreateCharacter("FFXIV_CHR1111111111111111", 1, "骑士", 19, 0, 1_700_000_000);
        var library = Path.Combine(_root, "library");
        var service = new ZipPortraitManagementService();
        var source = Assert.Single(await service.ScanCharacterAsync(character));
        var backup = await service.CreateBackupAsync(source, library, "旧方案", "旧备注");
        var originalManifest = backup.Manifest!;
        var originalData = ReadArchiveEntry(backup.ArchivePath, PortraitBackupManifest.DataEntryName);

        var updated = await service.UpdateBackupMetadataAsync(
            backup,
            library,
            " 新方案 ",
            " 新备注 ");
        var scanned = Assert.Single(await service.ScanBackupsAsync(library));

        Assert.Equal("新方案", updated.Manifest!.SchemeName);
        Assert.Equal("新备注", updated.Manifest.Note);
        Assert.Equal(originalManifest with { SchemeName = "新方案", Note = "新备注" }, updated.Manifest);
        Assert.Equal(updated.Manifest, scanned.Manifest);
        Assert.Equal(originalData, ReadArchiveEntry(updated.ArchivePath, PortraitBackupManifest.DataEntryName));
    }

    [Fact]
    public async Task UpdateBackupMetadataAsync_InvalidInputLeavesArchiveUnchanged()
    {
        var character = CreateCharacter("FFXIV_CHR1111111111111111", 1, "骑士", 19, 0, 1_700_000_000);
        var library = Path.Combine(_root, "library");
        var service = new ZipPortraitManagementService();
        var source = Assert.Single(await service.ScanCharacterAsync(character));
        var backup = await service.CreateBackupAsync(source, library, "原方案", "原备注");
        var originalArchive = await File.ReadAllBytesAsync(backup.ArchivePath);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UpdateBackupMetadataAsync(backup, library, " ", "新备注"));

        Assert.Equal(originalArchive, await File.ReadAllBytesAsync(backup.ArchivePath));
    }

    [Fact]
    public async Task UpdateBackupMetadataAsync_RejectsSelectionChangedAfterScan()
    {
        var character = CreateCharacter("FFXIV_CHR1111111111111111", 1, "骑士", 19, 0, 1_700_000_000);
        var library = Path.Combine(_root, "library");
        var service = new ZipPortraitManagementService();
        var source = Assert.Single(await service.ScanCharacterAsync(character));
        var original = await service.CreateBackupAsync(source, library, "原方案", "原备注");
        await service.UpdateBackupMetadataAsync(original, library, "其他修改", "其他备注");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.UpdateBackupMetadataAsync(original, library, "过期修改", "过期备注"));

        var current = Assert.Single(await service.ScanBackupsAsync(library));
        Assert.Equal("其他修改", current.Manifest!.SchemeName);
        Assert.Equal("其他备注", current.Manifest.Note);
    }

    [Fact]
    public async Task UpdateBackupMetadataAsync_RejectsArchiveOutsideCurrentLibrary()
    {
        Directory.CreateDirectory(_root);
        var outsidePath = Path.Combine(_root, "outside.ffxivportrait.zip");
        await File.WriteAllBytesAsync(outsidePath, [1, 2, 3]);
        var entry = new PortraitBackupEntry(
            outsidePath,
            DateTimeOffset.UtcNow,
            PortraitBackupIntegrity.Corrupted,
            null,
            null,
            ["损坏"]);
        var service = new ZipPortraitManagementService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateBackupMetadataAsync(entry, Path.Combine(_root, "library"), "方案", "备注"));

        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(outsidePath));
    }

    [Fact]
    public async Task DeleteBackupAsync_RemovesOnlySelectedSchemeAndCleansEmptyDirectories()
    {
        var character = CreateCharacter("FFXIV_CHR1111111111111111", 1, "骑士", 19, 0, 1_700_000_000);
        var library = Path.Combine(_root, "library");
        var service = new ZipPortraitManagementService();
        var source = Assert.Single(await service.ScanCharacterAsync(character));
        var backup = await service.CreateBackupAsync(source, library, "待删除方案", "测试删除");

        await service.DeleteBackupAsync(backup, library);

        Assert.False(File.Exists(backup.ArchivePath));
        Assert.Empty(await service.ScanBackupsAsync(library));
    }

    [Fact]
    public async Task DeleteBackupAsync_RejectsArchiveOutsideCurrentLibrary()
    {
        Directory.CreateDirectory(_root);
        var outsidePath = Path.Combine(_root, "outside.ffxivportrait.zip");
        await File.WriteAllBytesAsync(outsidePath, [1, 2, 3]);
        var entry = new PortraitBackupEntry(
            outsidePath,
            DateTimeOffset.UtcNow,
            PortraitBackupIntegrity.Corrupted,
            null,
            null,
            ["损坏"]);
        var service = new ZipPortraitManagementService();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteBackupAsync(entry, Path.Combine(_root, "library")));

        Assert.True(File.Exists(outsidePath));
    }

    [Fact]
    public async Task TransferAsync_CopiesVisualDataAndPreservesTargetIdentityFields()
    {
        var sourceCharacter = CreateCharacter("FFXIV_CHR1111111111111111", 1, "来源", 19, 0, 1_700_000_000, visualSeed: 10);
        var targetCharacter = CreateCharacter("FFXIV_CHR2222222222222222", 2, "目标", 24, 0, 1_600_000_000, visualSeed: 80);
        var library = Path.Combine(_root, "library");
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var service = new ZipPortraitManagementService(new FixedTimeProvider(now));
        var source = Assert.Single(await service.ScanCharacterAsync(sourceCharacter));
        var target = Assert.Single(await service.ScanCharacterAsync(targetCharacter));
        var targetBefore = target.Data.SerializedRecord.ToArray();

        var result = await service.TransferAsync(
            PortraitTransferSource.FromCharacter(source),
            target,
            library);
        var targetAfter = Assert.Single(await service.ScanCharacterAsync(targetCharacter));

        Assert.Equal(source.Data.SerializedRecord.AsSpan(0x04, 0x58).ToArray(),
            targetAfter.Data.SerializedRecord.AsSpan(0x04, 0x58).ToArray());
        Assert.Equal(targetBefore.AsSpan(0, 4).ToArray(), targetAfter.Data.SerializedRecord.AsSpan(0, 4).ToArray());
        Assert.Equal(targetBefore.AsSpan(0x62).ToArray(), targetAfter.Data.SerializedRecord.AsSpan(0x62).ToArray());
        Assert.Equal(now, targetAfter.Data.LastUpdatedUtc);
        Assert.Equal(PortraitBackupReason.BeforeTransfer, result.RecoveryPoint.Manifest!.Reason);
    }

    [Fact]
    public async Task TransferAsync_RejectsCharacterSourceThatChangedAfterSelection()
    {
        var sourceCharacter = CreateCharacter("FFXIV_CHR1111111111111111", 1, "来源", 19, 0, 1_700_000_000, visualSeed: 10);
        var targetCharacter = CreateCharacter("FFXIV_CHR2222222222222222", 2, "目标", 24, 0, 1_600_000_000, visualSeed: 80);
        var library = Path.Combine(_root, "library");
        var service = new ZipPortraitManagementService();
        var selectedSource = Assert.Single(await service.ScanCharacterAsync(sourceCharacter));
        var selectedTarget = Assert.Single(await service.ScanCharacterAsync(targetCharacter));
        var targetPath = Path.Combine(targetCharacter, "UISAVE.DAT");
        var targetBefore = await File.ReadAllBytesAsync(targetPath);
        File.WriteAllBytes(
            Path.Combine(sourceCharacter, "UISAVE.DAT"),
            CreateUiSaveFile(CreatePortraitRecord(1_700_000_001, 11)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransferAsync(
            PortraitTransferSource.FromCharacter(selectedSource),
            selectedTarget,
            library));

        Assert.Equal(targetBefore, await File.ReadAllBytesAsync(targetPath));
        Assert.Empty(await service.ScanBackupsAsync(library));
    }

    [Fact]
    public async Task TransferAsync_RevalidatesBackupImmediatelyBeforeRestore()
    {
        var sourceCharacter = CreateCharacter("FFXIV_CHR1111111111111111", 1, "来源", 19, 0, 1_700_000_000, visualSeed: 10);
        var targetCharacter = CreateCharacter("FFXIV_CHR2222222222222222", 2, "目标", 24, 0, 1_600_000_000, visualSeed: 80);
        var library = Path.Combine(_root, "library");
        var service = new ZipPortraitManagementService();
        var source = Assert.Single(await service.ScanCharacterAsync(sourceCharacter));
        var backup = await service.CreateBackupAsync(source, library, "方案", "备注");
        var target = Assert.Single(await service.ScanCharacterAsync(targetCharacter));
        var targetPath = Path.Combine(targetCharacter, "UISAVE.DAT");
        var targetBefore = await File.ReadAllBytesAsync(targetPath);
        await File.WriteAllBytesAsync(backup.ArchivePath, [1, 2, 3]);

        await Assert.ThrowsAsync<InvalidDataException>(() => service.TransferAsync(
            PortraitTransferSource.FromBackup(backup),
            target,
            library));

        Assert.Equal(targetBefore, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task TransferAsync_FailureAfterReplacementRollsBackOriginalUiSave()
    {
        var sourceCharacter = CreateCharacter("FFXIV_CHR1111111111111111", 1, "来源", 19, 0, 1_700_000_000, visualSeed: 10);
        var targetCharacter = CreateCharacter("FFXIV_CHR2222222222222222", 2, "目标", 24, 0, 1_600_000_000, visualSeed: 80);
        var library = Path.Combine(_root, "library");
        var targetPath = Path.Combine(targetCharacter, "UISAVE.DAT");
        var original = await File.ReadAllBytesAsync(targetPath);
        var normal = new ZipPortraitManagementService();
        var source = Assert.Single(await normal.ScanCharacterAsync(sourceCharacter));
        var target = Assert.Single(await normal.ScanCharacterAsync(targetCharacter));
        var failing = new ZipPortraitManagementService(
            faultInjector: new ThrowAfterReplace());

        await Assert.ThrowsAsync<IOException>(() =>
            failing.TransferAsync(PortraitTransferSource.FromCharacter(source), target, library));

        Assert.Equal(original, await File.ReadAllBytesAsync(targetPath));
        Assert.False(Directory.Exists(Path.Combine(targetCharacter, ".ffxivconfigmanager")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static byte[] ReadArchiveEntry(string archivePath, string entryName)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(entryName)!;
        using var source = entry.Open();
        using var destination = new MemoryStream();
        source.CopyTo(destination);
        return destination.ToArray();
    }

    private string CreateCharacter(
        string folderName,
        int gearsetNumber,
        string gearsetName,
        byte classJobId,
        int bannerIndex,
        uint updatedAt,
        byte visualSeed = 1)
    {
        var root = Path.Combine(_root, folderName);
        Directory.CreateDirectory(root);
        File.WriteAllBytes(
            Path.Combine(root, "GEARSET.DAT"),
            CreateGearsetFile(gearsetNumber, gearsetName, classJobId, bannerIndex));
        File.WriteAllBytes(
            Path.Combine(root, "UISAVE.DAT"),
            CreateUiSaveFile(CreatePortraitRecord(updatedAt, visualSeed)));
        return root;
    }

    private static byte[] CreateGearsetFile(
        int gearsetNumber,
        string name,
        byte classJobId,
        int bannerIndex)
    {
        const int fileLength = 45_689;
        const int maximumSize = fileLength - 32;
        const int contentSize = 45_657;
        var file = new byte[fileLength];
        BinaryPrimitives.WriteUInt32LittleEndian(file, 0x006E0005);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), maximumSize);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(8), contentSize);
        file[16] = 0xFF;
        var content = new byte[contentSize - 1];
        var record = content.AsSpan((gearsetNumber - 1) * 452, 452);
        record[0] = checked((byte)(gearsetNumber - 1));
        Encoding.UTF8.GetBytes(name).CopyTo(record[5..]);
        record[0x35] = classJobId;
        record[0x3A] = checked((byte)(bannerIndex + 1));
        record[0x3B] = 0x01;
        ApplyMask(content, 0x73);
        content.CopyTo(file, 17);
        return file;
    }

    private static byte[] CreateUiSaveFile(params byte[][] records)
    {
        var bannerLength = 32 + records.Length * PortraitData.SerializedSize;
        var banner = new byte[bannerLength];
        BinaryPrimitives.WriteUInt32LittleEndian(banner, 1);
        BinaryPrimitives.WriteInt32LittleEndian(banner.AsSpan(0x14), bannerLength - 32);
        for (var index = 0; index < records.Length; index++)
        {
            records[index].CopyTo(banner, 32 + index * PortraitData.SerializedSize);
        }

        var decrypted = new byte[16 + 16 + bannerLength + 4];
        BinaryPrimitives.WriteInt16LittleEndian(decrypted.AsSpan(16), 23);
        BinaryPrimitives.WriteInt32LittleEndian(decrypted.AsSpan(24), bannerLength);
        banner.CopyTo(decrypted, 32);
        var encrypted = decrypted.ToArray();
        ApplyMask(encrypted, 0x31);
        var file = new byte[16 + encrypted.Length];
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8), encrypted.Length);
        encrypted.CopyTo(file, 16);
        return file;
    }

    private static byte[] CreatePortraitRecord(uint updatedAt, byte visualSeed)
    {
        var fields = new (ushort Tag, int Length)[]
        {
            (0, 2), (2, 2), (3, 2), (4, 2), (5, 2), (6, 4), (7, 2),
            (8, 6), (9, 6), (10, 2), (11, 2), (16, 4), (17, 2), (18, 4),
            (14, 4), (15, 2), (19, 4), (20, 4), (21, 4), (22, 6), (23, 4), (24, 4),
        };
        var record = new byte[PortraitData.SerializedSize];
        var offset = 0;
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset), field.Tag);
            offset += 2;
            record.AsSpan(offset, field.Length).Fill(visualSeed);
            offset += field.Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(PortraitData.LastUpdatedOffset), updatedAt);
        return record;
    }

    private static void ApplyMask(Span<byte> data, byte mask)
    {
        for (var index = 0; index < data.Length; index++)
        {
            data[index] ^= mask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class ThrowAfterReplace : IPortraitTransferFaultInjector
    {
        public void AfterTargetReplaced() => throw new IOException("模拟肖像替换失败。");
    }
}
