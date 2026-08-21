using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Infrastructure.Settings;

namespace FFXIVConfigManager.IntegrationTests;

public sealed class JsonSettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsSettingsAndLeavesNoTemporaryFiles()
    {
        var path = Path.Combine(_root, "settings.json");
        var profile = new GameProfile(
            Guid.NewGuid(),
            "国服",
            GameRegion.China,
            Path.Combine(_root, "config"));
        var expected = new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [profile],
            [new CharacterAliasSetting(profile.Id, "FFXIV_CHR0000000000000001", "角色")])
        {
            ShowOnlyTaggedCharacters = true,
            IsUpdateProxyEnabled = true,
            UpdateProxyAddress = "socks5://127.0.0.1:7890/",
        };
        var store = new JsonSettingsStore(path);

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.CustomProfiles, actual.CustomProfiles);
        Assert.Equal(expected.CharacterAliases, actual.CharacterAliases);
        Assert.True(actual.ShowOnlyTaggedCharacters);
        Assert.True(actual.IsUpdateProxyEnabled);
        Assert.Equal(expected.UpdateProxyAddress, actual.UpdateProxyAddress);
        Assert.Single(Directory.GetFiles(_root));
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"China\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_MissingFileReturnsEmptySettings()
    {
        var store = new JsonSettingsStore(Path.Combine(_root, "settings.json"));

        var settings = await store.LoadAsync();

        Assert.Equal(ApplicationSettings.Empty, settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
