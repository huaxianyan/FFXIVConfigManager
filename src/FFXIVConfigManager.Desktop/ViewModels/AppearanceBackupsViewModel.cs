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
    private readonly List<AppearanceBackupItemViewModel> _allBackups = [];
    private readonly Dictionary<Guid, IReadOnlyList<AppearanceSlot>> _profileSlots = [];

    public AppearanceBackupsViewModel(
        IReadOnlyList<GameProfile> profiles,
        string libraryRoot,
        IAppearanceBackupService service,
        ITextLocalizer text)
    {
        _service = service;
        _libraryRoot = libraryRoot;
        _text = text;
        IsBusy = true;
        Profiles = profiles.Select(AppearanceProfileOptionViewModel.From).ToArray();
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
        TargetSlots = Enumerable.Range(1, AppearanceData.MaximumSlot)
            .Select(slot => new AppearanceTargetSlotViewModel(slot, text.Format("AppearanceSlotFormat", slot)))
            .ToArray();
        SelectedRaceFilter = RaceFilters[0];
        SelectedGenderFilter = GenderFilters[0];
        SelectedSourceProfile = Profiles.FirstOrDefault();
        SelectedTargetProfile = Profiles.FirstOrDefault();
        SelectedTargetSlot = TargetSlots[0];
        StatusMessage = text["LoadingAppearanceData"];
        IsBusy = false;
    }

    public IReadOnlyList<AppearanceProfileOptionViewModel> Profiles { get; }

    public IReadOnlyList<AppearanceRaceFilterViewModel> RaceFilters { get; }

    public IReadOnlyList<AppearanceGenderFilterViewModel> GenderFilters { get; }

    public IReadOnlyList<AppearanceTargetSlotViewModel> TargetSlots { get; }

    public ObservableCollection<AppearanceSourceSlotViewModel> SourceSlots { get; } = [];

    public ObservableCollection<AppearanceBackupItemViewModel> Backups { get; } = [];

    public bool Changed { get; private set; }

    [ObservableProperty]
    public partial AppearanceProfileOptionViewModel? SelectedSourceProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial AppearanceProfileOptionViewModel? SelectedTargetProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial AppearanceTargetSlotViewModel? SelectedTargetSlot { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial AppearanceBackupItemViewModel? SelectedBackup { get; set; }

    [ObservableProperty]
    public partial AppearanceRaceFilterViewModel SelectedRaceFilter { get; set; }

    [ObservableProperty]
    public partial AppearanceGenderFilterViewModel SelectedGenderFilter { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; }

    [ObservableProperty]
    public partial string TargetPreview { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RestoreButtonText))]
    public partial bool IsOverwriteArmed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonText))]
    public partial bool IsDeleteArmed { get; private set; }

    public string RestoreButtonText => IsOverwriteArmed
        ? _text["ConfirmOverwriteAppearance"]
        : _text["RestoreToSelectedSlot"];

    public string DeleteButtonText => IsDeleteArmed
        ? _text["ConfirmDeleteAppearanceBackup"]
        : _text["DeleteAppearanceBackup"];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            await LoadSourceSlotsAsync(cancellationToken);
            await EnsureTargetSlotsAsync(cancellationToken);
            await ReloadBackupsAsync(cancellationToken);
            StatusMessage = _text.Format("AppearanceBackupCountFormat", _allBackups.Count);
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

    partial void OnSelectedSourceProfileChanged(AppearanceProfileOptionViewModel? value)
    {
        if (!IsBusy)
        {
            _ = LoadSourceSlotsSafelyAsync();
        }
    }

    partial void OnSelectedTargetProfileChanged(AppearanceProfileOptionViewModel? value)
    {
        ResetRestoreConfirmation();
        if (!IsBusy)
        {
            _ = UpdateTargetPreviewSafelyAsync();
        }
    }

    partial void OnSelectedTargetSlotChanged(AppearanceTargetSlotViewModel? value)
    {
        ResetRestoreConfirmation();
        if (!IsBusy)
        {
            _ = UpdateTargetPreviewSafelyAsync();
        }
    }

    partial void OnSelectedBackupChanged(AppearanceBackupItemViewModel? value)
    {
        ResetRestoreConfirmation();
        IsDeleteArmed = false;
    }

    partial void OnSelectedRaceFilterChanged(AppearanceRaceFilterViewModel value) => ApplyFilters();

    partial void OnSelectedGenderFilterChanged(AppearanceGenderFilterViewModel value) => ApplyFilters();

    partial void OnSearchTextChanged(string value) => ApplyFilters();

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var target = SelectedTargetProfile!;
        var slot = SelectedTargetSlot!.Slot;
        var occupied = await GetTargetSlotAsync(target.Profile, slot, cancellationToken) is not null;
        if (occupied && !IsOverwriteArmed)
        {
            IsOverwriteArmed = true;
            StatusMessage = _text["OverwriteAppearanceWarning"];
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _service.RestoreAsync(
                SelectedBackup!.Entry,
                target.Profile.ConfigRoot,
                slot,
                _libraryRoot,
                cancellationToken);
            Changed = true;
            _profileSlots.Remove(target.Profile.Id);
            await EnsureTargetSlotsAsync(cancellationToken);
            await ReloadBackupsAsync(cancellationToken);
            StatusMessage = result.RecoveryPoint is null
                ? _text.Format("AppearanceRestoredFormat", slot)
                : _text.Format("AppearanceRestoredWithRecoveryFormat", slot);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _text["AppearanceRestoreCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("AppearanceRestoreFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
            ResetRestoreConfirmation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (!IsDeleteArmed)
        {
            IsDeleteArmed = true;
            return;
        }

        IsBusy = true;
        try
        {
            await _service.DeleteAsync(SelectedBackup!.Entry.ArchivePath, cancellationToken);
            Changed = true;
            await ReloadBackupsAsync(cancellationToken);
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

    private async Task CreateBackupAsync(AppearanceSlot slot)
    {
        IsBusy = true;
        try
        {
            await _service.CreateBackupAsync(slot.FilePath, _libraryRoot);
            Changed = true;
            await ReloadBackupsAsync(CancellationToken.None);
            StatusMessage = _text["AppearanceBackupCreated"];
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("AppearanceBackupCreateFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSourceSlotsAsync(CancellationToken cancellationToken)
    {
        SourceSlots.Clear();
        var selectedProfile = SelectedSourceProfile;
        if (selectedProfile is null)
        {
            return;
        }

        var slots = await GetProfileSlotsAsync(selectedProfile.Profile, cancellationToken);
        if (SelectedSourceProfile != selectedProfile)
        {
            return;
        }

        foreach (var slot in slots.Where(item => item.Appearance is not null))
        {
            SourceSlots.Add(new AppearanceSourceSlotViewModel(
                slot,
                AppearanceText.Summary(slot.Appearance!, _text),
                _text.Format("AppearanceSlotFormat", slot.Slot),
                () => CreateBackupAsync(slot)));
        }
    }

    private async Task ReloadBackupsAsync(CancellationToken cancellationToken)
    {
        var entries = await _service.ScanBackupsAsync(_libraryRoot, cancellationToken);
        _allBackups.Clear();
        _allBackups.AddRange(entries.Select(entry => AppearanceBackupItemViewModel.From(entry, _text)));
        SelectedBackup = null;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var race = SelectedRaceFilter?.Race;
        var gender = SelectedGenderFilter?.Gender;
        var matches = _allBackups.Where(item =>
                AppearanceBackupFilter.Matches(
                    item.Entry.Manifest?.Appearance,
                    race,
                    gender,
                    SearchText))
            .ToArray();
        if (SelectedBackup is not null && !matches.Contains(SelectedBackup))
        {
            SelectedBackup = null;
        }

        Backups.Clear();
        foreach (var backup in matches)
        {
            Backups.Add(backup);
        }
    }

    private async Task EnsureTargetSlotsAsync(CancellationToken cancellationToken)
    {
        if (SelectedTargetProfile is not null)
        {
            await GetProfileSlotsAsync(SelectedTargetProfile.Profile, cancellationToken);
        }

        UpdateTargetPreview();
    }

    private async Task<AppearanceSlot?> GetTargetSlotAsync(
        GameProfile profile,
        int slot,
        CancellationToken cancellationToken) =>
        (await GetProfileSlotsAsync(profile, cancellationToken))
        .FirstOrDefault(item => item.Slot == slot);

    private async Task<IReadOnlyList<AppearanceSlot>> GetProfileSlotsAsync(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        if (_profileSlots.TryGetValue(profile.Id, out var cached))
        {
            return cached;
        }

        var slots = await _service.ScanSlotsAsync(profile.ConfigRoot, cancellationToken);
        _profileSlots[profile.Id] = slots;
        return slots;
    }

    private void UpdateTargetPreview()
    {
        if (SelectedTargetProfile is null || SelectedTargetSlot is null)
        {
            TargetPreview = _text["SelectAppearanceTarget"];
            return;
        }

        _profileSlots.TryGetValue(SelectedTargetProfile.Profile.Id, out var slots);
        var occupied = slots?.FirstOrDefault(item => item.Slot == SelectedTargetSlot.Slot);
        TargetPreview = occupied is null
            ? _text["AppearanceTargetEmpty"]
            : occupied.Appearance is null
                ? _text.Format("AppearanceTargetUnreadableFormat", occupied.Error ?? _text["UnknownAppearance"])
                : _text.Format(
                    "AppearanceTargetOccupiedFormat",
                    AppearanceText.Summary(occupied.Appearance, _text));
    }

    private async Task LoadSourceSlotsSafelyAsync()
    {
        try
        {
            await LoadSourceSlotsAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("LoadAppearanceDataFailedFormat", exception.Message);
        }
    }

    private async Task UpdateTargetPreviewSafelyAsync()
    {
        try
        {
            await EnsureTargetSlotsAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            TargetPreview = _text.Format("LoadAppearanceDataFailedFormat", exception.Message);
        }
    }

    private void ResetRestoreConfirmation() => IsOverwriteArmed = false;

    private bool CanRestore() =>
        !IsBusy &&
        SelectedBackup?.IsValid == true &&
        SelectedTargetProfile is not null &&
        SelectedTargetSlot is not null;

    private bool CanDelete() => !IsBusy && SelectedBackup is not null;
}

public sealed record AppearanceProfileOptionViewModel(GameProfile Profile, string DisplayName)
{
    public static AppearanceProfileOptionViewModel From(GameProfile profile) =>
        new(profile, $"{profile.Name} · {profile.ConfigRoot}");
}

public sealed record AppearanceRaceFilterViewModel(AppearanceRace? Race, string DisplayName);

public sealed record AppearanceGenderFilterViewModel(AppearanceGender? Gender, string DisplayName);

public sealed record AppearanceTargetSlotViewModel(int Slot, string DisplayName);

public sealed partial class AppearanceSourceSlotViewModel(
    AppearanceSlot slot,
    string summary,
    string slotText,
    Func<Task> createBackup) : ObservableObject
{
    public AppearanceSlot Slot { get; } = slot;

    public string Summary { get; } = summary;

    public string SlotText { get; } = slotText;

    [RelayCommand]
    private Task CreateBackupAsync() => createBackup();
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
