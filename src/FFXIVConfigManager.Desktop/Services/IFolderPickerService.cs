namespace FFXIVConfigManager.Desktop.Services;

public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(
        string title,
        CancellationToken cancellationToken = default);

    Task<string?> PickOpenFileAsync(
        string title,
        string extension,
        CancellationToken cancellationToken = default);

    Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        string extension,
        CancellationToken cancellationToken = default);
}
