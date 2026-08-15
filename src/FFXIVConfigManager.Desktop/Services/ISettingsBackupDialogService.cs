namespace FFXIVConfigManager.Desktop.Services;

public interface ISettingsBackupDialogService
{
    Task<bool> ShowBackupAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default);

    Task<bool> ShowRestoreAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default);
}
