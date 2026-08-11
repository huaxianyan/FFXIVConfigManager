namespace FFXIVConfigManager.Application.Discovery;

public sealed class ScanProfilesUseCase(
    IProfileDiscovery profileDiscovery,
    IConfigRootScanner configRootScanner)
{
    public async Task<IReadOnlyList<ConfigRootScanResult>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var profiles = await profileDiscovery.DiscoverAsync(cancellationToken);
        var results = new List<ConfigRootScanResult>(profiles.Count);

        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await configRootScanner.ScanAsync(profile, cancellationToken);
            var sortedCharacters = result.Characters
                .OrderByDescending(character => character.LastModifiedUtc)
                .ThenBy(character => character.FolderName.Value, StringComparer.Ordinal)
                .ToArray();

            results.Add(result with { Characters = sortedCharacters });
        }

        return results;
    }
}
