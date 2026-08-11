using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Discovery;

public sealed class ConfiguredProfileDiscovery(
    IProfileDiscovery automaticDiscovery,
    ISettingsStore settingsStore) : IProfileDiscovery
{
    public async Task<IReadOnlyList<GameProfile>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var automaticProfiles = await automaticDiscovery.DiscoverAsync(cancellationToken);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var profiles = new List<GameProfile>(automaticProfiles.Count + settings.CustomProfiles.Count);
        var knownIds = new HashSet<Guid>();
        var knownRoots = new HashSet<string>(StringComparer.Ordinal);

        foreach (var profile in automaticProfiles.Concat(settings.CustomProfiles))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.ConfigRoot));

            if (knownIds.Add(profile.Id) && knownRoots.Add(normalizedRoot))
            {
                profiles.Add(profile);
            }
        }

        return profiles;
    }
}
