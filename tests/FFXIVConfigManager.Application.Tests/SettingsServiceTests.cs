using FFXIVConfigManager.Application.Backups;
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
            IsUpdateProxyEnabled = true,
            UpdateProxyAddress = "socks5://127.0.0.1:7890/",
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
        Assert.True(restored.IsUpdateProxyEnabled);
        Assert.Equal("socks5://127.0.0.1:7890/", restored.UpdateProxyAddress);
    }

    [Fact]
    public async Task UpdateProxy_DisablingPreservesSavedEndpointForNextEnable()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);

        var address = await service.SetUpdateProxyEndpointAsync(
            "socks5",
            "127.0.0.1",
            7890);
        await service.SetUpdateProxyEnabledAsync(true);
        await service.SetUpdateProxyEnabledAsync(false);

        var settings = await service.GetAsync();
        Assert.False(settings.IsUpdateProxyEnabled);
        Assert.Equal("socks5://127.0.0.1:7890/", address);
        Assert.Equal(address, settings.UpdateProxyAddress);
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

    [Fact]
    public async Task SetAutomaticBackupPolicy_PersistsEveryFlag()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);
        var policy = new AutomaticBackupPolicy
        {
            BeforeCharacterRestore = new BackupRetentionSetting(false, 3),
            BeforeCharacterMigration = new BackupRetentionSetting(true, 7),
            BeforeAppearanceRestore = new BackupRetentionSetting(false, 5),
            BeforePortraitTransfer = new BackupRetentionSetting(true, 9),
        };

        await service.SetAutomaticBackupPolicyAsync(policy);

        var saved = await service.GetAsync();
        Assert.Equal(policy, saved.AutomaticBackups);
        Assert.False(saved.AutomaticBackups.BeforeCharacterRestore.Enabled);
        Assert.Equal(3, saved.AutomaticBackups.BeforeCharacterRestore.EffectiveRetentionCount);
        Assert.False(saved.AutomaticBackups.BeforeAppearanceRestore.Enabled);
        Assert.Equal(9, saved.AutomaticBackups.BeforePortraitTransfer.EffectiveRetentionCount);
    }

    [Fact]
    public async Task SetAutomaticBackupRetention_UpdatesOnlySelectedRule()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);

        await service.SetAutomaticBackupRetentionAsync(BackupRule.CharacterRestore, 12);
        await service.SetAutomaticBackupRetentionAsync(BackupRule.Portrait, 2);

        var saved = await service.GetAsync();
        Assert.Equal(12, saved.AutomaticBackups.BeforeCharacterRestore.RetentionCount);
        Assert.Equal(
            BackupRetentionSetting.DefaultRetentionCount,
            saved.AutomaticBackups.BeforeCharacterMigration.RetentionCount);
        Assert.Equal(
            BackupRetentionSetting.DefaultRetentionCount,
            saved.AutomaticBackups.BeforeAppearanceRestore.RetentionCount);
        Assert.Equal(2, saved.AutomaticBackups.BeforePortraitTransfer.RetentionCount);
    }

    [Fact]
    public async Task SetAutomaticBackupRetention_OutOfRangeThrows()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetAutomaticBackupRetentionAsync(BackupRule.CharacterRestore, 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetAutomaticBackupRetentionAsync(
                BackupRule.CharacterRestore,
                BackupRetentionSetting.MaximumRetentionCount + 1));
    }

    [Fact]
    public async Task RestoreBackup_PreservesAutomaticBackupPolicy()
    {
        var disabled = new AutomaticBackupPolicy
        {
            BeforeCharacterRestore = new BackupRetentionSetting(false, 2),
            BeforeCharacterMigration = new BackupRetentionSetting(false, 2),
            BeforeAppearanceRestore = new BackupRetentionSetting(false, 2),
            BeforePortraitTransfer = new BackupRetentionSetting(false, 2),
        };
        var manualCleanup = new BackupRetentionSetting(true, 7);
        var store = new MemorySettingsStore(new ApplicationSettings(
            ApplicationSettings.CurrentSchemaVersion,
            [],
            [])
        {
            AutomaticBackups = disabled,
            ManualCharacterBackupCleanup = manualCleanup,
        });
        var service = new SettingsService(store);
        var backup = new SettingsBackupDocument(
            SettingsBackupDocument.CurrentFormatVersion,
            DateTimeOffset.UtcNow,
            SettingsBackupScope.CustomProfiles,
            [],
            []);

        await service.RestoreBackupAsync(backup, SettingsBackupScope.CustomProfiles);

        var restored = await service.GetAsync();
        Assert.Equal(disabled, restored.AutomaticBackups);
        Assert.Equal(manualCleanup, restored.ManualCharacterBackupCleanup);
    }

    [Fact]
    public void EmptySettings_EnableEveryAutomaticBackup()
    {
        var policy = ApplicationSettings.Empty.AutomaticBackups;

        Assert.True(policy.BeforeCharacterRestore.Enabled);
        Assert.True(policy.BeforeCharacterMigration.Enabled);
        Assert.True(policy.BeforeAppearanceRestore.Enabled);
        Assert.True(policy.BeforePortraitTransfer.Enabled);
        Assert.Equal(
            BackupRetentionSetting.DefaultRetentionCount,
            policy.BeforeCharacterRestore.EffectiveRetentionCount);
    }

    [Fact]
    public void EmptySettings_DisableManualBackupCleanup()
    {
        var setting = ApplicationSettings.Empty.ManualCharacterBackupCleanup;

        Assert.False(setting.Enabled);
        Assert.Equal(
            BackupRetentionSetting.DefaultManualRetentionCount,
            setting.EffectiveRetentionCount);
    }

    [Fact]
    public async Task SetManualCharacterBackupCleanup_KeepsRetentionWhenDisabled()
    {
        var store = new MemorySettingsStore();
        var service = new SettingsService(store);

        await service.SetManualCharacterBackupCleanupAsync(new BackupRetentionSetting(true, 12));
        var enabled = await service.GetAsync();
        Assert.True(enabled.ManualCharacterBackupCleanup.Enabled);
        Assert.Equal(12, enabled.ManualCharacterBackupCleanup.EffectiveRetentionCount);

        await service.SetManualCharacterBackupCleanupAsync(new BackupRetentionSetting(false, 12));
        var disabled = await service.GetAsync();
        Assert.False(disabled.ManualCharacterBackupCleanup.Enabled);
        Assert.Equal(12, disabled.ManualCharacterBackupCleanup.RetentionCount);
    }

    [Fact]
    public async Task SetManualCharacterBackupCleanup_OutOfRangeThrows()
    {
        var service = new SettingsService(new MemorySettingsStore());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetManualCharacterBackupCleanupAsync(
                new BackupRetentionSetting(true, 0)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.SetManualCharacterBackupCleanupAsync(
                new BackupRetentionSetting(
                    true,
                    BackupRetentionSetting.MaximumRetentionCount + 1)));
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
