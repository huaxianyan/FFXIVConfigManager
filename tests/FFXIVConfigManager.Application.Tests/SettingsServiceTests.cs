using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task AddAndRemoveProfile_PersistsProfileAndRemovesItsAliases()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);
        var profile = await service.AddProfileAsync("国服", GameRegion.China, ".");
        await service.SetCharacterAliasAsync(
            profile.Id,
            CharacterFolderName.Create("FFXIV_CHR0000000000000001"),
            "测试角色");

        var saved = await service.GetAsync();
        Assert.Single(saved.CustomProfiles);
        Assert.Single(saved.CharacterAliases);

        await service.RemoveProfileAsync(profile.Id);
        saved = await service.GetAsync();

        Assert.Empty(saved.CustomProfiles);
        Assert.Empty(saved.CharacterAliases);
    }

    [Fact]
    public async Task RestoreBackup_ReplacesOnlySelectedScopeAndPreservesLibraryPath()
    {
        var currentProfile = new GameProfile(Guid.NewGuid(), "当前", GameRegion.Custom, "/current");
        var backupProfile = new GameProfile(Guid.NewGuid(), "备份", GameRegion.Custom, "/backup");
        var currentAlias = new CharacterAliasSetting(
            currentProfile.Id,
            "FFXIV_CHR0000000000000001",
            "当前标记");
        var backupAlias = new CharacterAliasSetting(
            backupProfile.Id,
            "FFXIV_CHR0000000000000002",
            "备份标记");
        var store = new MemorySettingsStore(new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [currentProfile],
            [currentAlias])
        {
            SnapshotLibraryPath = "/local/backups",
        });
        var service = new SettingsService(store);
        var backup = new SettingsBackupDocument(
            SettingsBackupDocument.CurrentFormatVersion,
            DateTimeOffset.UtcNow,
            SettingsBackupScope.All,
            [backupProfile],
            [backupAlias]);

        await service.RestoreBackupAsync(backup, SettingsBackupScope.CustomProfiles);

        var restored = await service.GetAsync();
        Assert.Equal([backupProfile], restored.CustomProfiles);
        Assert.Equal([currentAlias], restored.CharacterAliases);
        Assert.Equal("/local/backups", restored.SnapshotLibraryPath);
    }

    [Fact]
    public async Task RestoreBackup_RejectsScopeNotIncludedInBackup()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);
        var backup = new SettingsBackupDocument(
            SettingsBackupDocument.CurrentFormatVersion,
            DateTimeOffset.UtcNow,
            SettingsBackupScope.CharacterAliases,
            [],
            []);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.RestoreBackupAsync(backup, SettingsBackupScope.CustomProfiles));
        Assert.Same(ApplicationSettings.Empty, await service.GetAsync());
    }

    [Fact]
    public async Task SetShowOnlyTaggedCharacters_PersistsFilterState()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);

        await service.SetShowOnlyTaggedCharactersAsync(true);

        Assert.True((await service.GetAsync()).ShowOnlyTaggedCharacters);
    }

    [Fact]
    public async Task SetSnapshotLibraryPath_PersistsNormalizedPath()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);

        await service.SetSnapshotLibraryPathAsync(".");

        Assert.Equal(Path.GetFullPath("."), (await service.GetAsync()).SnapshotLibraryPath);
    }

    [Fact]
    public async Task SetCharacterAlias_BlankAliasRemovesExistingAlias()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);
        var folder = CharacterFolderName.Create("FFXIV_CHR0000000000000001");
        var profileId = Guid.NewGuid();

        await service.SetCharacterAliasAsync(profileId, folder, "角色");
        await service.SetCharacterAliasAsync(profileId, folder, "  ");

        Assert.Empty((await service.GetAsync()).CharacterAliases);
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public MemorySettingsStore(ApplicationSettings? settings = null)
        {
            _settings = settings ?? ApplicationSettings.Empty;
        }

        private ApplicationSettings _settings;

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_settings);

        public Task SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            _settings = settings;
            return Task.CompletedTask;
        }
    }
}
