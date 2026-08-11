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

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private ApplicationSettings _settings = ApplicationSettings.Empty;

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
