using Avalonia.Controls;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.ViewModels;
using FFXIVConfigManager.Desktop.Views;

namespace FFXIVConfigManager.Desktop.Services;

public sealed class AvaloniaPortraitBackupEditDialogService(
    Func<Window?> ownerAccessor,
    ITextLocalizer text) : IPortraitBackupEditDialogService
{
    public async Task<PortraitBackupEditResult?> ShowAsync(
        string schemeName,
        string note,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerAccessor();
        if (owner is null)
        {
            return null;
        }

        var viewModel = new PortraitBackupEditViewModel(schemeName, note, text);
        var window = new PortraitBackupEditWindow
        {
            DataContext = viewModel,
        };
        viewModel.CloseRequested += result => window.Close(result);
        return await window.ShowDialog<PortraitBackupEditResult?>(owner);
    }
}
