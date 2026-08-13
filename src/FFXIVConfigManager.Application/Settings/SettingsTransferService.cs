namespace FFXIVConfigManager.Application.Settings;

public interface ISettingsTransferService
{
    Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken = default);

    Task<ApplicationSettings> ImportAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);
}
