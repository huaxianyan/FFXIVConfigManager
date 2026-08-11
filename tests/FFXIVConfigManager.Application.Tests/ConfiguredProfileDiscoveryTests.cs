using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Tests;

public sealed class ConfiguredProfileDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_MergesAutomaticAndCustomProfiles()
    {
        var automatic = new GameProfile(
            Guid.NewGuid(),
            "国际服",
            GameRegion.International,
            Path.Combine(Path.GetTempPath(), "global"),
            GameProfileOrigin.Automatic);
        var custom = new GameProfile(
            Guid.NewGuid(),
            "国服",
            GameRegion.China,
            Path.Combine(Path.GetTempPath(), "china"));
        var settings = new ApplicationSettings(1, [custom], []);
        var discovery = new ConfiguredProfileDiscovery(
            new StubDiscovery(automatic),
            new StubStore(settings));

        var profiles = await discovery.DiscoverAsync();

        Assert.Equal([automatic, custom], profiles);
    }

    private sealed class StubDiscovery(GameProfile profile) : IProfileDiscovery
    {
        public Task<IReadOnlyList<GameProfile>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameProfile>>([profile]);
    }

    private sealed class StubStore(ApplicationSettings settings) : ISettingsStore
    {
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(settings);

        public Task SaveAsync(
            ApplicationSettings updatedSettings,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
