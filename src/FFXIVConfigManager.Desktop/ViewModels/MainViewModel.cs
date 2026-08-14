using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.Services;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Files;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Desktop.ViewModels;

public partial class MainViewModel(
    ScanProfilesUseCase scanProfiles,
    SettingsService settingsService,
    CreateCharacterSnapshotUseCase createSnapshot,
    ScanSnapshotLibraryUseCase scanSnapshotLibrary,
    PreviewSnapshotUseCase previewSnapshot,
    RestoreSnapshotUseCase restoreSnapshot,
    PreviewCharacterMigrationUseCase previewMigration,
    MigrateCharacterConfigurationUseCase migrateCharacter,
    IIncompleteRestoreRecovery incompleteRestoreRecovery,
    ISnapshotArchiveService snapshotArchiveService,
    ISettingsTransferService settingsTransferService,
    IFolderPickerService folderPicker,
    ITextLocalizer text) : ViewModelBase
{
    private readonly List<SnapshotRowViewModel> _allSnapshots = [];
    private readonly Dictionary<Guid, GameProfile> _currentProfiles = [];
    private readonly Dictionary<(Guid ProfileId, string Folder), CharacterConfiguration> _currentCharacters = [];
    private SnapshotLibraryEntry? _previewedSnapshot;
    private GameProfile? _previewedTargetProfile;
    private CharacterConfiguration? _previewedTarget;
    private CharacterRowViewModel? _previewedMigrationSource;
    private CharacterRowViewModel? _previewedMigrationTarget;
    private ConfigScope _previewedMigrationScopes;

    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    public ObservableCollection<SnapshotRowViewModel> Snapshots { get; } = [];

    public ObservableCollection<SnapshotFilePreviewViewModel> SnapshotPreviewFiles { get; } = [];

    public ObservableCollection<SnapshotFilePreviewViewModel> MigrationPreviewFiles { get; } = [];

    public IReadOnlyList<MigrationScopeOptionViewModel> MigrationScopes { get; } =
    [
        new(ConfigScope.Hud, text["ScopeHud"]),
        new(ConfigScope.Character, text["ScopeCharacter"]),
        new(ConfigScope.Controls, text["ScopeControls"]),
        new(ConfigScope.Hotbars, text["ScopeHotbars"]),
        new(ConfigScope.Macros, text["ScopeMacros"]),
        new(ConfigScope.Gearsets, text["ScopeGearsets"]),
        new(ConfigScope.UiState, text["ScopeUiState"]),
        new(ConfigScope.AllKnownFiles, text["ScopeAllKnown"], isSelected: false),
    ];

    public IReadOnlyList<GameRegionOption> RegionOptions { get; } =
    [
        new(GameRegion.International, text["RegionInternational"]),
        new(GameRegion.China, text["RegionChina"]),
        new(GameRegion.Custom, text["RegionOther"]),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDashboardPage))]
    [NotifyPropertyChangedFor(nameof(IsCharactersPage))]
    [NotifyPropertyChangedFor(nameof(IsBackupsPage))]
    [NotifyPropertyChangedFor(nameof(IsMigrationPage))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPage))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    public partial NavigationPage CurrentPage { get; private set; } = NavigationPage.Dashboard;

    public bool IsDashboardPage => CurrentPage == NavigationPage.Dashboard;

    public bool IsCharactersPage => CurrentPage == NavigationPage.Characters;

    public bool IsBackupsPage => CurrentPage == NavigationPage.Backups;

    public bool IsMigrationPage => CurrentPage == NavigationPage.Migration;

    public bool IsSettingsPage => CurrentPage == NavigationPage.Settings;

    public string PageTitle => CurrentPage switch
    {
        NavigationPage.Dashboard => text["Dashboard"],
        NavigationPage.Characters => text["Characters"],
        NavigationPage.Backups => text["Backups"],
        NavigationPage.Migration => text["Migration"],
        NavigationPage.Settings => text["Settings"],
        _ => text["AppTitle"],
    };

    public string PageSubtitle => CurrentPage switch
    {
        NavigationPage.Dashboard => text["DashboardSubtitle"],
        NavigationPage.Characters => text["CharactersSubtitle"],
        NavigationPage.Backups => text["BackupsSubtitle"],
        NavigationPage.Migration => text["MigrationSubtitle"],
        NavigationPage.Settings => text["SettingsSubtitle"],
        _ => string.Empty,
    };

    [ObservableProperty]
    public partial int BackupCount { get; private set; }

    [ObservableProperty]
    public partial int HealthyBackupCount { get; private set; }

    [ObservableProperty]
    public partial int CorruptedBackupCount { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectSnapshotLibraryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewMigrationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMigrationCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = text["PreparingScan"];

    [ObservableProperty]
    public partial string ProfileName { get; private set; } = text["NoProfileDiscovered"];

    [ObservableProperty]
    public partial string ConfigRoot { get; private set; } = "—";

    [ObservableProperty]
    public partial string Summary { get; private set; } = text["ZeroCharacters"];

    [ObservableProperty]
    public partial string SnapshotLibraryPath { get; private set; } = text["BackupLibraryNotSet"];

    [ObservableProperty]
    public partial string SnapshotHistorySummary { get; private set; } = text["NoBackups"];

    [ObservableProperty]
    public partial string SnapshotPreviewTitle { get; private set; } = text["SelectBackupForPreview"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRestoreCommand))]
    public partial bool CanRestorePreview { get; private set; }

    [ObservableProperty]
    public partial string SnapshotFilter { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewMigrationCommand))]
    public partial CharacterRowViewModel? SelectedMigrationSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewMigrationCommand))]
    public partial CharacterRowViewModel? SelectedMigrationTarget { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmMigrationCommand))]
    public partial bool CanConfirmMigration { get; private set; }

    [ObservableProperty]
    public partial string MigrationPreviewTitle { get; private set; } = text["SelectCharactersForMigrationPreview"];

    [ObservableProperty]
    public partial string NewProfileName { get; set; } = text["RegionChina"];

    [ObservableProperty]
    public partial GameRegionOption SelectedRegion { get; set; } =
        new(GameRegion.China, text["RegionChina"]);

    [RelayCommand]
    private void ShowDashboard() => CurrentPage = NavigationPage.Dashboard;

    [RelayCommand]
    private void ShowCharacters() => CurrentPage = NavigationPage.Characters;

    [RelayCommand]
    private void ShowBackups() => CurrentPage = NavigationPage.Backups;

    [RelayCommand]
    private void ShowMigration() => CurrentPage = NavigationPage.Migration;

    [RelayCommand]
    private void ShowSettings() => CurrentPage = NavigationPage.Settings;

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = text["Scanning"];

        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            SnapshotLibraryPath = settings.SnapshotLibraryPath ?? text["BackupLibraryNotSet"];
            var aliases = BuildAliasLookup(settings.CharacterAliases);
            var results = await scanProfiles.ExecuteAsync(cancellationToken);
            var recoveryResults = await incompleteRestoreRecovery.RecoverAsync(
                results.SelectMany(result => result.Characters)
                    .Select(character => character.FullPath),
                cancellationToken);
            if (recoveryResults.Any(result => result.Recovered))
            {
                results = await scanProfiles.ExecuteAsync(cancellationToken);
            }

            Characters.Clear();
            Profiles.Clear();
            SelectedMigrationSource = null;
            SelectedMigrationTarget = null;
            ClearMigrationPreview();
            _currentProfiles.Clear();
            _currentCharacters.Clear();

            if (results.Count == 0)
            {
                ProfileName = text["NoAvailableProfile"];
                ConfigRoot = text["AddCustomProfileHint"];
                Summary = text["ZeroCharacters"];
                StatusMessage = text["NoConfiguredDirectory"];
                await LoadSnapshotsAsync(settings, aliases, cancellationToken);
                return;
            }

            var primaryResult = results[0];
            ProfileName = primaryResult.Profile.Name;
            ConfigRoot = primaryResult.Profile.ConfigRoot;

            foreach (var result in results)
            {
                _currentProfiles[result.Profile.Id] = result.Profile;
                Profiles.Add(ProfileRowViewModel.From(
                    result.Profile,
                    RemoveProfileAsync));

                foreach (var character in result.Characters)
                {
                    _currentCharacters[(result.Profile.Id, character.FolderName.Value)] = character;
                    var alias = FindAlias(
                        aliases,
                        result.Profile.Id,
                        character.FolderName.Value);
                    Characters.Add(CharacterRowViewModel.From(
                        result.Profile,
                        character,
                        alias,
                        SaveAliasAsync,
                        CreateSnapshotAsync));
                }
            }

            Summary = text.Format("SummaryFormat", Characters.Count, results.Count);
            var issues = results
                .Where(result => result.Issue is not null)
                .Select(result => $"{result.Profile.Name}：{result.Issue}")
                .ToArray();
            var failedRecoveries = recoveryResults
                .Where(result => !result.Recovered)
                .SelectMany(result => result.Errors)
                .ToArray();
            StatusMessage = failedRecoveries.Length > 0
                ? text.Format("InterruptedTransactionsFormat", string.Join("；", failedRecoveries))
                : recoveryResults.Count > 0
                    ? text.Format("RecoveredTransactionsFormat", recoveryResults.Count)
                    : issues.Length > 0
                        ? string.Join("　", issues)
                        : Characters.Count == 0
                            ? text["NoCharacterFound"]
                            : text.Format("ScanCompletedFormat", DateTimeOffset.Now);
            await LoadSnapshotsAsync(settings, aliases, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = text["ScanCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("ScanFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task ExportSettingsAsync(CancellationToken cancellationToken)
    {
        var path = await folderPicker.PickSaveFileAsync(
            text["ExportSettingsPickerTitle"],
            $"FFXIVConfigManager-settings-{DateTimeOffset.Now:yyyyMMdd}",
            ".ffxivconfig-settings.json",
            cancellationToken);
        if (path is null)
        {
            return;
        }

        try
        {
            await settingsTransferService.ExportAsync(path, cancellationToken);
            StatusMessage = text.Format("SettingsExportedFormat", Path.GetFileName(path));
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("ExportSettingsFailedFormat", exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task ImportSettingsAsync(CancellationToken cancellationToken)
    {
        var path = await folderPicker.PickOpenFileAsync(
            text["ImportSettingsPickerTitle"],
            ".ffxivconfig-settings.json",
            cancellationToken);
        if (path is null)
        {
            return;
        }

        try
        {
            var imported = await settingsTransferService.ImportAsync(path, cancellationToken);
            await settingsService.ImportPortableAsync(imported, cancellationToken);
            StatusMessage = text["SettingsImported"];
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("ImportSettingsFailedFormat", exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task AddProfileAsync(CancellationToken cancellationToken)
    {
        var selectedPath = await folderPicker.PickFolderAsync(
            text["SelectProfileDirectory"],
            cancellationToken);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedPath));
        if (Profiles.Any(profile => comparer.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.ConfigRoot)),
                normalizedPath)))
        {
            StatusMessage = text["ProfileAlreadyExists"];
            return;
        }

        IsBusy = true;
        try
        {
            var name = string.IsNullOrWhiteSpace(NewProfileName)
                ? Path.GetFileName(normalizedPath)
                : NewProfileName.Trim();
            await settingsService.AddProfileAsync(
                name,
                SelectedRegion.Region,
                normalizedPath,
                cancellationToken);
            StatusMessage = text.Format("ProfileAddedFormat", name);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = text.Format("AddProfileFailedFormat", exception.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task SelectSnapshotLibraryAsync(CancellationToken cancellationToken)
    {
        var selectedPath = await folderPicker.PickFolderAsync(
            text["SelectBackupLibraryDirectory"],
            cancellationToken);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await settingsService.SetSnapshotLibraryPathAsync(selectedPath, cancellationToken);
            SnapshotLibraryPath = Path.GetFullPath(selectedPath);
            StatusMessage = text["BackupLibraryUpdated"];
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = text.Format("SetBackupLibraryFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveProfileAsync(Guid profileId)
    {
        IsBusy = true;
        try
        {
            await settingsService.RemoveProfileAsync(profileId);
            StatusMessage = text["ProfileRemoved"];
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("RemoveProfileFailedFormat", exception.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync();
    }

    private async Task CreateSnapshotAsync(
        GameProfile profile,
        CharacterConfiguration character)
    {
        if (IsBusy)
        {
            StatusMessage = text["OperationInProgress"];
            return;
        }

        IsBusy = true;
        try
        {
            var settings = await settingsService.GetAsync();
            var libraryPath = settings.SnapshotLibraryPath;
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                libraryPath = await folderPicker.PickFolderAsync(text["SelectBackupLibraryDirectory"]);
                if (string.IsNullOrWhiteSpace(libraryPath))
                {
                    StatusMessage = text["CreateBackupCanceled"];
                    return;
                }

                await settingsService.SetSnapshotLibraryPathAsync(libraryPath);
                SnapshotLibraryPath = Path.GetFullPath(libraryPath);
            }

            StatusMessage = text.Format("CreatingBackupFormat", character.FolderName.Value);
            var result = await createSnapshot.ExecuteAsync(profile, character, libraryPath);
            StatusMessage = text.Format(
                "BackupCreatedFormat",
                Path.GetFileName(result.ArchivePath));
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("CreateBackupFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadSnapshotsAsync()
    {
        var settings = await settingsService.GetAsync();
        var aliases = BuildAliasLookup(settings.CharacterAliases);
        await LoadSnapshotsAsync(settings, aliases, CancellationToken.None);
    }

    private async Task LoadSnapshotsAsync(
        ApplicationSettings settings,
        IReadOnlyDictionary<(Guid ProfileId, string CharacterFolder), string> aliases,
        CancellationToken cancellationToken)
    {
        _allSnapshots.Clear();
        Snapshots.Clear();
        BackupCount = 0;
        HealthyBackupCount = 0;
        CorruptedBackupCount = 0;
        SnapshotPreviewFiles.Clear();
        SnapshotPreviewTitle = text["SelectBackupForPreview"];
        ClearRestoreSelection();

        if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
        {
            SnapshotHistorySummary = text["BackupLibraryNotSet"];
            return;
        }

        var entries = await scanSnapshotLibrary.ExecuteAsync(
            settings.SnapshotLibraryPath,
            cancellationToken);
        foreach (var entry in entries)
        {
            string? alias = null;
            if (entry.Manifest is not null)
            {
                alias = FindAlias(
                    aliases,
                    entry.Manifest.Source.ProfileId,
                    entry.Manifest.Source.CharacterFolder);
            }

            _allSnapshots.Add(SnapshotRowViewModel.From(
                entry,
                alias,
                PreviewSnapshotAsync,
                DeleteSnapshotAsync));
        }

        ApplySnapshotFilter();
        var corrupted = entries.Count(entry =>
            entry.IntegrityStatus == SnapshotIntegrityStatus.Corrupted);
        BackupCount = entries.Count;
        CorruptedBackupCount = corrupted;
        HealthyBackupCount = entries.Count - corrupted;
        SnapshotHistorySummary = corrupted == 0
            ? text.Format("AllBackupsValidFormat", entries.Count)
            : text.Format("BackupSummaryFormat", entries.Count, corrupted);
    }

    private async Task DeleteSnapshotAsync(SnapshotLibraryEntry snapshot)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await snapshotArchiveService.DeleteAsync(snapshot.ArchivePath);
            StatusMessage = text.Format("BackupDeletedFormat", Path.GetFileName(snapshot.ArchivePath));
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("DeleteBackupFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PreviewSnapshotAsync(SnapshotLibraryEntry snapshot)
    {
        if (IsBusy)
        {
            StatusMessage = text["OperationInProgress"];
            return;
        }

        IsBusy = true;
        try
        {
            CharacterConfiguration? target = null;
            GameProfile? targetProfile = null;
            if (snapshot.Manifest is not null)
            {
                _currentCharacters.TryGetValue(
                    (snapshot.Manifest.Source.ProfileId, snapshot.Manifest.Source.CharacterFolder),
                    out target);
                _currentProfiles.TryGetValue(snapshot.Manifest.Source.ProfileId, out targetProfile);
            }

            var preview = await previewSnapshot.ExecuteAsync(snapshot, target);
            SnapshotPreviewFiles.Clear();
            foreach (var file in preview.Files)
            {
                SnapshotPreviewFiles.Add(SnapshotFilePreviewViewModel.From(file));
            }

            var changed = preview.Files.Count(file =>
                file.Difference is SnapshotFileDifference.Different or
                    SnapshotFileDifference.MissingFromTarget);
            SnapshotPreviewTitle = target is null
                ? text["TargetCharacterUnavailable"]
                : text.Format(
                    "RestorePreviewFormat",
                    changed,
                    preview.Files.Count - changed);
            _previewedSnapshot = snapshot;
            _previewedTarget = target;
            _previewedTargetProfile = targetProfile;
            CanRestorePreview = target is not null && targetProfile is not null;
            StatusMessage = text["BackupPreviewReady"];
        }
        catch (Exception exception)
        {
            SnapshotPreviewFiles.Clear();
            SnapshotPreviewTitle = text["PreviewUnavailable"];
            ClearRestoreSelection();
            StatusMessage = text.Format("BackupPreviewFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanConfirmRestore))]
    private async Task ConfirmRestoreAsync(CancellationToken cancellationToken)
    {
        if (_previewedSnapshot is null ||
            _previewedTarget is null ||
            _previewedTargetProfile is null)
        {
            return;
        }

        IsBusy = true;
        string? completionMessage = null;
        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
            {
                throw new InvalidOperationException(text["RecoveryPointRequiresLibrary"]);
            }

            StatusMessage = text["Restoring"];
            var result = await restoreSnapshot.ExecuteAsync(
                _previewedSnapshot,
                _previewedTargetProfile,
                _previewedTarget,
                settings.SnapshotLibraryPath,
                cancellationToken);
            completionMessage = text.Format(
                "RestoreCompletedFormat",
                result.RestoreResult.RestoredFileCount,
                Path.GetFileName(result.RecoveryPoint.ArchivePath));
            StatusMessage = completionMessage;
            ClearRestoreSelection();
        }
        catch (SnapshotRestoreException exception)
        {
            StatusMessage = exception.RollbackCompleted
                ? exception.Message
                : text.Format("RestoreCriticalErrorFormat", exception.Message);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = text["RestoreCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("RestoreFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (completionMessage is not null)
        {
            await RefreshAsync(cancellationToken);
            StatusMessage = completionMessage;
        }
    }

    private bool CanConfirmRestore() => CanRestorePreview && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanPreviewMigration))]
    private async Task PreviewMigrationAsync(CancellationToken cancellationToken)
    {
        if (SelectedMigrationSource is null || SelectedMigrationTarget is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var scopes = GetSelectedMigrationScopes();
            var preview = await previewMigration.ExecuteAsync(
                SelectedMigrationSource.Character,
                SelectedMigrationTarget.Character,
                scopes,
                cancellationToken);
            MigrationPreviewFiles.Clear();
            foreach (var file in preview.Files)
            {
                MigrationPreviewFiles.Add(SnapshotFilePreviewViewModel.From(file));
            }

            var changed = preview.Files.Count(file =>
                file.Difference != SnapshotFileDifference.Identical);
            MigrationPreviewTitle = text.Format(
                "MigrationPreviewFormat",
                changed,
                preview.Files.Count - changed);
            _previewedMigrationSource = SelectedMigrationSource;
            _previewedMigrationTarget = SelectedMigrationTarget;
            _previewedMigrationScopes = scopes;
            CanConfirmMigration = true;
            StatusMessage = text["MigrationPreviewReady"];
        }
        catch (Exception exception)
        {
            ClearMigrationPreview();
            MigrationPreviewTitle = text["MigrationPreviewUnavailable"];
            StatusMessage = text.Format("MigrationPreviewFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunMigration))]
    private async Task ConfirmMigrationAsync(CancellationToken cancellationToken)
    {
        if (_previewedMigrationSource is null || _previewedMigrationTarget is null)
        {
            return;
        }

        var currentScopes = GetSelectedMigrationScopes();
        if (currentScopes != _previewedMigrationScopes ||
            SelectedMigrationSource != _previewedMigrationSource ||
            SelectedMigrationTarget != _previewedMigrationTarget)
        {
            ClearMigrationPreview();
            StatusMessage = text["MigrationSelectionChanged"];
            return;
        }

        IsBusy = true;
        string? completionMessage = null;
        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
            {
                throw new InvalidOperationException(text["MigrationRequiresLibrary"]);
            }

            StatusMessage = text["PreparingMigration"];
            var result = await migrateCharacter.ExecuteAsync(
                _previewedMigrationSource.Profile,
                _previewedMigrationSource.Character,
                _previewedMigrationTarget.Profile,
                _previewedMigrationTarget.Character,
                settings.SnapshotLibraryPath,
                currentScopes,
                cancellationToken);
            completionMessage = text.Format(
                "MigrationCompletedFormat",
                result.RestoreResult.RestoredFileCount,
                Path.GetFileName(result.TargetRecoveryPoint.ArchivePath));
            StatusMessage = completionMessage;
            ClearMigrationPreview();
        }
        catch (SnapshotRestoreException exception)
        {
            StatusMessage = exception.RollbackCompleted
                ? exception.Message
                : text.Format("MigrationCriticalErrorFormat", exception.Message);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = text["MigrationCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("MigrationFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }

        if (completionMessage is not null)
        {
            await RefreshAsync(cancellationToken);
            StatusMessage = completionMessage;
        }
    }

    private bool CanPreviewMigration() =>
        !IsBusy &&
        SelectedMigrationSource is not null &&
        SelectedMigrationTarget is not null &&
        SelectedMigrationSource != SelectedMigrationTarget;

    private bool CanRunMigration() => CanConfirmMigration && !IsBusy;

    private ConfigScope GetSelectedMigrationScopes() =>
        MigrationScopes
            .Where(scope => scope.IsSelected)
            .Aggregate(ConfigScope.None, (current, scope) => current | scope.Scope);

    partial void OnSelectedMigrationSourceChanged(CharacterRowViewModel? value) =>
        ClearMigrationPreview();

    partial void OnSelectedMigrationTargetChanged(CharacterRowViewModel? value) =>
        ClearMigrationPreview();

    private void ClearMigrationPreview()
    {
        _previewedMigrationSource = null;
        _previewedMigrationTarget = null;
        _previewedMigrationScopes = ConfigScope.None;
        CanConfirmMigration = false;
        MigrationPreviewFiles.Clear();
        MigrationPreviewTitle = text["SelectCharactersForMigrationPreview"];
    }

    private void ClearRestoreSelection()
    {
        _previewedSnapshot = null;
        _previewedTarget = null;
        _previewedTargetProfile = null;
        CanRestorePreview = false;
    }

    partial void OnSnapshotFilterChanged(string value) => ApplySnapshotFilter();

    private void ApplySnapshotFilter()
    {
        var filter = SnapshotFilter.Trim();
        Snapshots.Clear();
        foreach (var snapshot in _allSnapshots.Where(item =>
                     filter.Length == 0 ||
                     item.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            Snapshots.Add(snapshot);
        }
    }

    private async Task SaveAliasAsync(
        Guid profileId,
        CharacterFolderName folderName,
        string alias)
    {
        try
        {
            await settingsService.SetCharacterAliasAsync(profileId, folderName, alias);
            StatusMessage = string.IsNullOrWhiteSpace(alias)
                ? text["CharacterTagCleared"]
                : text.Format("CharacterTagSavedFormat", alias.Trim());
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("SaveCharacterTagFailedFormat", exception.Message);
        }
    }

    private static IReadOnlyDictionary<(Guid ProfileId, string CharacterFolder), string> BuildAliasLookup(
        IReadOnlyList<CharacterAliasSetting> aliases) =>
        aliases
            .GroupBy(item => (item.ProfileId, item.CharacterFolder))
            .ToDictionary(group => group.Key, group => group.Last().Alias);

    private static string? FindAlias(
        IReadOnlyDictionary<(Guid ProfileId, string CharacterFolder), string> aliases,
        Guid profileId,
        string characterFolder)
    {
        if (aliases.TryGetValue((profileId, characterFolder), out var exactAlias))
        {
            return exactAlias;
        }

        var matches = aliases
            .Where(item => string.Equals(
                item.Key.CharacterFolder,
                characterFolder,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Value)
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private bool CanRunCommand() => !IsBusy;
}

public enum NavigationPage
{
    Dashboard,
    Characters,
    Backups,
    Migration,
    Settings,
}

public sealed record GameRegionOption(GameRegion Region, string DisplayName);

public sealed partial class MigrationScopeOptionViewModel(
    ConfigScope scope,
    string displayName,
    bool isSelected = true) : ObservableObject
{
    public ConfigScope Scope { get; } = scope;

    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}

public sealed partial class ProfileRowViewModel : ObservableObject
{
    private readonly Func<Guid, Task> _remove;

    private ProfileRowViewModel(GameProfile profile, Func<Guid, Task> remove)
    {
        Id = profile.Id;
        Name = profile.Name;
        Region = profile.Region switch
        {
            GameRegion.International => ResourceTextLocalizer.Instance["RegionInternational"],
            GameRegion.China => ResourceTextLocalizer.Instance["RegionChina"],
            _ => ResourceTextLocalizer.Instance["RegionOther"],
        };
        ConfigRoot = profile.ConfigRoot;
        CanRemove = profile.Origin == GameProfileOrigin.User;
        _remove = remove;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string Region { get; }

    public string ConfigRoot { get; }

    public bool CanRemove { get; }

    public static ProfileRowViewModel From(GameProfile profile, Func<Guid, Task> remove) =>
        new(profile, remove);

    [RelayCommand(CanExecute = nameof(CanRemoveProfile))]
    private Task RemoveAsync() => _remove(Id);

    private bool CanRemoveProfile() => CanRemove;
}

public sealed partial class CharacterRowViewModel : ObservableObject
{
    private readonly Guid _profileId;
    private readonly CharacterFolderName _characterFolder;
    private readonly GameProfile _profile;
    private readonly CharacterConfiguration _character;
    private readonly Func<Guid, CharacterFolderName, string, Task> _saveAlias;
    private readonly Func<GameProfile, CharacterConfiguration, Task> _createSnapshot;

    private CharacterRowViewModel(
        GameProfile profile,
        CharacterConfiguration character,
        string? alias,
        Func<Guid, CharacterFolderName, string, Task> saveAlias,
        Func<GameProfile, CharacterConfiguration, Task> createSnapshot)
    {
        _profileId = profile.Id;
        _characterFolder = character.FolderName;
        _profile = profile;
        _character = character;
        _saveAlias = saveAlias;
        _createSnapshot = createSnapshot;
        ProfileName = profile.Name;
        FolderName = character.FolderName.Value;
        Alias = alias ?? string.Empty;
        LastModified = character.LastModifiedUtc.ToLocalTime().ToString("g");
        FileSummary = ResourceTextLocalizer.Instance.Format(
            "KnownFileCountFormat",
            character.ExistingFileCount,
            character.ExpectedFileCount);
    }

    public GameProfile Profile => _profile;

    public CharacterConfiguration Character => _character;

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? FolderName : Alias;

    public string ProfileName { get; }

    public string FolderName { get; }

    public string LastModified { get; }

    public string FileSummary { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Alias { get; set; }

    public static CharacterRowViewModel From(
        GameProfile profile,
        CharacterConfiguration character,
        string? alias,
        Func<Guid, CharacterFolderName, string, Task> saveAlias,
        Func<GameProfile, CharacterConfiguration, Task> createSnapshot) =>
        new(profile, character, alias, saveAlias, createSnapshot);

    [RelayCommand]
    private Task SaveAliasAsync() => _saveAlias(_profileId, _characterFolder, Alias);

    [RelayCommand]
    private Task CreateSnapshotAsync() => _createSnapshot(_profile, _character);
}

public sealed partial class SnapshotRowViewModel : ObservableObject
{
    private readonly SnapshotLibraryEntry _entry;
    private readonly Func<SnapshotLibraryEntry, Task> _preview;
    private readonly Func<SnapshotLibraryEntry, Task> _delete;

    private SnapshotRowViewModel(
        SnapshotLibraryEntry entry,
        string? alias,
        Func<SnapshotLibraryEntry, Task> preview,
        Func<SnapshotLibraryEntry, Task> delete)
    {
        _entry = entry;
        _preview = preview;
        _delete = delete;
        var manifest = entry.Manifest;
        CharacterName = string.IsNullOrWhiteSpace(alias)
            ? manifest?.Source.CharacterFolder ?? Path.GetFileName(entry.ArchivePath)
            : alias;
        var text = ResourceTextLocalizer.Instance;
        CharacterFolder = manifest?.Source.CharacterFolder ?? text["Unavailable"];
        ProfileName = manifest?.Source.ProfileName ?? text["UnknownSource"];
        CreatedAt = (manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc)
            .ToLocalTime()
            .ToString("g");
        FileSummary = manifest is null
            ? text["ManifestUnavailable"]
            : text.Format(
                "FileCountAndSizeFormat",
                manifest.Files.Count,
                FormatSize(entry.ArchiveSize));
        IntegrityText = entry.IntegrityStatus == SnapshotIntegrityStatus.Valid
            ? text["IntegrityValid"]
            : text["IntegrityCorrupted"];
        TypeText = manifest?.Reason switch
        {
            SnapshotReason.BeforeMigration => text["TypeBeforeMigration"],
            SnapshotReason.BeforeRestore => text["TypeBeforeRestore"],
            SnapshotReason.MigrationSource => text["TypeMigrationSource"],
            SnapshotReason.Manual => text["TypeManual"],
            _ => text["TypeUnknown"],
        };
        ErrorSummary = entry.Errors.Count == 0
            ? string.Empty
            : string.Join("；", entry.Errors);
        CanPreview = entry.IntegrityStatus == SnapshotIntegrityStatus.Valid;
        SearchText = string.Join(
            ' ',
            CharacterName,
            CharacterFolder,
            ProfileName,
            IntegrityText,
            TypeText,
            Path.GetFileName(entry.ArchivePath));
    }

    public string CharacterName { get; }

    public string CharacterFolder { get; }

    public string ProfileName { get; }

    public string CreatedAt { get; }

    public string FileSummary { get; }

    public string IntegrityText { get; }

    public string TypeText { get; }

    public string ErrorSummary { get; }

    public bool CanPreview { get; }

    [ObservableProperty]
    public partial bool IsDeleteArmed { get; private set; }

    public string DeleteButtonText => IsDeleteArmed
        ? ResourceTextLocalizer.Instance["ConfirmDelete"]
        : ResourceTextLocalizer.Instance["Delete"];

    public string SearchText { get; }

    public static SnapshotRowViewModel From(
        SnapshotLibraryEntry entry,
        string? alias,
        Func<SnapshotLibraryEntry, Task> preview,
        Func<SnapshotLibraryEntry, Task> delete) =>
        new(entry, alias, preview, delete);

    [RelayCommand(CanExecute = nameof(CanPreviewSnapshot))]
    private Task PreviewAsync() => _preview(_entry);

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (!IsDeleteArmed)
        {
            IsDeleteArmed = true;
            OnPropertyChanged(nameof(DeleteButtonText));
            return;
        }

        await _delete(_entry);
    }

    private bool CanPreviewSnapshot() => CanPreview;

    private static string FormatSize(long size) => size switch
    {
        >= 1024 * 1024 => $"{size / 1024d / 1024d:F1} MiB",
        >= 1024 => $"{size / 1024d:F1} KiB",
        _ => $"{size} B",
    };
}

public sealed record SnapshotFilePreviewViewModel(
    string FileName,
    string SnapshotSize,
    string DifferenceText)
{
    public static SnapshotFilePreviewViewModel From(SnapshotFilePreview preview) =>
        new(
            preview.FileName,
            preview.SnapshotSize >= 1024
                ? $"{preview.SnapshotSize / 1024d:F1} KiB"
                : $"{preview.SnapshotSize} B",
            preview.Difference switch
            {
                SnapshotFileDifference.Identical => ResourceTextLocalizer.Instance["DifferenceIdentical"],
                SnapshotFileDifference.Different => ResourceTextLocalizer.Instance["DifferenceDifferent"],
                SnapshotFileDifference.MissingFromTarget => ResourceTextLocalizer.Instance["DifferenceMissing"],
                _ => ResourceTextLocalizer.Instance["DifferenceTargetUnavailable"],
            });
}
