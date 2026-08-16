using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Appearances;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Domain.Appearances;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Desktop.ViewModels;

public sealed partial class AppearanceBackupsViewModel : ViewModelBase
{
    private readonly IAppearanceBackupService _service;
    private readonly string _libraryRoot;
    private readonly ITextLocalizer _text;
    private readonly Dictionary<Guid, IReadOnlyList<AppearanceSlot>> _profileSlots = [];
    private readonly List<AppearanceListItemViewModel> _allRightItems = [];
    private int _leftLoadVersion;
    private int _rightLoadVersion;

    public AppearanceBackupsViewModel(
        IReadOnlyList<GameProfile> profiles,
        string libraryRoot,
        IAppearanceBackupService service,
        ITextLocalizer text)
    {
        _service = service;
        _libraryRoot = libraryRoot;
        _text = text;
        Profiles = profiles.Select(AppearanceProfileOptionViewModel.From).ToArray();
        RightSources =
        [
            new AppearanceRightSourceOptionViewModel(null, text["AppearanceBackupArea"]),
            .. Profiles.Select(profile => new AppearanceRightSourceOptionViewModel(profile, profile.DisplayName)),
        ];
        RaceFilters =
        [
            new(null, text["AllRaces"]),
            .. Enum.GetValues<AppearanceRace>()
                .Select(race => new AppearanceRaceFilterViewModel(race, AppearanceText.Race(race, text))),
        ];
        GenderFilters =
        [
            new(null, text["AllGenders"]),
            new(AppearanceGender.Male, text["GenderMale"]),
            new(AppearanceGender.Female, text["GenderFemale"]),
        ];
        SelectedRaceFilter = RaceFilters[0];
        SelectedGenderFilter = GenderFilters[0];
        SelectedLeftProfile = Profiles.FirstOrDefault();
        SelectedRightSource = RightSources.FirstOrDefault();
        StatusMessage = text["AppearanceDualListHint"];
    }

    public IReadOnlyList<AppearanceProfileOptionViewModel> Profiles { get; }

    public IReadOnlyList<AppearanceRightSourceOptionViewModel> RightSources { get; }

    public IReadOnlyList<AppearanceRaceFilterViewModel> RaceFilters { get; }

    public IReadOnlyList<AppearanceGenderFilterViewModel> GenderFilters { get; }

    public ObservableCollection<AppearanceListItemViewModel> LeftItems { get; } = [];

    public ObservableCollection<AppearanceListItemViewModel> RightItems { get; } = [];

