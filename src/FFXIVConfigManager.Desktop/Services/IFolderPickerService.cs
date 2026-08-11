namespace FFXIVConfigManager.Desktop.Services;

public interface IFolderPickerService
{
    Task<string?> PickConfigRootAsync(CancellationToken cancellationToken = default);
}
