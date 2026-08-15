using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace FFXIVConfigManager.Desktop.Services;

public sealed class AvaloniaFolderPickerService(
    Func<Window?> ownerAccessor) : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerAccessor();
        if (owner?.StorageProvider is null)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });
        cancellationToken.ThrowIfCancellationRequested();

        return folders.Count == 0 ? null : folders[0].Path.LocalPath;
    }

}