    public bool Changed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftPanelTitle))]
    [NotifyPropertyChangedFor(nameof(RightPanelTitle))]
    [NotifyPropertyChangedFor(nameof(OperationName))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial AppearanceProfileOptionViewModel? SelectedLeftProfile { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRightBackupArea))]
    [NotifyPropertyChangedFor(nameof(IsBackupOperation))]
    [NotifyPropertyChangedFor(nameof(OperationName))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBackupCommand))]
    public partial AppearanceRightSourceOptionViewModel? SelectedRightSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial AppearanceListItemViewModel? SelectedLeftItem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBackupCommand))]
    public partial AppearanceListItemViewModel? SelectedRightItem { get; set; }

    [ObservableProperty]
    public partial AppearanceRaceFilterViewModel SelectedRaceFilter { get; set; }

    [ObservableProperty]
    public partial AppearanceGenderFilterViewModel SelectedGenderFilter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationName))]
    [NotifyPropertyChangedFor(nameof(DirectionSymbol))]
    [NotifyPropertyChangedFor(nameof(LeftPanelTitle))]
    [NotifyPropertyChangedFor(nameof(RightPanelTitle))]
    [NotifyPropertyChangedFor(nameof(IsBackupOperation))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial bool IsDirectionReversed { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapDirectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationName))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial bool IsOverwriteArmed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonText))]
    public partial bool IsDeleteArmed { get; private set; }

    public bool IsRightBackupArea => SelectedRightSource?.IsBackupArea == true;

    public bool IsBackupOperation => GetOperationKind() == AppearanceOperationKind.Backup;

    public string DirectionSymbol => IsDirectionReversed ? "←" : "→";

    public string LeftPanelTitle => IsDirectionReversed
        ? _text["AppearanceTarget"]
        : _text["AppearanceSource"];

    public string RightPanelTitle => IsDirectionReversed
        ? _text["AppearanceSource"]
        : _text["AppearanceTarget"];

    public string DeleteButtonText => IsDeleteArmed
        ? _text["ConfirmDeleteAppearanceBackup"]
        : _text["DeleteAppearanceBackup"];

    public string OperationName
    {
        get
        {
            var label = GetOperationKind() switch
            {
                AppearanceOperationKind.Backup => _text["BackupSelectedAppearance"],
                AppearanceOperationKind.Restore => IsOverwriteArmed
                    ? _text["ConfirmOverwriteAppearance"]
                    : _text["RestoreSelectedAppearance"],
                AppearanceOperationKind.Migrate => IsOverwriteArmed
                    ? _text["ConfirmOverwriteAppearance"]
                    : _text["MigrateSelectedAppearance"],
                _ => _text["SelectAppearanceOperation"],
            };
            return GetOperationKind() == AppearanceOperationKind.None || IsOverwriteArmed
                ? label
                : IsDirectionReversed ? $"← {label}" : $"{label} →";
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            await Task.WhenAll(
                ReloadLeftAsync(cancellationToken),
                ReloadRightAsync(cancellationToken));
            StatusMessage = _text["AppearanceDualListHint"];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = _text.Format("LoadAppearanceDataFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedLeftProfileChanged(AppearanceProfileOptionViewModel? value)
    {
        SelectedLeftItem = null;
        CancelConfirmations();
        _ = ReloadLeftAsync();
    }

    partial void OnSelectedRightSourceChanged(AppearanceRightSourceOptionViewModel? value)
    {
        SelectedRightItem = null;
        CancelConfirmations();
        _ = ReloadRightAsync();
    }

    partial void OnSelectedLeftItemChanged(AppearanceListItemViewModel? value) =>
        CancelOverwriteConfirmation();

    partial void OnSelectedRightItemChanged(AppearanceListItemViewModel? value)
    {
        CancelOverwriteConfirmation();
        CancelDeleteConfirmation();
    }

    partial void OnSelectedRaceFilterChanged(AppearanceRaceFilterViewModel value)
    {
        CancelDeleteConfirmation();
        ApplyRightFilters();
    }

    partial void OnSelectedGenderFilterChanged(AppearanceGenderFilterViewModel value)
    {
        CancelDeleteConfirmation();
        ApplyRightFilters();
    }

    partial void OnSearchTextChanged(string value)
    {
        CancelDeleteConfirmation();
        ApplyRightFilters();
    }

    [RelayCommand(CanExecute = nameof(CanSwapDirection))]
    private void SwapDirection()
    {
        IsDirectionReversed = !IsDirectionReversed;
        CancelConfirmations();
        ExecuteOperationCommand.NotifyCanExecuteChanged();
    }

    private bool CanSwapDirection() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            _profileSlots.Clear();
            await Task.WhenAll(
                ReloadLeftAsync(cancellationToken),
                ReloadRightAsync(cancellationToken));
            StatusMessage = _text["AppearanceDualListHint"];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = _text.Format("LoadAppearanceDataFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRefresh() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExecuteOperation))]
    private async Task ExecuteOperationAsync(CancellationToken cancellationToken)
    {
        var kind = GetOperationKind();
        var target = GetTargetItem();
        if (kind is AppearanceOperationKind.Restore or AppearanceOperationKind.Migrate &&
            target?.IsOccupied == true && !IsOverwriteArmed)
        {
            IsOverwriteArmed = true;
            StatusMessage = _text["OverwriteAppearanceWarning"];
            return;
        }

        IsBusy = true;
        ResetConfirmations();
        try
        {
            switch (kind)
            {
                case AppearanceOperationKind.Backup:
                    await BackupAsync(GetSourceItem()!, cancellationToken);
                    break;
                case AppearanceOperationKind.Restore:
                    await RestoreAsync(GetSourceItem()!, target!, cancellationToken);
                    break;
                case AppearanceOperationKind.Migrate:
                    await MigrateAsync(GetSourceItem()!, target!, cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException(_text["AppearanceOperationUnavailable"]);
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _text["AppearanceRestoreCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("AppearanceOperationFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ResetConfirmations();
        }
    }

    private async Task BackupAsync(
        AppearanceListItemViewModel source,
        CancellationToken cancellationToken)
    {
        await _service.CreateBackupAsync(source.Slot!.FilePath, _libraryRoot, cancellationToken: cancellationToken);
        Changed = true;
        await ReloadRightAsync(cancellationToken);
        StatusMessage = _text["AppearanceBackupCreated"];
    }

    private async Task RestoreAsync(
        AppearanceListItemViewModel source,
        AppearanceListItemViewModel target,
        CancellationToken cancellationToken)
    {
        var targetProfile = SelectedLeftProfile!.Profile;
        var result = await _service.RestoreAsync(
            source.Backup!.Entry,
            targetProfile.ConfigRoot,
            target.SlotNumber,
            _libraryRoot,
            cancellationToken);
        Changed = true;
        _profileSlots.Remove(targetProfile.Id);
        await ReloadLeftAsync(cancellationToken);
        StatusMessage = result.RecoveryPoint is null
            ? _text.Format("AppearanceRestoredFormat", target.SlotNumber)
            : _text.Format("AppearanceRestoredWithRecoveryFormat", target.SlotNumber);
    }

    private async Task MigrateAsync(
        AppearanceListItemViewModel source,
        AppearanceListItemViewModel target,
        CancellationToken cancellationToken)
    {
        var targetProfile = GetTargetProfile()!;
        var sourceBackup = await _service.CreateBackupAsync(
            source.Slot!.FilePath,
            _libraryRoot,
            cancellationToken: cancellationToken);
        var result = await _service.RestoreAsync(
            sourceBackup,
            targetProfile.Profile.ConfigRoot,
            target.SlotNumber,
            _libraryRoot,
            cancellationToken);
        Changed = true;
        _profileSlots.Remove(targetProfile.Profile.Id);
        await ReloadTargetSideAsync(cancellationToken);
        StatusMessage = result.RecoveryPoint is null
            ? _text.Format("AppearanceMigratedFormat", target.SlotNumber)
            : _text.Format("AppearanceMigratedWithRecoveryFormat", target.SlotNumber);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteBackup))]
    private async Task DeleteBackupAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedRightItem?.Backup
            ?? throw new InvalidOperationException(_text["AppearanceBackupNotSelected"]);
        if (!IsDeleteArmed)
        {
            IsDeleteArmed = true;
            StatusMessage = _text["DeleteAppearanceBackupWarning"];
            return;
        }

        IsBusy = true;
        try
        {
            await _service.DeleteAsync(selected.Entry.ArchivePath, cancellationToken);
            Changed = true;
            IsDeleteArmed = false;
            SelectedRightItem = null;
            await ReloadRightAsync(cancellationToken);
            StatusMessage = _text["AppearanceBackupDeleted"];
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = _text.Format("AppearanceBackupDeleteFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
            IsDeleteArmed = false;
        }
    }

    private bool CanDeleteBackup() =>
        !IsBusy && IsRightBackupArea && SelectedRightItem?.Backup is not null;

    private bool CanExecuteOperation()
    {
        if (IsBusy || SelectedLeftProfile is null || SelectedRightSource is null ||
            GetOperationKind() == AppearanceOperationKind.None || GetSourceItem()?.CanBeSource != true)
        {
            return false;
        }

        return GetOperationKind() == AppearanceOperationKind.Backup ||
               GetTargetItem()?.CanBeTarget == true;
    }

    private AppearanceOperationKind GetOperationKind()
    {
        if (SelectedLeftProfile is null || SelectedRightSource is null)
        {
            return AppearanceOperationKind.None;
        }

        if (SelectedRightSource.IsBackupArea)
        {
            return IsDirectionReversed
                ? AppearanceOperationKind.Restore
                : AppearanceOperationKind.Backup;
        }

        if (SelectedRightSource.Profile?.Profile.Id == SelectedLeftProfile.Profile.Id)
        {
            return AppearanceOperationKind.None;
        }

        return AppearanceOperationKind.Migrate;
    }

    private AppearanceListItemViewModel? GetSourceItem() =>
        IsDirectionReversed ? SelectedRightItem : SelectedLeftItem;

    private AppearanceListItemViewModel? GetTargetItem() =>
        IsDirectionReversed ? SelectedLeftItem : SelectedRightItem;

    private AppearanceProfileOptionViewModel? GetTargetProfile() =>
        IsDirectionReversed ? SelectedLeftProfile : SelectedRightSource?.Profile;

    private Task ReloadTargetSideAsync(CancellationToken cancellationToken) =>
        IsDirectionReversed
            ? ReloadLeftAsync(cancellationToken)
            : ReloadRightAsync(cancellationToken);

    private async Task ReloadLeftAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _leftLoadVersion);
        var profile = SelectedLeftProfile;
        var items = profile is null
            ? []
            : await LoadProfileItemsAsync(profile, cancellationToken);
        if (version != _leftLoadVersion || profile != SelectedLeftProfile)
        {
            return;
        }

        ReplaceItems(LeftItems, items);
    }

    private async Task ReloadRightAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _rightLoadVersion);
        var source = SelectedRightSource;
        IReadOnlyList<AppearanceListItemViewModel> items;
        if (source is null)
        {
            items = [];
        }
        else if (source.IsBackupArea)
        {
            var entries = await _service.ScanBackupsAsync(_libraryRoot, cancellationToken);
            items = entries.Select(entry => AppearanceListItemViewModel.FromBackup(entry, _text)).ToArray();
        }
        else
        {
            items = await LoadProfileItemsAsync(source.Profile!, cancellationToken);
        }

        if (version != _rightLoadVersion || source != SelectedRightSource)
        {
            return;
        }

        _allRightItems.Clear();
        _allRightItems.AddRange(items);
        ApplyRightFilters();
    }

    private async Task<IReadOnlyList<AppearanceListItemViewModel>> LoadProfileItemsAsync(
        AppearanceProfileOptionViewModel profile,
        CancellationToken cancellationToken)
    {
        if (!_profileSlots.TryGetValue(profile.Profile.Id, out var existingSlots))
        {
            existingSlots = await _service.ScanSlotsAsync(profile.Profile.ConfigRoot, cancellationToken);
            _profileSlots[profile.Profile.Id] = existingSlots;
        }

        var bySlot = existingSlots.ToDictionary(slot => slot.Slot);
        return Enumerable.Range(1, AppearanceData.MaximumSlot)
            .Select(slot => bySlot.TryGetValue(slot, out var existing)
                ? AppearanceListItemViewModel.FromSlot(existing, isOccupied: true, _text)
                : AppearanceListItemViewModel.FromSlot(
                    new AppearanceSlot(
                        slot,
                        Path.Combine(profile.Profile.ConfigRoot, AppearanceData.GetSlotFileName(slot)),
                        null,
                        null),
                    isOccupied: false,
                    _text))
            .ToArray();
    }

    private void ApplyRightFilters()
    {
        IReadOnlyList<AppearanceListItemViewModel> matches = _allRightItems;
        if (IsRightBackupArea)
        {
            var race = SelectedRaceFilter?.Race;
            var gender = SelectedGenderFilter?.Gender;
            matches = _allRightItems.Where(item =>
                    AppearanceBackupFilter.Matches(
                        item.Backup?.Entry.Manifest?.Appearance,
                        race,
                        gender,
                        SearchText))
                .ToArray();
        }

        if (SelectedRightItem is not null && !matches.Contains(SelectedRightItem))
        {
            SelectedRightItem = null;
        }

        ReplaceItems(RightItems, matches);
    }

    private static void ReplaceItems(
        ObservableCollection<AppearanceListItemViewModel> target,
        IReadOnlyList<AppearanceListItemViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void CancelOverwriteConfirmation()
    {
        if (!IsOverwriteArmed)
        {
            return;
        }

        IsOverwriteArmed = false;
        StatusMessage = _text["AppearanceDualListHint"];
    }

    private void CancelDeleteConfirmation()
    {
        if (!IsDeleteArmed)
        {
            return;
        }

        IsDeleteArmed = false;
        StatusMessage = _text["AppearanceDualListHint"];
    }

    private void CancelConfirmations()
    {
        var hadConfirmation = IsOverwriteArmed || IsDeleteArmed;
        ResetConfirmations();
        if (hadConfirmation)
        {
            StatusMessage = _text["AppearanceDualListHint"];
        }
    }

    private void ResetConfirmations()
    {
        IsOverwriteArmed = false;
        IsDeleteArmed = false;
    }

    private enum AppearanceOperationKind
    {
        None,
        Backup,
        Restore,
        Migrate,
    }
}

public sealed record AppearanceProfileOptionViewModel(GameProfile Profile, string DisplayName)
{
    public static AppearanceProfileOptionViewModel From(GameProfile profile) =>
        new(profile, $"{profile.Name} · {profile.ConfigRoot}");
}

public sealed record AppearanceRightSourceOptionViewModel(
    AppearanceProfileOptionViewModel? Profile,
    string DisplayName)
{
    public bool IsBackupArea => Profile is null;
}

public sealed record AppearanceRaceFilterViewModel(AppearanceRace? Race, string DisplayName);

public sealed record AppearanceGenderFilterViewModel(AppearanceGender? Gender, string DisplayName);

public sealed record AppearanceListItemViewModel(
    AppearanceSlot? Slot,
    AppearanceBackupItemViewModel? Backup,
    int SlotNumber,
    string Title,
    string Summary,
    string SecondaryText,
    bool IsOccupied,
    bool CanBeSource,
    bool CanBeTarget)
{
    public bool IsSlot => Slot is not null;

    public bool IsBackup => Backup is not null;

    public static AppearanceListItemViewModel FromSlot(
        AppearanceSlot slot,
        bool isOccupied,
        ITextLocalizer text)
    {
        var isValid = slot.Appearance is not null;
        var summary = !isOccupied
            ? text["AppearanceEmptySlot"]
            : isValid
                ? AppearanceText.Summary(slot.Appearance!, text)
                : text.Format("AppearanceTargetUnreadableFormat", slot.Error ?? text["UnknownAppearance"]);
        return new AppearanceListItemViewModel(
            slot,
            null,
            slot.Slot,
            text.Format("AppearanceSlotFormat", slot.Slot),
            summary,
            isOccupied ? text["AppearanceSlotOccupied"] : text["AppearanceSlotEmpty"],
            isOccupied,
            isValid,
            !isOccupied || isValid);
    }

    public static AppearanceListItemViewModel FromBackup(
        AppearanceBackupEntry entry,
        ITextLocalizer text)
    {
        var backup = AppearanceBackupItemViewModel.From(entry, text);
        return new AppearanceListItemViewModel(
            null,
            backup,
            0,
            $"{backup.RaceText} · {backup.GenderText}",
            backup.Comment,
            $"{backup.CreatedAt} · {backup.Reason}",
            true,
            backup.IsValid,
            false);
    }
}

public sealed record AppearanceBackupItemViewModel(
    AppearanceBackupEntry Entry,
    AppearanceRace? Race,
    AppearanceGender? Gender,
    string RaceText,
    string GenderText,
    string Comment,
    string CreatedAt,
    string Reason,
    string Details,
    bool IsValid)
{
    public static AppearanceBackupItemViewModel From(
        AppearanceBackupEntry entry,
        ITextLocalizer text)
    {
        var appearance = entry.Manifest?.Appearance;
        return new AppearanceBackupItemViewModel(
            entry,
            appearance?.Race,
            appearance?.Gender,
            appearance is null ? text["UnknownAppearance"] : AppearanceText.Race(appearance.Race, text),
            appearance is null ? "—" : AppearanceText.Gender(appearance.Gender, text),
            appearance is null || string.IsNullOrWhiteSpace(appearance.Comment)
                ? text["NoAppearanceComment"]
                : appearance.Comment,
            (entry.Manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc).ToLocalTime().ToString("g"),
            entry.Manifest?.Reason == AppearanceBackupReason.BeforeRestore
                ? text["AppearanceReasonBeforeRestore"]
                : text["AppearanceReasonManual"],
            entry.Errors.Count == 0 ? string.Empty : string.Join("；", entry.Errors),
            entry.Integrity == AppearanceBackupIntegrity.Valid);
    }
}

public static class AppearanceText
{
    public static string Summary(AppearanceMetadata appearance, ITextLocalizer text)
    {
        var comment = string.IsNullOrWhiteSpace(appearance.Comment)
            ? text["NoAppearanceComment"]
            : appearance.Comment;
        return $"{Race(appearance.Race, text)} · {Gender(appearance.Gender, text)} · {comment}";
    }

    public static string Race(AppearanceRace race, ITextLocalizer text) => race switch
    {
        AppearanceRace.Hyur => text["RaceHyur"],
        AppearanceRace.Elezen => text["RaceElezen"],
        AppearanceRace.Lalafell => text["RaceLalafell"],
        AppearanceRace.Miqote => text["RaceMiqote"],
        AppearanceRace.Roegadyn => text["RaceRoegadyn"],
        AppearanceRace.AuRa => text["RaceAuRa"],
        AppearanceRace.Hrothgar => text["RaceHrothgar"],
        AppearanceRace.Viera => text["RaceViera"],
        _ => text["UnknownAppearance"],
    };

    public static string Gender(AppearanceGender gender, ITextLocalizer text) =>
        gender == AppearanceGender.Male ? text["GenderMale"] : text["GenderFemale"];
}
