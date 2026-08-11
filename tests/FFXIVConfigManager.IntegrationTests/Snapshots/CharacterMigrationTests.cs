using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Files;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Infrastructure.Snapshots;

namespace FFXIVConfigManager.IntegrationTests.Snapshots;

public sealed class CharacterMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-migration-{Guid.NewGuid():N}");

    [Fact]
    public async Task ExecuteAsync_MigratesSafeScopesAndPreservesExcludedPrivateFile()
    {
        var sourcePath = CreateCharacterDirectory(
            "source",
            ("ADDON.DAT", "source-hud"),
            ("HOTBAR.DAT", "source-hotbar"),
            ("ACQ.DAT", "source-private"));
        var targetPath = CreateCharacterDirectory(
            "target",
            ("ADDON.DAT", "target-hud"),
            ("HOTBAR.DAT", "target-hotbar"),
            ("ACQ.DAT", "target-private"));
        var sourceProfile = new GameProfile(Guid.NewGuid(), "国际服", GameRegion.International, _root);
        var targetProfile = new GameProfile(Guid.NewGuid(), "国服", GameRegion.China, _root);
        var source = CreateCharacter(sourceProfile, sourcePath, "FFXIV_CHR0000000000000001");
        var target = CreateCharacter(targetProfile, targetPath, "FFXIV_CHR0000000000000002");
        var archiveService = new ZipSnapshotArchiveService();
        var createSnapshot = new CreateCharacterSnapshotUseCase(archiveService, TimeProvider.System);
        var useCase = new MigrateCharacterConfigurationUseCase(
            createSnapshot,
            new TransactionalSnapshotRestorer());

        var result = await useCase.ExecuteAsync(
            sourceProfile,
            source,
            targetProfile,
            target,
            Path.Combine(_root, "library"));

        Assert.Equal("source-hud", await File.ReadAllTextAsync(Path.Combine(targetPath, "ADDON.DAT")));
        Assert.Equal("source-hotbar", await File.ReadAllTextAsync(Path.Combine(targetPath, "HOTBAR.DAT")));
        Assert.Equal("target-private", await File.ReadAllTextAsync(Path.Combine(targetPath, "ACQ.DAT")));
        Assert.Equal(2, result.RestoreResult.RestoredFileCount);
        Assert.Equal("BeforeMigration", result.TargetRecoveryPoint.Manifest.Reason.ToString());
        Assert.Equal("MigrationSource", result.SourceSnapshot.Manifest.Reason.ToString());
    }

    private string CreateCharacterDirectory(
        string name,
        params (string FileName, string Content)[] files)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        foreach (var (fileName, content) in files)
        {
            File.WriteAllText(Path.Combine(path, fileName), content);
        }

        return path;
    }

    private static CharacterConfiguration CreateCharacter(
        GameProfile profile,
        string path,
        string folderName)
    {
        var files = Directory.EnumerateFiles(path)
            .Select(file =>
            {
                Assert.True(ConfigFileCatalog.TryGet(Path.GetFileName(file), out var definition));
                var info = new FileInfo(file);
                return new CharacterConfigFile(
                    definition,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero));
            })
            .ToArray();
        return new CharacterConfiguration(
            profile.Id,
            CharacterFolderName.Create(folderName),
            path,
            DateTimeOffset.UtcNow,
            files);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
