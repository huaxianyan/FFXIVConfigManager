using FFXIVConfigManager.Application.Appearances;
using FFXIVConfigManager.Application.Backups;
using FFXIVConfigManager.Application.Portraits;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Appearances;
using FFXIVConfigManager.Domain.Portraits;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Tests;

public sealed class PruneAutomaticBackupsUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_KeepsNewestPerRuleAndDeletesOlder()
    {
        var snapshots = Enumerable.Range(1, 7)
            .Select(index => CreateSnapshotEntry(
                $"snapshot-{index}.zip",
                BackupRule.CharacterRestore,
                DateTimeOffset.UtcNow.AddDays(-index)))
            .ToArray();
        var reader = new StubSnapshotReader(snapshots);
        var snapshotArchive = new StubSnapshotArchiveService();
        var appearances = new StubAppearanceBackupService([
            CreateAppearanceEntry("a-old.zip", DateTimeOffset.UtcNow.AddDays(-9)),
            CreateAppearanceEntry("a-new.zip", DateTimeOffset.UtcNow.AddDays(-1)),
        ]);
        var portraits = new StubPortraitManagementService([
            CreatePortraitEntry("p-old.zip", DateTimeOffset.UtcNow.AddDays(-8)),
            CreatePortraitEntry("p-mid.zip", DateTimeOffset.UtcNow.AddDays(-5)),
            CreatePortraitEntry("p-new.zip", DateTimeOffset.UtcNow.AddDays(-2)),
        ]);
        var useCase = new PruneAutomaticBackupsUseCase(
            reader,
            snapshotArchive,
            appearances,
            portraits);
        var policy = new AutomaticBackupPolicy
        {
            BeforeCharacterRestore = new BackupRetentionSetting(true, 3),
            BeforeAppearanceRestore = new BackupRetentionSetting(true, 1),
            BeforePortraitTransfer = new BackupRetentionSetting(true, 1),
        };

        var result = await useCase.ExecuteAsync(policy, @"C:\library");

        Assert.Equal(7, result.DeletedCount);
        Assert.Equal(4, snapshotArchive.DeletedPaths.Count);
        Assert.Equal(
            new[] { "snapshot-4.zip", "snapshot-5.zip", "snapshot-6.zip", "snapshot-7.zip" },
            snapshotArchive.DeletedPaths.Select(Path.GetFileName).ToArray());
        Assert.Equal(
            new[] { "a-old.zip" },
            appearances.DeletedPaths.Select(Path.GetFileName).ToArray());
        Assert.Equal(
            new[] { "p-mid.zip", "p-old.zip" },
            portraits.DeletedBackups.Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsDisabledRules()
    {
        var snapshots = Enumerable.Range(1, 5)
            .Select(index => CreateSnapshotEntry(
                $"snapshot-{index}.zip",
                BackupRule.CharacterMigration,
                DateTimeOffset.UtcNow.AddDays(-index)))
            .ToArray();
        var useCase = new PruneAutomaticBackupsUseCase(
            new StubSnapshotReader(snapshots),
            new StubSnapshotArchiveService(),
            new StubAppearanceBackupService([]),
            new StubPortraitManagementService([]));
        var policy = new AutomaticBackupPolicy
        {
            BeforeCharacterMigration = new BackupRetentionSetting(false, 2),
        };

        var result = await useCase.ExecuteAsync(policy, @"C:\library");

        Assert.False(result.HasChanges);
        Assert.False(result.HasFailures);
    }

    [Fact]
    public async Task ExecuteAsync_ManualBackupsAreNeverPruned()
    {
        var snapshots = Enumerable.Range(1, 12)
            .Select(index => CreateSnapshotEntry(
                $"manual-{index}.zip",
                BackupRule.Manual,
                DateTimeOffset.UtcNow.AddDays(-index)))
            .ToArray();
        var snapshotArchive = new StubSnapshotArchiveService();
        var useCase = new PruneAutomaticBackupsUseCase(
            new StubSnapshotReader(snapshots),
            snapshotArchive,
            new StubAppearanceBackupService([]),
            new StubPortraitManagementService([]));

        await useCase.ExecuteAsync(AutomaticBackupPolicy.AllEnabled, @"C:\library");

        Assert.Empty(snapshotArchive.DeletedPaths);
    }

    [Fact]
    public async Task ExecuteAsync_BlankLibraryRootReturnsEmptyWithoutScanning()
    {
        var reader = new StubSnapshotReader([]);
        var useCase = new PruneAutomaticBackupsUseCase(
            reader,
            new StubSnapshotArchiveService(),
            new StubAppearanceBackupService([]),
            new StubPortraitManagementService([]));

        var result = await useCase.ExecuteAsync(AutomaticBackupPolicy.AllEnabled, "  ");

        Assert.Same(BackupPruneResult.Empty, result);
        Assert.False(reader.Scanned);
    }

    private static SnapshotLibraryEntry CreateSnapshotEntry(
        string fileName,
        BackupRule rule,
        DateTimeOffset createdAt)
    {
        var manifest = new SnapshotManifest(
            SnapshotManifest.CurrentFormatVersion,
            Guid.NewGuid(),
            createdAt,
            SnapshotReason.BeforeRestore,
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

    private static AppearanceBackupEntry CreateAppearanceEntry(
        string fileName,
        DateTimeOffset createdAt) =>
        new(
            Path.Combine(@"C:\library", fileName),
            createdAt,
            AppearanceBackupIntegrity.Valid,
            null,
            [],
            BackupRule.Appearance);

    private static PortraitBackupEntry CreatePortraitEntry(
        string fileName,
        DateTimeOffset createdAt) =>
        new(
            Path.Combine(@"C:\library", fileName),
            createdAt,
            PortraitBackupIntegrity.Valid,
            null,
            null,
            [],
            BackupRule.Portrait);

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
            DeletedPaths.Add(archivePath);
            return Task.CompletedTask;
        }
    }

    private sealed class StubAppearanceBackupService(
        IReadOnlyList<AppearanceBackupEntry> entries) : IAppearanceBackupService
    {
        public List<string> DeletedPaths { get; } = [];

        public Task<IReadOnlyList<AppearanceSlot>> ScanSlotsAsync(
            string configRoot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AppearanceBackupEntry>> ScanBackupsAsync(
            string libraryRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entries);

        public Task<AppearanceBackupEntry> CreateBackupAsync(
            string sourceFilePath,
            string libraryRoot,
            AppearanceBackupReason reason = AppearanceBackupReason.Manual,
            BackupCategory? category = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AppearanceRestoreResult> RestoreAsync(
            AppearanceBackupEntry backup,
            string targetConfigRoot,
            int targetSlot,
            string libraryRoot,
            bool createRecoveryPoint = true,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string archivePath,
            CancellationToken cancellationToken = default)
        {
            DeletedPaths.Add(archivePath);
            return Task.CompletedTask;
        }
    }

    private sealed class StubPortraitManagementService(
        IReadOnlyList<PortraitBackupEntry> entries) : IPortraitManagementService
    {
        public List<string> DeletedBackups { get; } = [];

        public Task<IReadOnlyList<CharacterPortrait>> ScanCharacterAsync(
            string characterDirectory,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PortraitBackupEntry>> ScanBackupsAsync(
            string libraryRoot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(entries);

        public Task<PortraitBackupEntry> CreateBackupAsync(
            CharacterPortrait source,
            string libraryRoot,
            string schemeName,
            string note,
            PortraitBackupReason reason = PortraitBackupReason.Manual,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PortraitTransferResult> TransferAsync(
            PortraitTransferSource source,
            CharacterPortrait target,
            string libraryRoot,
            bool createRecoveryPoint = true,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PortraitBackupEntry> UpdateBackupMetadataAsync(
            PortraitBackupEntry backup,
            string libraryRoot,
            string schemeName,
            string note,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteBackupAsync(
            PortraitBackupEntry backup,
            string libraryRoot,
            CancellationToken cancellationToken = default)
        {
            DeletedBackups.Add(backup.ArchivePath);
            return Task.CompletedTask;
        }
    }
}
