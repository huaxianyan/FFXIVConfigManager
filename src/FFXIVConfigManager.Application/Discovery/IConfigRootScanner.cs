using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Discovery;

public sealed record ConfigRootScanResult(
    GameProfile Profile,
    bool RootExists,
    IReadOnlyList<CharacterConfiguration> Characters,
    string? Issue = null);

public interface IConfigRootScanner
{
    Task<ConfigRootScanResult> ScanAsync(
        GameProfile profile,
        CancellationToken cancellationToken = default);
}
