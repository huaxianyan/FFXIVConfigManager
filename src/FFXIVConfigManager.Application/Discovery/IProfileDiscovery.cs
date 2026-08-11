using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Discovery;

public interface IProfileDiscovery
{
    Task<IReadOnlyList<GameProfile>> DiscoverAsync(CancellationToken cancellationToken = default);
}
