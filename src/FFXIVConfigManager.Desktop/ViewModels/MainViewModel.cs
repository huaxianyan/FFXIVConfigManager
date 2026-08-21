using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Appearances;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Application.Portraits;
using FFXIVConfigManager.Application.Updates;
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
    PreviewCharacterMigrationUseCase previewMigration,
    MigrateCharacterConfigurationUseCase migrateCharacter,
    IIncompleteRestoreRecovery incompleteRestoreRecovery,
    ISettingsBackupService settingsBackupService,
    ICharacterBackupDialogService characterBackupDialog,
    IAppearanceBackupService appearanceBackupService,
    IPortraitManagementService portraitManagementService,
    IPortraitBackupEditDialogService portraitBackupEditDialog,
    ISettingsBackupDialogService settingsBackupDialog,
    IApplicationUpdateService applicationUpdateService,
    IApplicationUpdateProxy applicationUpdateProxy,
    IUpdateProxyDialogService updateProxyDialog,
    IApplicationUpdateInstaller applicationUpdateInstaller,
    IFolderPickerService folderPicker,
    ITextLocalizer text) : ViewModelBase
{
    private readonly List<CharacterBackupGroupViewModel> _allBackupGroups = [];
    private readonly List<SnapshotLibraryEntry> _snapshotEntries = [];
    private readonly Dictionary<Guid, GameProfile> _currentProfiles = [];
    private readonly Dictionary<(Guid ProfileId, string Folder), CharacterConfiguration> _currentCharacters = [];
    private ApplicationRelease? _availableRelease;
    private CharacterRowViewModel? _previewedMigrationSource;
    private CharacterRowViewModel? _previewedMigrationTarget;
    private ConfigScope _previewedMigrationScopes;
    private string? _updateProxyAddress;
    private bool _isApplyingSettings;
    private bool _isApplyingUpdateProxySettings;
    private bool _migrationScopeHandlersRegistered;

    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    public ObservableCollection<CharacterRowViewModel> VisibleCharacters { get; } = [];

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    public ObservableCollection<CharacterBackupGroupViewModel> BackupGroups { get; } = [];

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
    [NotifyPropertyChangedFor(nameof(IsAppearancesPage))]
    [NotifyPropertyChangedFor(nameof(IsPortraitsPage))]
    [NotifyPropertyChangedFor(nameof(IsBackupsPage))]
    [NotifyPropertyChangedFor(nameof(IsMigrationPage))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPage))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    public partial NavigationPage CurrentPage { get; private set; } = NavigationPage.Dashboard;

    public bool IsDashboardPage => CurrentPage == NavigationPage.Dashboard;

    public bool IsCharactersPage => CurrentPage == NavigationPage.Characters;

    public bool IsAppearancesPage => CurrentPage == NavigationPage.Appearances;

    public bool IsPortraitsPage => CurrentPage == NavigationPage.Portraits;

    public bool IsBackupsPage => CurrentPage == NavigationPage.Backups;

    public bool IsMigrationPage => CurrentPage == NavigationPage.Migration;

    public bool IsSettingsPage => CurrentPage == NavigationPage.Settings;

    public string PageTitle => CurrentPage switch
    {
        NavigationPage.Dashboard => text["Dashboard"],
        NavigationPage.Characters => text["Characters"],
        NavigationPage.Appearances => text["CharacterAppearances"],
        NavigationPage.Portraits => text["PortraitManagement"],
        NavigationPage.Backups => text["Backups"],
        NavigationPage.Migration => text["Migration"],
        NavigationPage.Settings => text["Settings"],
        _ => text["AppTitle"],
    };

    public string PageSubtitle => CurrentPage switch
    {
        NavigationPage.Dashboard => text["DashboardSubtitle"],
        NavigationPage.Characters => text["CharactersSubtitle"],
        NavigationPage.Appearances => text["AppearancesSubtitle"],
        NavigationPage.Portraits => text["PortraitManagementSubtitle"],
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
    public partial int AppearanceBackupCount { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(BackupSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreSettingsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManageAppearancesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ManagePortraitsCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectSnapshotLibraryCommand))]
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
    public partial string SettingsBackupStatusText { get; private set; } = text["SettingsBackupMissing"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreSettingsCommand))]
    public partial bool CanRestoreSettingsBackup { get; private set; }

    public string CurrentVersionText { get; } = text.Format(
        "CurrentVersionFormat",
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? text["UnknownValue"]);

    [ObservableProperty]
    public partial string UpdateStatusText { get; private set; } = text["UpdateNotChecked"];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenUpdateProxySettingsCommand))]
    public partial bool IsUpdateBusy { get; private set; }

    [ObservableProperty]
    public partial bool IsUpdateProgressIndeterminate { get; private set; } = true;

    [ObservableProperty]
    public partial double UpdateProgressValue { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial bool IsUpdateAvailable { get; private set; }

    public bool IsAutomaticUpdateSupported => applicationUpdateInstaller.IsSupported;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenUpdateProxySettingsCommand))]
    public partial bool IsUpdateProxyEnabled { get; set; }

    [ObservableProperty]
    public partial string UpdateProxyStatusText { get; private set; } = text["UpdateProxyDisabled"];

    [ObservableProperty]
    public partial string SnapshotFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowOnlyTaggedCharacters { get; set; }

    [ObservableProperty]
    public partial AppearanceBackupsViewModel? AppearanceManager { get; private set; }

    [ObservableProperty]
    public partial PortraitManagementViewModel? PortraitManager { get; private set; }

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
            _updateProxyAddress = settings.UpdateProxyAddress;
            _isApplyingUpdateProxySettings = true;
            IsUpdateProxyEnabled = settings.IsUpdateProxyEnabled;
            _isApplyingUpdateProxySettings = false;
            ApplyUpdateProxy();
            _isApplyingSettings = true;
            ShowOnlyTaggedCharacters = settings.ShowOnlyTaggedCharacters;
            _isApplyingSettings = false;
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
            VisibleCharacters.Clear();
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
                        CreateSnapshotAsync,
                        ManageCharacterBackupsAsync));
                }
            }

            ApplyCharacterFilter();
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
            if (CurrentPage == NavigationPage.Appearances)
            {
                await LoadAppearanceManagerAsync(cancellationToken);
            }
            else if (CurrentPage == NavigationPage.Portraits)
            {
                await LoadPortraitManagerAsync(cancellationToken);
            }
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
            var alias = settings.CharacterAliases.LastOrDefault(item =>
                    item.ProfileId == profile.Id &&
                    string.Equals(
                        item.CharacterFolder,
                        character.FolderName.Value,
                        StringComparison.OrdinalIgnoreCase))?.Alias;
            var result = await createSnapshot.ExecuteAsync(
                profile,
                character,
                libraryPath,
                characterAlias: alias);
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
        _snapshotEntries.Clear();
        BackupGroups.Clear();
        BackupCount = 0;
        HealthyBackupCount = 0;
        CorruptedBackupCount = 0;
        AppearanceBackupCount = 0;
        if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
        {
            SnapshotHistorySummary = text["BackupLibraryNotSet"];
            SettingsBackupStatusText = text["SettingsBackupMissing"];
            CanRestoreSettingsBackup = false;
            UpdateCharacterBackupStatuses();
            return;
        }

        var entries = await scanSnapshotLibrary.ExecuteAsync(
            settings.SnapshotLibraryPath,
            cancellationToken);
        var appearanceBackups = await appearanceBackupService.ScanBackupsAsync(
            settings.SnapshotLibraryPath,
            cancellationToken);
        AppearanceBackupCount = appearanceBackups.Count;
        _snapshotEntries.AddRange(entries);
        RebuildBackupGroups(aliases);
        UpdateCharacterBackupStatuses();
        await LoadSettingsBackupStatusAsync(settings.SnapshotLibraryPath, cancellationToken);
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

    private void RebuildBackupGroups(
        IReadOnlyDictionary<(Guid ProfileId, string CharacterFolder), string> aliases)
    {
        BackupGroups.Clear();
        _allBackupGroups.Clear();
        var identified = _snapshotEntries
            .Where(entry => entry.Manifest is not null)
            .GroupBy(entry => (
                entry.Manifest!.Source.ProfileId,
                entry.Manifest.Source.CharacterFolder));
        foreach (var group in identified.OrderBy(item => item.Key.CharacterFolder))
        {
            var entries = group.ToArray();
            var manifest = entries
                .OrderByDescending(entry => entry.Manifest!.CreatedAtUtc)
                .First()
                .Manifest!;
            var alias = FindAlias(aliases, group.Key.ProfileId, group.Key.CharacterFolder)
                ?? entries
                    .OrderByDescending(entry => entry.Manifest!.CreatedAtUtc)
                    .Select(entry => entry.Manifest!.Source.CharacterAlias)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var characterName = string.IsNullOrWhiteSpace(alias)
                ? group.Key.CharacterFolder
                : alias;
            var target = ResolveLocalCharacter(group.Key.ProfileId, group.Key.CharacterFolder);
            _allBackupGroups.Add(CharacterBackupGroupViewModel.Create(
                characterName,
                manifest.Source.ProfileName,
                entries,
                () => OpenBackupGroupAsync(characterName, target.Profile, target.Character, entries)));
        }

        var unidentified = _snapshotEntries.Where(entry => entry.Manifest is null).ToArray();
        if (unidentified.Length > 0)
        {
            _allBackupGroups.Add(CharacterBackupGroupViewModel.Create(
                text["UnidentifiedBackups"],
                text["UnknownSource"],
                unidentified,
                () => OpenBackupGroupAsync(
                    text["UnidentifiedBackups"],
                    null,
                    null,
                    unidentified)));
        }
    }

    private void UpdateCharacterBackupStatuses()
    {
        foreach (var character in Characters)
        {
            character.SetBackups(FindBackupsForCharacter(
                character.Profile.Id,
                character.Character.FolderName.Value));
        }
    }

    private IReadOnlyList<SnapshotLibraryEntry> FindBackupsForCharacter(
        Guid profileId,
        string characterFolder)
    {
        var exact = _snapshotEntries.Where(entry =>
            entry.Manifest?.Source.ProfileId == profileId &&
            string.Equals(
                entry.Manifest.Source.CharacterFolder,
                characterFolder,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        var localFolderCount = Characters.Count(character => string.Equals(
            character.Character.FolderName.Value,
            characterFolder,
            StringComparison.OrdinalIgnoreCase));
        if (localFolderCount != 1)
        {
            return exact;
        }

        return _snapshotEntries.Where(entry => string.Equals(
                entry.Manifest?.Source.CharacterFolder,
                characterFolder,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private (GameProfile? Profile, CharacterConfiguration? Character) ResolveLocalCharacter(
        Guid profileId,
        string characterFolder)
    {
        if (_currentProfiles.TryGetValue(profileId, out var exactProfile))
        {
            _currentCharacters.TryGetValue((profileId, characterFolder), out var exactCharacter);
            return (exactProfile, exactCharacter);
        }

        var matches = Characters.Where(character => string.Equals(
                character.Character.FolderName.Value,
                characterFolder,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? (matches[0].Profile, matches[0].Character)
            : (null, null);
    }

    private Task ManageCharacterBackupsAsync(
        GameProfile profile,
        CharacterConfiguration character) =>
        OpenBackupGroupAsync(
            Characters.First(item => ReferenceEquals(item.Character, character)).DisplayName,
            profile,
            character,
            FindBackupsForCharacter(profile.Id, character.FolderName.Value));

    private async Task OpenBackupGroupAsync(
        string characterName,
        GameProfile? profile,
        CharacterConfiguration? character,
        IReadOnlyList<SnapshotLibraryEntry> backups)
    {
        var settings = await settingsService.GetAsync();
        if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
        {
            StatusMessage = text["BackupLibraryNotSet"];
            return;
        }

        var changed = await characterBackupDialog.ShowAsync(new CharacterBackupDialogContext(
            characterName,
            profile,
            character,
            _currentProfiles.Values.OrderBy(item => item.Name).ToArray(),
            settings.SnapshotLibraryPath,
            backups));
        if (changed)
        {
            await RefreshAsync();
        }
    }

    private async Task LoadSettingsBackupStatusAsync(
        string libraryRoot,
        CancellationToken cancellationToken)
    {
        var status = await settingsBackupService.GetStatusAsync(libraryRoot, cancellationToken);
        CanRestoreSettingsBackup = status.Exists && status.IsValid;
        SettingsBackupStatusText = !status.Exists
            ? text["SettingsBackupMissing"]
            : !status.IsValid
                ? text["SettingsBackupCorrupted"]
                : text.Format(
                    "SettingsBackupValidFormat",
                    status.CreatedAtUtc!.Value.ToLocalTime().ToString("g"),
                    FormatSettingsScopes(status.IncludedScopes));
    }

    private string FormatSettingsScopes(SettingsBackupScope scopes)
    {
        var names = new List<string>();
        if (scopes.HasFlag(SettingsBackupScope.CharacterAliases))
        {
            names.Add(text["SettingsScopeCharacterAliases"]);
        }

        if (scopes.HasFlag(SettingsBackupScope.CustomProfiles))
        {
            names.Add(text["SettingsScopeCustomProfiles"]);
        }

        return string.Join("、", names);
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task BackupSettingsAsync(CancellationToken cancellationToken)
    {
        var libraryRoot = await EnsureBackupLibraryAsync(cancellationToken);
        if (libraryRoot is null)
        {
            return;
        }

        if (await settingsBackupDialog.ShowBackupAsync(libraryRoot, cancellationToken))
        {
            await RefreshAsync(cancellationToken);
            StatusMessage = text["SettingsBackupSucceeded"];
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreSettings))]
    private async Task RestoreSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
        {
            return;
        }

        if (await settingsBackupDialog.ShowRestoreAsync(
                settings.SnapshotLibraryPath,
                cancellationToken))
        {
            await RefreshAsync(cancellationToken);
            StatusMessage = text["SettingsRestoreSucceeded"];
        }
    }

    private bool CanRestoreSettings() => CanRestoreSettingsBackup && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanManageAppearances))]
    private async Task ManageAppearancesAsync(CancellationToken cancellationToken)
    {
        CurrentPage = NavigationPage.Appearances;
        await LoadAppearanceManagerAsync(cancellationToken);
    }

    private async Task LoadAppearanceManagerAsync(CancellationToken cancellationToken)
    {
        if (_currentProfiles.Count == 0)
        {
            AppearanceManager = null;
            StatusMessage = text["NoAppearanceProfile"];
            return;
        }

        var libraryRoot = await EnsureBackupLibraryAsync(cancellationToken);
        if (libraryRoot is null)
        {
            AppearanceManager = null;
            StatusMessage = text["AppearanceRequiresLibrary"];
            return;
        }

        var viewModel = new AppearanceBackupsViewModel(
            _currentProfiles.Values.OrderBy(profile => profile.Name).ToArray(),
            libraryRoot,
            appearanceBackupService,
            text);
        AppearanceManager = viewModel;
        await viewModel.InitializeAsync(cancellationToken);
    }

    private bool CanManageAppearances() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanManagePortraits))]
    private async Task ManagePortraitsAsync(CancellationToken cancellationToken)
    {
        CurrentPage = NavigationPage.Portraits;
        await LoadPortraitManagerAsync(cancellationToken);
    }

    private async Task LoadPortraitManagerAsync(CancellationToken cancellationToken)
    {
        if (Characters.Count == 0)
        {
            PortraitManager = null;
            StatusMessage = text["NoPortraitCharacter"];
            return;
        }

        var libraryRoot = await EnsureBackupLibraryAsync(cancellationToken);
        if (libraryRoot is null)
        {
            PortraitManager = null;
            StatusMessage = text["PortraitRequiresLibrary"];
            return;
        }

        var characters = Characters
            .OrderBy(character => character.DisplayName)
            .ThenBy(character => character.ProfileName)
            .Select(character => new PortraitSourceOptionViewModel(
                PortraitSourceKind.Character,
                $"{character.DisplayName} · {character.ProfileName}",
                character.Character.FullPath))
            .ToArray();
        var viewModel = new PortraitManagementViewModel(
            portraitManagementService,
            libraryRoot,
            characters,
            portraitBackupEditDialog,
            text);
        PortraitManager = viewModel;
        await viewModel.InitializeAsync(cancellationToken);
    }

    private bool CanManagePortraits() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanOpenUpdateProxySettings))]
    private async Task OpenUpdateProxySettingsAsync(CancellationToken cancellationToken)
    {
        var result = await updateProxyDialog.ShowAsync(_updateProxyAddress, cancellationToken);
        if (result is null)
        {
            return;
        }

        try
        {
            var endpoint = UpdateProxyEndpoint.Parse(result.Address);
            _updateProxyAddress = await settingsService.SetUpdateProxyEndpointAsync(
                endpoint.Scheme,
                endpoint.Host,
                endpoint.Port,
                cancellationToken);
            ApplyUpdateProxy();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            UpdateProxyStatusText = text.Format(
                "SaveUpdateProxyFailedFormat",
                exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        IsUpdateBusy = true;
        IsUpdateProgressIndeterminate = true;
        UpdateProgressValue = 0;
        UpdateStatusText = text["CheckingForUpdates"];
        try
        {
            var status = await applicationUpdateService.CheckAsync(cancellationToken);
            _availableRelease = status.IsUpdateAvailable ? status.LatestRelease : null;
            IsUpdateAvailable = _availableRelease is not null;
            UpdateStatusText = _availableRelease is null
                ? text["AlreadyLatestVersion"]
                : text.Format("UpdateAvailableFormat", _availableRelease.Version.ToString(3));
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText = text["UpdateCheckCanceled"];
        }
        catch (Exception exception)
        {
            _availableRelease = null;
            IsUpdateAvailable = false;
            UpdateStatusText = text.Format("UpdateCheckFailedFormat", exception.Message);
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync(CancellationToken cancellationToken)
    {
        if (_availableRelease is null)
        {
            return;
        }

        IsUpdateBusy = true;
        IsUpdateProgressIndeterminate = true;
        UpdateProgressValue = 0;
        var version = _availableRelease.Version.ToString(3);
        UpdateStatusText = text.Format("DownloadingUpdateFormat", version);
        var progress = new Progress<ApplicationUpdateProgress>(updateProgress =>
            ReportUpdateProgress(updateProgress, version));
        try
        {
            var prepared = await applicationUpdateService.PrepareAsync(
                _availableRelease,
                progress,
                cancellationToken);
            UpdateStatusText = text["ApplyingUpdate"];
            applicationUpdateInstaller.Launch(prepared);
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText = text["UpdateCanceled"];
        }
        catch (Exception exception)
        {
            UpdateStatusText = text.Format("UpdateFailedFormat", exception.Message);
        }
        finally
        {
            IsUpdateBusy = false;
        }
    }

    private void ReportUpdateProgress(ApplicationUpdateProgress progress, string version)
    {
        if (progress.Stage == ApplicationUpdateStage.Preparing)
        {
            IsUpdateProgressIndeterminate = true;
            UpdateStatusText = text["PreparingUpdate"];
            return;
        }

        if (progress.BytesReceived == 0)
        {
            IsUpdateProgressIndeterminate = true;
            UpdateStatusText = text.Format("WaitingForUpdateDataFormat", version);
            return;
        }

        var receivedSize = FormatDownloadSize(progress.BytesReceived);
        if (progress.TotalBytes is not > 0)
        {
            IsUpdateProgressIndeterminate = true;
            UpdateStatusText = text.Format(
                "DownloadingUpdateWithoutTotalFormat",
                version,
                receivedSize);
            return;
        }

        var percentage = Math.Clamp(
            progress.BytesReceived * 100d / progress.TotalBytes.Value,
            0,
            100);
        IsUpdateProgressIndeterminate = false;
        UpdateProgressValue = percentage;
        UpdateStatusText = text.Format(
            "DownloadingUpdateProgressFormat",
            version,
            receivedSize,
            FormatDownloadSize(progress.TotalBytes.Value),
            Math.Round(percentage));
    }

    private string FormatDownloadSize(long bytes) => bytes < 1_000_000
        ? text.Format("DownloadSizeKilobytesFormat", bytes / 1_000d)
        : text.Format("DownloadSizeMegabytesFormat", bytes / 1_000_000d);

    private bool CanOpenUpdateProxySettings() => IsUpdateProxyEnabled && !IsUpdateBusy;

    private bool CanCheckForUpdates() => !IsUpdateBusy;

    private bool CanInstallUpdate() =>
        !IsUpdateBusy &&
        IsUpdateAvailable &&
        applicationUpdateInstaller.IsSupported;

    private async Task<string?> EnsureBackupLibraryAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.GetAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
        {
            return settings.SnapshotLibraryPath;
        }

        var path = await folderPicker.PickFolderAsync(
            text["SelectBackupLibraryDirectory"],
            cancellationToken);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        await settingsService.SetSnapshotLibraryPathAsync(path, cancellationToken);
        SnapshotLibraryPath = Path.GetFullPath(path);
        return SnapshotLibraryPath;
    }

    [RelayCommand(CanExecute = nameof(CanPreviewMigration))]
    private async Task PreviewMigrationAsync(CancellationToken cancellationToken)
    {
        EnsureMigrationScopeHandlers();
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
        InvalidateMigrationPreview();

    partial void OnSelectedMigrationTargetChanged(CharacterRowViewModel? value) =>
        InvalidateMigrationPreview();

    private void EnsureMigrationScopeHandlers()
    {
        if (_migrationScopeHandlersRegistered)
        {
            return;
        }

        foreach (var scope in MigrationScopes)
        {
            scope.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(MigrationScopeOptionViewModel.IsSelected))
                {
                    InvalidateMigrationPreview();
                }
            };
        }

        _migrationScopeHandlersRegistered = true;
    }

    private void InvalidateMigrationPreview()
    {
        var hadPreview = CanConfirmMigration || MigrationPreviewFiles.Count > 0;
        ClearMigrationPreview();
        if (hadPreview)
        {
            StatusMessage = text["MigrationSelectionChanged"];
        }
    }

    private void ClearMigrationPreview()
    {
        _previewedMigrationSource = null;
        _previewedMigrationTarget = null;
        _previewedMigrationScopes = ConfigScope.None;
        CanConfirmMigration = false;
        MigrationPreviewFiles.Clear();
        MigrationPreviewTitle = text["SelectCharactersForMigrationPreview"];
    }

    partial void OnSnapshotFilterChanged(string value) => ApplySnapshotFilter();

    partial void OnIsUpdateProxyEnabledChanged(bool oldValue, bool newValue)
    {
        if (_isApplyingUpdateProxySettings)
        {
            return;
        }

        ApplyUpdateProxy();
        _ = SaveUpdateProxyEnabledAsync(oldValue, newValue);
    }

    private async Task SaveUpdateProxyEnabledAsync(bool oldValue, bool newValue)
    {
        try
        {
            await settingsService.SetUpdateProxyEnabledAsync(newValue);
        }
        catch (Exception exception)
        {
            _isApplyingUpdateProxySettings = true;
            IsUpdateProxyEnabled = oldValue;
            _isApplyingUpdateProxySettings = false;
            ApplyUpdateProxy();
            UpdateProxyStatusText = text.Format(
                "SaveUpdateProxyFailedFormat",
                exception.Message);
        }
    }

    private void ApplyUpdateProxy()
    {
        applicationUpdateProxy.Configure(
            IsUpdateProxyEnabled ? _updateProxyAddress : null);
        UpdateProxyStatusText = (IsUpdateProxyEnabled, _updateProxyAddress) switch
        {
            (true, not null) => text.Format("UpdateProxyEnabledFormat", _updateProxyAddress),
            (true, null) => text["UpdateProxyNeedsSettings"],
            (false, not null) => text.Format("UpdateProxyDisabledWithSavedFormat", _updateProxyAddress),
            _ => text["UpdateProxyDisabled"],
        };
    }

    partial void OnShowOnlyTaggedCharactersChanged(bool value)
    {
        ApplyCharacterFilter();
        if (!_isApplyingSettings)
        {
            _ = SaveCharacterFilterAsync(value);
        }
    }

    private void ApplyCharacterFilter()
    {
        VisibleCharacters.Clear();
        foreach (var character in Characters.Where(character =>
                     !ShowOnlyTaggedCharacters || !string.IsNullOrWhiteSpace(character.Alias)))
        {
            VisibleCharacters.Add(character);
        }
    }

    private async Task SaveCharacterFilterAsync(bool value)
    {
        try
        {
            await settingsService.SetShowOnlyTaggedCharactersAsync(value);
        }
        catch (Exception exception)
        {
            StatusMessage = text.Format("SaveCharacterFilterFailedFormat", exception.Message);
        }
    }

    private void ApplySnapshotFilter()
    {
        var filter = SnapshotFilter.Trim();
        BackupGroups.Clear();
        foreach (var group in _allBackupGroups.Where(item =>
                     filter.Length == 0 ||
                     item.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)))
        {
            BackupGroups.Add(group);
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
            ApplyCharacterFilter();
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
    Appearances,
    Portraits,
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
    private readonly Func<GameProfile, CharacterConfiguration, Task> _manageBackups;

    private CharacterRowViewModel(
        GameProfile profile,
        CharacterConfiguration character,
        string? alias,
        Func<Guid, CharacterFolderName, string, Task> saveAlias,
        Func<GameProfile, CharacterConfiguration, Task> createSnapshot,
        Func<GameProfile, CharacterConfiguration, Task> manageBackups)
    {
        _profileId = profile.Id;
        _characterFolder = character.FolderName;
        _profile = profile;
        _character = character;
        _saveAlias = saveAlias;
        _createSnapshot = createSnapshot;
        _manageBackups = manageBackups;
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
    public partial string BackupStatus { get; private set; } =
        ResourceTextLocalizer.Instance["NoCharacterBackups"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    public partial string Alias { get; set; }

    public static CharacterRowViewModel From(
        GameProfile profile,
        CharacterConfiguration character,
        string? alias,
        Func<Guid, CharacterFolderName, string, Task> saveAlias,
        Func<GameProfile, CharacterConfiguration, Task> createSnapshot,
        Func<GameProfile, CharacterConfiguration, Task> manageBackups) =>
        new(profile, character, alias, saveAlias, createSnapshot, manageBackups);

    [RelayCommand]
    private Task SaveAliasAsync() => _saveAlias(_profileId, _characterFolder, Alias);

    [RelayCommand]
    private Task CreateSnapshotAsync() => _createSnapshot(_profile, _character);

    [RelayCommand]
    private Task ManageBackupsAsync() => _manageBackups(_profile, _character);

    public void SetBackups(IReadOnlyList<SnapshotLibraryEntry> backups)
    {
        if (backups.Count == 0)
        {
            BackupStatus = ResourceTextLocalizer.Instance["NoCharacterBackups"];
            return;
        }

        var valid = backups.Count(item => item.IntegrityStatus == SnapshotIntegrityStatus.Valid);
        var corrupted = backups.Count - valid;
        var latest = backups.Max(item =>
            item.Manifest?.CreatedAtUtc ?? item.ArchiveLastWriteTimeUtc);
        BackupStatus = ResourceTextLocalizer.Instance.Format(
            "CharacterBackupStatusFormat",
            backups.Count,
            valid,
            corrupted,
            latest.ToLocalTime().ToString("g"));
    }
}

public sealed partial class CharacterBackupGroupViewModel : ObservableObject
{
    private readonly Func<Task> _open;

    private CharacterBackupGroupViewModel(
        string characterName,
        string profileName,
        IReadOnlyList<SnapshotLibraryEntry> backups,
        Func<Task> open)
    {
        CharacterName = characterName;
        ProfileName = profileName;
        _open = open;
        var valid = backups.Count(item => item.IntegrityStatus == SnapshotIntegrityStatus.Valid);
        var corrupted = backups.Count - valid;
        var latest = backups.Max(item =>
            item.Manifest?.CreatedAtUtc ?? item.ArchiveLastWriteTimeUtc);
        Status = ResourceTextLocalizer.Instance.Format(
            "CharacterBackupStatusFormat",
            backups.Count,
            valid,
            corrupted,
            latest.ToLocalTime().ToString("g"));
        SearchText = $"{characterName} {profileName} {Status}";
    }

    public string CharacterName { get; }

    public string ProfileName { get; }

    public string Status { get; }

    public string SearchText { get; }

    public static CharacterBackupGroupViewModel Create(
        string characterName,
        string profileName,
        IReadOnlyList<SnapshotLibraryEntry> backups,
        Func<Task> open) =>
        new(characterName, profileName, backups, open);

    [RelayCommand]
    private Task OpenAsync() => _open();
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
