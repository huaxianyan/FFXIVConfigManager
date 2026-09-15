using Avalonia.Controls;
using FFXIVConfigManager.Application.Backups;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.ViewModels;
using FFXIVConfigManager.Desktop.Views;

namespace FFXIVConfigManager.Desktop.Services;

public sealed class AvaloniaCharacterBackupDialogService(
    Func<Window?> ownerAccessor,
    PreviewSnapshotUseCase previewSnapshot,
    RestoreSnapshotUseCase restoreSnapshot,
    ISnapshotArchiveService archiveService,
    ScanSnapshotLibraryUseCase scanSnapshotLibrary,
    PruneAutomaticBackupsUseCase pruneAutomaticBackups,
    SettingsService settingsService,
    ITextLocalizer text) : ICharacterBackupDialogService
{
    public async Task<bool> ShowAsync(
        CharacterBackupDialogContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerAccessor();
        if (owner is null)
        {
            return false;
        }

        var settings = await settingsService.GetAsync(cancellationToken);
        var viewModel = new CharacterBackupsViewModel(
            context,
            previewSnapshot,
            restoreSnapshot,
            archiveService,
            scanSnapshotLibrary,
            pruneAutomaticBackups,
            settings.AutomaticBackups,
            settings.ManualCharacterBackupCleanup,
            text);
        var window = new CharacterBackupsWindow
        {
            DataContext = viewModel,
        };
        await window.ShowDialog(owner);
        return viewModel.Changed;
    }
}
