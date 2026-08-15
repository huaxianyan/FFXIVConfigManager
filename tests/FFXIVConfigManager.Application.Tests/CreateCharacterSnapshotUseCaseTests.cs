using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Files;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Tests;

public sealed class CreateCharacterSnapshotUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ExcludesPrivateAndCacheFilesFromDefaultSnapshot()
    {
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, "/config");
        var character = new CharacterConfiguration(
            profile.Id,
            CharacterFolderName.Create("FFXIV_CHR0000000000000001"),
            "/config/FFXIV_CHR0000000000000001",
            DateTimeOffset.UtcNow,
            [
                CreateFile("ADDON.DAT"),
                CreateFile("ACQ.DAT"),
                CreateFile("ITEMFDR.DAT"),
            ]);
        var archive = new CapturingArchiveService();
        var useCase = new CreateCharacterSnapshotUseCase(archive, TimeProvider.System);

        await useCase.ExecuteAsync(profile, character, "/library");

        var source = Assert.Single(archive.Request!.Files);
        Assert.Equal("ADDON.DAT", source.OriginalFileName);
    }

    [Fact]
    public async Task ExecuteAsync_WritesTrimmedCharacterAliasToSnapshotSource()
    {
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, "/config");
        var character = new CharacterConfiguration(
            profile.Id,
            CharacterFolderName.Create("FFXIV_CHR0000000000000001"),
            "/config/FFXIV_CHR0000000000000001",
            DateTimeOffset.UtcNow,
            [CreateFile("ADDON.DAT")]);
        var archive = new CapturingArchiveService();
        var useCase = new CreateCharacterSnapshotUseCase(archive, TimeProvider.System);

        await useCase.ExecuteAsync(
            profile,
            character,
            "/library",
            characterAlias: "  测试角色  ");

        Assert.Equal("测试角色", archive.Request!.Source.CharacterAlias);
    }

    [Fact]
    public async Task ExecuteMigrationSourceAsync_AllKnownModeIncludesPrivateCacheAndUiSave()
    {
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, "/config");
        var character = new CharacterConfiguration(
            profile.Id,
            CharacterFolderName.Create("FFXIV_CHR0000000000000001"),
            "/config/FFXIV_CHR0000000000000001",
            DateTimeOffset.UtcNow,
            [
                CreateFile("UISAVE.DAT"),
                CreateFile("ACQ.DAT"),
                CreateFile("ITEMFDR.DAT"),
            ]);
        var archive = new CapturingArchiveService();
        var useCase = new CreateCharacterSnapshotUseCase(archive, TimeProvider.System);

        await useCase.ExecuteMigrationSourceAsync(
            profile,
            character,
            "/library",
            ConfigScope.AllKnownFiles);

        Assert.Equal(3, archive.Request!.Files.Count);
        Assert.Contains(archive.Request.Files, file => file.OriginalFileName == "UISAVE.DAT");
        Assert.Contains(archive.Request.Files, file => file.OriginalFileName == "ACQ.DAT");
        Assert.Contains(archive.Request.Files, file => file.OriginalFileName == "ITEMFDR.DAT");
    }

    private static CharacterConfigFile CreateFile(string name)
    {
        Assert.True(ConfigFileCatalog.TryGet(name, out var definition));
        return new CharacterConfigFile(definition, 1, DateTimeOffset.UtcNow);
    }

    private sealed class CapturingArchiveService : ISnapshotArchiveService
    {
        public SnapshotArchiveRequest? Request { get; private set; }

        public Task<CreatedSnapshot> CreateAsync(
            SnapshotArchiveRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            var manifest = new SnapshotManifest(
                1,
                request.SnapshotId,
                request.CreatedAtUtc,
                request.Reason,
                request.Source,
                []);
            return Task.FromResult(new CreatedSnapshot("snapshot.zip", manifest));
        }

        public Task<SnapshotVerificationResult> VerifyAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
