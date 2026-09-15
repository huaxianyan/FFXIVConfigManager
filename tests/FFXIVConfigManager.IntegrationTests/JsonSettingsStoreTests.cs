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
            AutomaticBackups = new AutomaticBackupPolicy
            {
                BeforeCharacterRestore = new BackupRetentionSetting(false, 3),
                BeforeCharacterMigration = new BackupRetentionSetting(true, 7),
                BeforeAppearanceRestore = new BackupRetentionSetting(false, 5),
                BeforePortraitTransfer = new BackupRetentionSetting(true, 9),
            },
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
        Assert.False(actual.AutomaticBackups.BeforeCharacterRestore.Enabled);
        Assert.Equal(3, actual.AutomaticBackups.BeforeCharacterRestore.EffectiveRetentionCount);
        Assert.True(actual.AutomaticBackups.BeforeCharacterMigration.Enabled);
        Assert.Equal(7, actual.AutomaticBackups.BeforeCharacterMigration.EffectiveRetentionCount);
        Assert.False(actual.AutomaticBackups.BeforeAppearanceRestore.Enabled);
        Assert.True(actual.AutomaticBackups.BeforePortraitTransfer.Enabled);
        Assert.Equal(9, actual.AutomaticBackups.BeforePortraitTransfer.EffectiveRetentionCount);
        Assert.Single(Directory.GetFiles(_root));
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"China\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_SettingsFileWithoutAutomaticBackupsEnablesEveryRecoveryPoint()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(
            path,
            """
            {
              "schemaVersion": 1,
              "customProfiles": [],
              "characterAliases": [],
              "showOnlyTaggedCharacters": true
            }
            """);
        var store = new JsonSettingsStore(path);

        var settings = await store.LoadAsync();

        Assert.True(settings.ShowOnlyTaggedCharacters);
        Assert.True(settings.AutomaticBackups.BeforeCharacterRestore.Enabled);
        Assert.True(settings.AutomaticBackups.BeforeCharacterMigration.Enabled);
        Assert.True(settings.AutomaticBackups.BeforeAppearanceRestore.Enabled);
        Assert.True(settings.AutomaticBackups.BeforePortraitTransfer.Enabled);
        Assert.Equal(
            BackupRetentionSetting.DefaultRetentionCount,
            settings.AutomaticBackups.BeforeCharacterRestore.EffectiveRetentionCount);
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
