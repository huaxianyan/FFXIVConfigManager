using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Settings;

public sealed class SettingsService(ISettingsStore settingsStore)
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken = default) =>
        settingsStore.LoadAsync(cancellationToken);

    public async Task<GameProfile> AddProfileAsync(
        string name,
        GameRegion region,
        string configRoot,
        CancellationToken cancellationToken = default)
    {
        var profile = new GameProfile(
            Guid.NewGuid(),
            name,
            region,
            Path.GetFullPath(configRoot),
            GameProfileOrigin.User);

        await UpdateAsync(
            settings => settings with
            {
                CustomProfiles = [.. settings.CustomProfiles, profile],
            },
            cancellationToken);

        return profile;
    }

    public Task ImportPortableAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            current => current with
            {
                CharacterAliases = settings.CharacterAliases,
                SnapshotLibraryPath = current.SnapshotLibraryPath,
            },
            cancellationToken);

    public Task SetSnapshotLibraryPathAsync(
        string libraryPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(libraryPath))
        {
            throw new ArgumentException("备份库目录不能为空。", nameof(libraryPath));
        }

        var normalizedPath = Path.GetFullPath(libraryPath);
        return UpdateAsync(
            settings => settings with { SnapshotLibraryPath = normalizedPath },
            cancellationToken);
    }

    public Task RemoveProfileAsync(
        Guid profileId,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            settings => settings with
            {
                CustomProfiles = settings.CustomProfiles
                    .Where(profile => profile.Id != profileId)
                    .ToArray(),
                CharacterAliases = settings.CharacterAliases
                    .Where(alias => alias.ProfileId != profileId)
                    .ToArray(),
            },
            cancellationToken);

    public Task SetCharacterAliasAsync(
        Guid profileId,
        CharacterFolderName characterFolder,
        string? alias,
        CancellationToken cancellationToken = default) =>
        UpdateAsync(
            settings =>
            {
                var aliases = settings.CharacterAliases
                    .Where(item => item.ProfileId != profileId ||
                                   !string.Equals(
                                       item.CharacterFolder,
                                       characterFolder.Value,
                                       StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var normalizedAlias = alias?.Trim();

                if (!string.IsNullOrEmpty(normalizedAlias))
                {
                    aliases.Add(new CharacterAliasSetting(
                        profileId,
                        characterFolder.Value,
                        normalizedAlias));
                }

                return settings with { CharacterAliases = aliases };
            },
            cancellationToken);

    private async Task UpdateAsync(
        Func<ApplicationSettings, ApplicationSettings> update,
        CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var current = await settingsStore.LoadAsync(cancellationToken);
            var updated = update(current) with
            {
                SchemaVersion = ApplicationSettings.CurrentSchemaVersion,
            };
            await settingsStore.SaveAsync(updated, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
