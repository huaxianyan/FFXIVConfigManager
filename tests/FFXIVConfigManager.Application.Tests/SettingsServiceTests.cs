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

    [Fact]
    public async Task ImportPortable_MergesAliasesByCharacterFolderAndPreservesLocalPaths()
    {
        var localProfile = new GameProfile(Guid.NewGuid(), "本机", GameRegion.Custom, "/local");
        var localFolder = "FFXIV_CHR0000000000000001";
        var untouchedFolder = "FFXIV_CHR0000000000000002";
        var store = new MemorySettingsStore(new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [localProfile],
            [
                new CharacterAliasSetting(localProfile.Id, localFolder, "旧标记"),
                new CharacterAliasSetting(localProfile.Id, untouchedFolder, "保留标记"),
            ])
        {
            SnapshotLibraryPath = "/local/backups",
        });
        var service = new SettingsService(store);
        var imported = new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [new GameProfile(Guid.NewGuid(), "另一设备", GameRegion.Custom, "/other")],
            [new CharacterAliasSetting(Guid.NewGuid(), localFolder, "导入标记")])
        {
            SnapshotLibraryPath = "/other/backups",
        };

        await service.ImportPortableAsync(imported);

        var saved = await service.GetAsync();
        Assert.Equal("/local/backups", saved.SnapshotLibraryPath);
        Assert.Equal([localProfile], saved.CustomProfiles);
        Assert.Equal(2, saved.CharacterAliases.Count);
        Assert.Contains(saved.CharacterAliases, alias =>
            alias.CharacterFolder == localFolder && alias.Alias == "导入标记");
        Assert.Contains(saved.CharacterAliases, alias =>
            alias.CharacterFolder == untouchedFolder && alias.Alias == "保留标记");
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
