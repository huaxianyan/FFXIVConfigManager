using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Infrastructure.Discovery;

public sealed class NoDefaultProfileDiscovery : IProfileDiscovery
{
    public Task<IReadOnlyList<GameProfile>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GameProfile>>([]);
    }
}
