using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Desktop.Localization;

namespace FFXIVConfigManager.Desktop.ViewModels;

public sealed partial class SettingsBackupScopeViewModel(
    string libraryRoot,
    bool isRestore,
    SettingsBackupScope availableScopes,
    ISettingsBackupService backupService,
    ITextLocalizer text) : ViewModelBase
{
    public string Title { get; } = isRestore
        ? text["RestoreSoftwareSettings"]
        : text["BackupSoftwareSettings"];

    public string Notice { get; } = isRestore
        ? text["SettingsRestoreNotice"]
        : text["SettingsBackupOverwriteNotice"];

    public string ConfirmButtonText { get; } = isRestore
        ? text["RestoreSoftwareSettings"]
        : text["BackupSoftwareSettings"];

    public bool Completed { get; private set; }

    public bool CanIncludeCharacterAliases { get; } =
        availableScopes.HasFlag(SettingsBackupScope.CharacterAliases);

    public bool CanIncludeCustomProfiles { get; } =
        availableScopes.HasFlag(SettingsBackupScope.CustomProfiles);

    [ObservableProperty]
    public partial bool IncludeCharacterAliases { get; set; } =
        availableScopes.HasFlag(SettingsBackupScope.CharacterAliases);

    [ObservableProperty]
    public partial bool IncludeCustomProfiles { get; set; } =
        availableScopes.HasFlag(SettingsBackupScope.CustomProfiles);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        var scopes = GetScopes();
        if (scopes == SettingsBackupScope.None)
        {
            StatusMessage = text["SettingsScopeRequired"];
            return;
        }

        IsBusy = true;
        try
        {
            if (isRestore)
            {
                await backupService.RestoreAsync(libraryRoot, scopes, cancellationToken);
                StatusMessage = text["SettingsRestoreSucceeded"];
            }
            else
            {
                await backupService.BackupAsync(libraryRoot, scopes, cancellationToken);
                StatusMessage = text["SettingsBackupSucceeded"];
            }

            Completed = true;
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format(
                isRestore ? "SettingsRestoreFailedFormat" : "SettingsBackupFailedFormat",
                exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private SettingsBackupScope GetScopes()
    {
        var scopes = SettingsBackupScope.None;
        if (IncludeCharacterAliases)
        {
            scopes |= SettingsBackupScope.CharacterAliases;
        }

        if (IncludeCustomProfiles)
        {
            scopes |= SettingsBackupScope.CustomProfiles;
        }

        return scopes;
    }

    private bool CanConfirm() => !IsBusy;
}
