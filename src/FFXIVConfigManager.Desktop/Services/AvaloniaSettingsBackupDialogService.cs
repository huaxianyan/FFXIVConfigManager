using Avalonia.Controls;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.ViewModels;
using FFXIVConfigManager.Desktop.Views;

namespace FFXIVConfigManager.Desktop.Services;

public sealed class AvaloniaSettingsBackupDialogService(
    Func<Window?> ownerAccessor,
    ISettingsBackupService backupService,
    ITextLocalizer text) : ISettingsBackupDialogService
{
    public Task<bool> ShowBackupAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default) =>
        ShowAsync(libraryRoot, isRestore: false, cancellationToken);

    public Task<bool> ShowRestoreAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default) =>
        ShowAsync(libraryRoot, isRestore: true, cancellationToken);

    private async Task<bool> ShowAsync(
        string libraryRoot,
        bool isRestore,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerAccessor();
        if (owner is null)
        {
            return false;
        }

        var availableScopes = SettingsBackupScope.All;
        if (isRestore)
        {
            var status = await backupService.GetStatusAsync(libraryRoot, cancellationToken);
            if (!status.IsValid)
            {
                return false;
            }

            availableScopes = status.IncludedScopes;
        }

        var viewModel = new SettingsBackupScopeViewModel(
            libraryRoot,
            isRestore,
            availableScopes,
            backupService,
            text);
        var window = new SettingsBackupScopeWindow
        {
            DataContext = viewModel,
        };
        await window.ShowDialog(owner);
        return viewModel.Completed;
    }
}
