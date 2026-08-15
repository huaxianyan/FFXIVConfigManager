using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Tests;

public sealed class RestoreSnapshotUseCaseTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-restore-use-case-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExecuteAsync_MissingCharacterCreatesDirectoryWithoutRecoveryPoint()
    {
        Directory.CreateDirectory(_root);
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, _root);
        var target = CreateMissingTarget(profile);
        var archive = new VerifiedArchiveService(CreateManifest(profile, target));
        var restorer = new CapturingRestorer();
        var useCase = new RestoreSnapshotUseCase(
            archive,
            new CreateCharacterSnapshotUseCase(archive, TimeProvider.System),
            restorer);

        var result = await useCase.ExecuteAsync(
            CreateEntry(archive.Manifest),
            profile,
            target,
            Path.Combine(_root, "library"));

        Assert.True(Directory.Exists(target.FullPath));
        Assert.Equal(target.FullPath, restorer.Request!.TargetDirectory);
        Assert.Null(result.RecoveryPoint);
        Assert.True(result.CreatedTargetDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_UnscannedExistingFilesAreNotOverwritten()
    {
        Directory.CreateDirectory(_root);
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, _root);
        var target = CreateMissingTarget(profile);
        Directory.CreateDirectory(target.FullPath);
        await File.WriteAllTextAsync(Path.Combine(target.FullPath, "ADDON.DAT"), "current");
        var archive = new VerifiedArchiveService(CreateManifest(profile, target));
        var restorer = new CapturingRestorer();
        var useCase = new RestoreSnapshotUseCase(
            archive,
            new CreateCharacterSnapshotUseCase(archive, TimeProvider.System),
            restorer);

        await Assert.ThrowsAsync<InvalidOperationException>(() => useCase.ExecuteAsync(
            CreateEntry(archive.Manifest),
            profile,
            target,
            Path.Combine(_root, "library")));

        Assert.Null(restorer.Request);
        Assert.Equal("current", await File.ReadAllTextAsync(Path.Combine(target.FullPath, "ADDON.DAT")));
    }

    [Fact]
    public async Task ExecuteAsync_FailedNewCharacterRestoreRemovesEmptyDirectory()
    {
        Directory.CreateDirectory(_root);
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, _root);
        var target = CreateMissingTarget(profile);
        var archive = new VerifiedArchiveService(CreateManifest(profile, target));
        var useCase = new RestoreSnapshotUseCase(
            archive,
            new CreateCharacterSnapshotUseCase(archive, TimeProvider.System),
            new CapturingRestorer(throwOnRestore: true));

        await Assert.ThrowsAsync<IOException>(() => useCase.ExecuteAsync(
            CreateEntry(archive.Manifest),
            profile,
            target,
            Path.Combine(_root, "library")));

        Assert.False(Directory.Exists(target.FullPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static CharacterConfiguration CreateMissingTarget(GameProfile profile)
    {
        var folder = CharacterFolderName.Create("FFXIV_CHR0000000000000001");
        return new CharacterConfiguration(
            profile.Id,
            folder,
            Path.Combine(profile.ConfigRoot, folder.Value),
            DateTimeOffset.MinValue,
            []);
    }

    private static SnapshotManifest CreateManifest(
        GameProfile profile,
        CharacterConfiguration target) =>
        new(
            SnapshotManifest.CurrentFormatVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            SnapshotReason.Manual,
            new SnapshotSource(profile.Id, profile.Name, target.FolderName.Value, "测试角色"),
            [
                new SnapshotFileEntry(
                    "files/ADDON.DAT",
                    "ADDON.DAT",
                    1,
                    DateTimeOffset.UtcNow,
                    new string('A', 64)),
            ]);

    private static SnapshotLibraryEntry CreateEntry(SnapshotManifest manifest) =>
        new(
            Path.Combine(Path.GetTempPath(), "snapshot.ffxivconfig.zip"),
            1,
            DateTimeOffset.UtcNow,
            SnapshotIntegrityStatus.Valid,
            manifest,
            []);

    private sealed class VerifiedArchiveService(SnapshotManifest manifest) : ISnapshotArchiveService
    {
        public SnapshotManifest Manifest { get; } = manifest;

        public Task<CreatedSnapshot> CreateAsync(
            SnapshotArchiveRequest request,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("新角色恢复不应创建操作前恢复点。");

        public Task<SnapshotVerificationResult> VerifyAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SnapshotVerificationResult.Valid(Manifest));

        public Task DeleteAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CapturingRestorer(bool throwOnRestore = false) : ITransactionalSnapshotRestorer
    {
        public SnapshotRestoreRequest? Request { get; private set; }

        public Task<SnapshotRestoreResult> RestoreAsync(
            SnapshotRestoreRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            if (throwOnRestore)
            {
                throw new IOException("模拟恢复失败。");
            }

            return Task.FromResult(new SnapshotRestoreResult(Guid.NewGuid(), 1));
        }
    }
}
