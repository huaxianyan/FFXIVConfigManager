using FFXIVConfigManager.Application.Settings;

namespace FFXIVConfigManager.Infrastructure.Settings;

public sealed class JsonSettingsTransferService(ISettingsStore settingsStore) : ISettingsTransferService
{
    public async Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        await new JsonSettingsStore(Path.GetFullPath(destinationPath))
            .SaveAsync(settings, cancellationToken);
    }

    public Task<ApplicationSettings> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        new JsonSettingsStore(Path.GetFullPath(sourcePath)).LoadAsync(cancellationToken);
}
