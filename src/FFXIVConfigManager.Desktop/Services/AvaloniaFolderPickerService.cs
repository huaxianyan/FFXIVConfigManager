using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FFXIVConfigManager.Desktop.Localization;

namespace FFXIVConfigManager.Desktop.Services;

public sealed class AvaloniaFolderPickerService(
    Func<Window?> ownerAccessor,
    ITextLocalizer text) : IFolderPickerService
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

    public async Task<string?> PickOpenFileAsync(
        string title,
        string extension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerAccessor();
        if (owner?.StorageProvider is null)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [CreateFileType(extension)],
            });
        cancellationToken.ThrowIfCancellationRequested();
        return files.Count == 0 ? null : files[0].Path.LocalPath;
    }

    public async Task<string?> PickSaveFileAsync(
        string title,
        string suggestedFileName,
        string extension,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerAccessor();
        if (owner?.StorageProvider is null)
        {
            return null;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedFileName,
                DefaultExtension = extension.TrimStart('.'),
                FileTypeChoices = [CreateFileType(extension)],
            });
        cancellationToken.ThrowIfCancellationRequested();
        return file?.Path.LocalPath;
    }

    private FilePickerFileType CreateFileType(string extension) =>
        new(text["SettingsBackupFileType"])
        {
            Patterns = [$"*{extension}"],
        };
}
