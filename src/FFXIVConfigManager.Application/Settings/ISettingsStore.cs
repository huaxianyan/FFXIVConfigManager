namespace FFXIVConfigManager.Application.Settings;

public interface ISettingsStore
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default);
}
