using FFXIVConfigManager.Application.Backups;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Tests;

public sealed class PruneManualCharacterBackupsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_KeepsNewestManualBackupsAndLeavesAutomaticOnes()
    {
        var snapshots = new[]
        {
            CreateEntry("manual-1.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-5)),
            CreateEntry("manual-2.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-4)),
            CreateEntry("manual-3.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-3)),
            CreateEntry("manual-4.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-2)),
            CreateEntry("manual-5.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-1)),
            CreateEntry("restore-1.zip", BackupRule.CharacterRestore, DateTimeOffset.UtcNow.AddDays(-9)),
            CreateEntry("migration-1.zip", BackupRule.CharacterMigration, DateTimeOffset.UtcNow.AddDays(-8)),
        };
        var reader = new StubSnapshotReader(snapshots);
        var archive = new StubSnapshotArchiveService();
        var useCase = new PruneManualCharacterBackupsUseCase(reader, archive);

        var result = await useCase.ExecuteAsync(new BackupRetentionSetting(true, 2), @"C:\library");

        Assert.True(reader.Scanned);
        Assert.Equal(3, result.DeletedCount);
        Assert.False(result.HasFailures);
        Assert.Equal(
            new[] { "manual-1.zip", "manual-2.zip", "manual-3.zip" },
            archive.DeletedPaths
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_DisabledSettingReturnsEmptyWithoutScanning()
    {
        var reader = new StubSnapshotReader([
            CreateEntry("manual-1.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-5)),
            CreateEntry("manual-2.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-4)),
        ]);
        var archive = new StubSnapshotArchiveService();
        var useCase = new PruneManualCharacterBackupsUseCase(reader, archive);

        var result = await useCase.ExecuteAsync(
            new BackupRetentionSetting(false, 1),
            @"C:\library");

        Assert.Same(BackupPruneResult.Empty, result);
        Assert.False(reader.Scanned);
        Assert.Empty(archive.DeletedPaths);
    }

    [Fact]
    public async Task ExecuteAsync_BlankLibraryRootReturnsEmptyWithoutScanning()
    {
        var reader = new StubSnapshotReader([]);
        var useCase = new PruneManualCharacterBackupsUseCase(
            reader,
            new StubSnapshotArchiveService());

        var result = await useCase.ExecuteAsync(new BackupRetentionSetting(true, 1), "  ");

        Assert.Same(BackupPruneResult.Empty, result);
        Assert.False(reader.Scanned);
    }

    [Fact]
    public async Task ExecuteAsync_DeleteFailureIsReportedAndOthersContinue()
    {
        var snapshots = new[]
        {
            CreateEntry("manual-1.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-3)),
            CreateEntry("manual-2.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-2)),
            CreateEntry("manual-3.zip", BackupRule.Manual, DateTimeOffset.UtcNow.AddDays(-1)),
        };
        var archive = new StubSnapshotArchiveService
        {
            FailOn = @"C:\library\manual-1.zip",
        };
        var useCase = new PruneManualCharacterBackupsUseCase(
            new StubSnapshotReader(snapshots),
            archive);

        var result = await useCase.ExecuteAsync(new BackupRetentionSetting(true, 1), @"C:\library");

        Assert.Equal(1, result.DeletedCount);
        Assert.True(result.HasFailures);
        Assert.Contains("manual-1.zip", result.Failures[0]);
        Assert.Equal(
            new[] { "manual-2.zip" },
            archive.DeletedPaths.Select(Path.GetFileName).ToArray());
    }

    private static SnapshotLibraryEntry CreateEntry(
        string fileName,
        BackupRule rule,
        DateTimeOffset createdAt)
    {
        var manifest = new SnapshotManifest(
            SnapshotManifest.CurrentFormatVersion,
            Guid.NewGuid(),
            createdAt,
            SnapshotReason.Manual,
            new SnapshotSource(Guid.NewGuid(), "测试", "FFXIV_CHR0000000000000001", "角色"),
            []);
        return new SnapshotLibraryEntry(
            Path.Combine(@"C:\library", fileName),
            10,
            createdAt,
            SnapshotIntegrityStatus.Valid,
            manifest,
            [],
            rule);
    }

    private sealed class StubSnapshotReader(
        IReadOnlyList<SnapshotLibraryEntry> entries) : ISnapshotLibraryReader
    {
        public bool Scanned { get; private set; }

        public Task<IReadOnlyList<SnapshotLibraryEntry>> ScanAsync(
            string libraryRoot,
            CancellationToken cancellationToken = default)
        {
            Scanned = true;
            return Task.FromResult(entries);
        }
    }

    private sealed class StubSnapshotArchiveService : ISnapshotArchiveService
    {
        public List<string> DeletedPaths { get; } = [];

        public string? FailOn { get; init; }

        public Task<CreatedSnapshot> CreateAsync(
            SnapshotArchiveRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SnapshotVerificationResult> VerifyAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            if (string.Equals(archivePath, FailOn, StringComparison.Ordinal))
            {
                throw new IOException("模拟删除失败");
            }

            DeletedPaths.Add(archivePath);
            return Task.CompletedTask;
        }
    }
}
