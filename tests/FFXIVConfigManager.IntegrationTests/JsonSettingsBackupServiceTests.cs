using System.Text.Json;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Infrastructure.Settings;

namespace FFXIVConfigManager.IntegrationTests;

public sealed class JsonSettingsBackupServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-settings-backup-{Guid.NewGuid():N}");

    [Fact]
    public async Task BackupAsync_OverwritesSingleBackupAndReportsStatus()
    {
        var store = new JsonSettingsStore(Path.Combine(_root, "app", "settings.json"));
        var settings = new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [],
            [new CharacterAliasSetting(Guid.NewGuid(), "FFXIV_CHR0000000000000001", "角色一")]);
        await store.SaveAsync(settings);
        var service = new JsonSettingsBackupService(
            store,
            new SettingsService(store),
            TimeProvider.System);

        var first = await service.BackupAsync(_root, SettingsBackupScope.CharacterAliases);
        await store.SaveAsync(settings with
        {
            CharacterAliases = [new CharacterAliasSetting(Guid.NewGuid(), "FFXIV_CHR0000000000000002", "角色二")],
        });
        var second = await service.BackupAsync(_root, SettingsBackupScope.CharacterAliases);

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(first.BackupPath, second.BackupPath);
        Assert.Single(Directory.EnumerateFiles(
            Path.Combine(_root, "settings"),
            "*.json",
            SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(_root, "settings"),
            "*.tmp",
            SearchOption.TopDirectoryOnly));

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(second.BackupPath));
        Assert.Empty(document.RootElement.GetProperty("customProfiles").EnumerateArray());
        Assert.Single(document.RootElement.GetProperty("characterAliases").EnumerateArray());
    }

    [Fact]
    public async Task RestoreAsync_ReplacesOnlySelectedScopeAndPreservesLibraryPath()
    {
        var store = new JsonSettingsStore(Path.Combine(_root, "app", "settings.json"));
        var service = new SettingsService(store);
        var profile = new GameProfile(Guid.NewGuid(), "备份配置源", GameRegion.Custom, _root);
        await store.SaveAsync(new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [profile],
            [new CharacterAliasSetting(profile.Id, "FFXIV_CHR0000000000000001", "备份标记")])
        {
            SnapshotLibraryPath = _root,
        });
        var backupService = new JsonSettingsBackupService(store, service, TimeProvider.System);
        await backupService.BackupAsync(_root, SettingsBackupScope.All);
        await store.SaveAsync(new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [],
            [new CharacterAliasSetting(Guid.NewGuid(), "FFXIV_CHR0000000000000002", "当前标记")])
        {
            SnapshotLibraryPath = _root,
        });

        await backupService.RestoreAsync(_root, SettingsBackupScope.CustomProfiles);

        var restored = await store.LoadAsync();
        Assert.Equal([profile], restored.CustomProfiles);
        Assert.Single(restored.CharacterAliases);
        Assert.Equal("当前标记", restored.CharacterAliases[0].Alias);
        Assert.Equal(_root, restored.SnapshotLibraryPath);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("""
        {
          "formatVersion": 1,
          "createdAtUtc": "2025-01-01T00:00:00+00:00",
          "includedScopes": "CharacterAliases",
          "customProfiles": [],
          "characterAliases": [
            {
              "profileId": "00000000-0000-0000-0000-000000000000",
              "characterFolder": "invalid",
              "alias": ""
            }
          ]
        }
        """)]
    [InlineData("""
        {
          "formatVersion": 1,
          "createdAtUtc": "2025-01-01T00:00:00+00:00",
          "includedScopes": "None",
          "customProfiles": [],
          "characterAliases": []
        }
        """)]
    public async Task GetStatusAsync_CorruptedBackupReportsInvalid(string contents)
    {
        var store = new JsonSettingsStore(Path.Combine(_root, "app", "settings.json"));
        var service = new JsonSettingsBackupService(
            store,
            new SettingsService(store),
            TimeProvider.System);
        var settingsDirectory = Path.Combine(_root, "settings");
        Directory.CreateDirectory(settingsDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(settingsDirectory, "settings.ffxivconfig-settings.json"),
            contents);

        var status = await service.GetStatusAsync(_root);

        Assert.True(status.Exists);
        Assert.False(status.IsValid);
        Assert.NotEmpty(status.Errors);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
