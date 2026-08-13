using System.IO.Compression;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Snapshots;
using FFXIVConfigManager.Infrastructure.Snapshots;

namespace FFXIVConfigManager.IntegrationTests.Snapshots;

public sealed class ZipSnapshotArchiveServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-snapshot-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateAndVerifyAsync_CreatesValidImmutableSnapshotAndCleansStaging()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDirectory);
        var addonPath = Path.Combine(sourceDirectory, "ADDON.DAT");
        var hotbarPath = Path.Combine(sourceDirectory, "HOTBAR.DAT");
        await File.WriteAllTextAsync(addonPath, "hud-layout");
        await File.WriteAllTextAsync(hotbarPath, "hotbar-layout");
        var service = new ZipSnapshotArchiveService();

        var created = await service.CreateAsync(CreateRequest(
            new SnapshotFileSource(addonPath, "files/ADDON.DAT", "ADDON.DAT"),
            new SnapshotFileSource(hotbarPath, "files/HOTBAR.DAT", "HOTBAR.DAT")));
        var verification = await service.VerifyAsync(created.ArchivePath);

        Assert.True(verification.IsValid);
        Assert.Equal(2, verification.Manifest!.Files.Count);
        Assert.True(File.Exists(created.ArchivePath));
        Assert.False(Directory.Exists(Path.Combine(_root, "library", ".staging", created.Manifest.SnapshotId.ToString("N"))));
    }

    [Fact]
    public async Task VerifyAsync_DetectsFileWhoseContentWasModified()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDirectory);
        var addonPath = Path.Combine(sourceDirectory, "ADDON.DAT");
        await File.WriteAllTextAsync(addonPath, "original");
        var service = new ZipSnapshotArchiveService();
        var created = await service.CreateAsync(CreateRequest(
            new SnapshotFileSource(addonPath, "files/ADDON.DAT", "ADDON.DAT")));

        using (var archive = ZipFile.Open(created.ArchivePath, ZipArchiveMode.Update))
        {
            archive.GetEntry("files/ADDON.DAT")!.Delete();
            var replacement = archive.CreateEntry("files/ADDON.DAT", CompressionLevel.NoCompression);
            await using var writer = new StreamWriter(replacement.Open());
            await writer.WriteAsync("tampered");
        }

        var verification = await service.VerifyAsync(created.ArchivePath);

        Assert.False(verification.IsValid);
        Assert.Contains(verification.Errors, error => error.Contains("哈希不匹配", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryReader_RebuildsIndexAndReportsCorruptedArchives()
    {
        var sourceDirectory = Path.Combine(_root, "source");
        Directory.CreateDirectory(sourceDirectory);
        var addonPath = Path.Combine(sourceDirectory, "ADDON.DAT");
        await File.WriteAllTextAsync(addonPath, "valid");
        var service = new ZipSnapshotArchiveService();
        var created = await service.CreateAsync(CreateRequest(
            new SnapshotFileSource(addonPath, "files/ADDON.DAT", "ADDON.DAT")));
        Assert.Contains(
            $"{Path.DirectorySeparatorChar}backups{Path.DirectorySeparatorChar}",
            created.ArchivePath,
            StringComparison.Ordinal);
        var corruptedPath = Path.Combine(
            _root,
            "library",
            "snapshots",
            "2026",
            "08",
            "corrupted.ffxivconfig.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(corruptedPath)!);
        await File.WriteAllTextAsync(corruptedPath, "not-a-zip");
        var reader = new PhysicalSnapshotLibraryReader(service);

        var entries = await reader.ScanAsync(Path.Combine(_root, "library"));

        Assert.Equal(2, entries.Count);
        Assert.Single(entries, entry => entry.IntegrityStatus == SnapshotIntegrityStatus.Valid);
        Assert.Single(entries, entry => entry.IntegrityStatus == SnapshotIntegrityStatus.Corrupted);
    }

    [Fact]
    public async Task DeleteAsync_RemovesPublishedBackup()
    {
        var sourceDirectory = Path.Combine(_root, "source-delete");
        Directory.CreateDirectory(sourceDirectory);
        var addonPath = Path.Combine(sourceDirectory, "ADDON.DAT");
        await File.WriteAllTextAsync(addonPath, "valid");
        var service = new ZipSnapshotArchiveService();
        var created = await service.CreateAsync(CreateRequest(
            new SnapshotFileSource(addonPath, "files/ADDON.DAT", "ADDON.DAT")));

        await service.DeleteAsync(created.ArchivePath);

        Assert.False(File.Exists(created.ArchivePath));
    }

    [Fact]
    public async Task CreateAsync_MissingSourceFailsWithoutPublishingSnapshot()
    {
        var service = new ZipSnapshotArchiveService();
        var request = CreateRequest(new SnapshotFileSource(
            Path.Combine(_root, "missing.DAT"),
            "files/ADDON.DAT",
            "ADDON.DAT"));

        await Assert.ThrowsAsync<FileNotFoundException>(() => service.CreateAsync(request));

        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(_root, "library"),
            "*.ffxivconfig.zip",
            SearchOption.AllDirectories));
    }

    private SnapshotArchiveRequest CreateRequest(params SnapshotFileSource[] files) =>
        new(
            Path.Combine(_root, "library"),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-08-11T03:00:00Z"),
            SnapshotReason.Manual,
            new SnapshotSource(
                Guid.NewGuid(),
                "测试",
                "FFXIV_CHR0000000000000001"),
            files);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
