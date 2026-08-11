using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Platform.Windows.Discovery;

public sealed class WindowsDefaultProfileDiscovery : IProfileDiscovery
{
    private static readonly Guid InternationalProfileId =
        Guid.Parse("CC8B879F-56B3-44AB-81E1-47EC279AFD62");

    public Task<IReadOnlyList<GameProfile>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var configRoot = Path.Combine(documents, "My Games", "FINAL FANTASY XIV - A Realm Reborn");

        IReadOnlyList<GameProfile> profiles =
        [
            new GameProfile(
                InternationalProfileId,
                "国际服",
                GameRegion.International,
                configRoot),
        ];

        return Task.FromResult(profiles);
    }
}
