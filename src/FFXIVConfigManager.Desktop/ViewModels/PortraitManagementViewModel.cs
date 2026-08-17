using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Portraits;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.Services;
using FFXIVConfigManager.Domain.Portraits;

namespace FFXIVConfigManager.Desktop.ViewModels;

public enum PortraitSourceKind
{
    Character,
    BackupArea,
}

public sealed record PortraitSourceOptionViewModel(
    PortraitSourceKind Kind,
    string DisplayName,
    string? CharacterDirectory);

public sealed partial class PortraitManagementViewModel : ViewModelBase
{
    private readonly IPortraitManagementService _service;
    private readonly string _libraryRoot;
    private readonly IPortraitBackupEditDialogService _editDialog;
    private readonly ITextLocalizer _text;
    private readonly List<PortraitListItemViewModel> _allLeftItems = [];
    private readonly List<PortraitListItemViewModel> _allRightItems = [];
    private int _leftLoadVersion;
    private int _rightLoadVersion;

    public PortraitManagementViewModel(
        IPortraitManagementService service,
        string libraryRoot,
        IReadOnlyList<PortraitSourceOptionViewModel> characters,
        IPortraitBackupEditDialogService editDialog,
        ITextLocalizer text)
    {
        _service = service;
        _libraryRoot = libraryRoot;
        _editDialog = editDialog;
        _text = text;
        StatusMessage = text["PortraitSelectSpecificHint"];
        Sources =
        [
            .. characters,
            new PortraitSourceOptionViewModel(PortraitSourceKind.BackupArea, text["PortraitBackupArea"], null),
        ];
        SelectedLeftSource = Sources.FirstOrDefault(item => item.Kind == PortraitSourceKind.Character);
        SelectedRightSource = Sources.Last();
    }

    public IReadOnlyList<PortraitSourceOptionViewModel> Sources { get; }

    public ObservableCollection<PortraitListItemViewModel> LeftItems { get; } = [];

    public ObservableCollection<PortraitListItemViewModel> RightItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationName))]
    [NotifyPropertyChangedFor(nameof(IsBackupOperation))]
    [NotifyPropertyChangedFor(nameof(IsLeftBackupArea))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteLeftBackupCommand))]
    public partial PortraitSourceOptionViewModel? SelectedLeftSource { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationName))]
    [NotifyPropertyChangedFor(nameof(IsBackupOperation))]
    [NotifyPropertyChangedFor(nameof(IsRightBackupArea))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRightBackupCommand))]
    public partial PortraitSourceOptionViewModel? SelectedRightSource { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteLeftBackupCommand))]
    public partial PortraitListItemViewModel? SelectedLeftItem { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRightBackupCommand))]
    public partial PortraitListItemViewModel? SelectedRightItem { get; set; }

    [ObservableProperty]
    public partial string LeftSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RightSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftDeleteButtonText))]
    [NotifyCanExecuteChangedFor(nameof(DeleteLeftBackupCommand))]
    public partial bool IsLeftDeleteArmed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RightDeleteButtonText))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRightBackupCommand))]
    public partial bool IsRightDeleteArmed { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial string SchemeName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SwapDirectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteLeftBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteRightBackupCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditBackupCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial bool IsConfirmationArmed { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OperationName))]
    [NotifyPropertyChangedFor(nameof(DirectionSymbol))]
    [NotifyPropertyChangedFor(nameof(LeftPanelTitle))]
    [NotifyPropertyChangedFor(nameof(RightPanelTitle))]
    [NotifyPropertyChangedFor(nameof(IsBackupOperation))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteOperationCommand))]
    public partial bool IsDirectionReversed { get; private set; }

    public bool IsBackupOperation =>
        GetSourceEndpoint()?.Kind == PortraitSourceKind.Character &&
        GetTargetEndpoint()?.Kind == PortraitSourceKind.BackupArea;

    public bool IsLeftBackupArea => SelectedLeftSource?.Kind == PortraitSourceKind.BackupArea;

    public bool IsRightBackupArea => SelectedRightSource?.Kind == PortraitSourceKind.BackupArea;

    public string LeftDeleteButtonText => IsLeftDeleteArmed
        ? _text["ConfirmDeletePortraitScheme"]
        : _text["DeletePortraitScheme"];

    public string RightDeleteButtonText => IsRightDeleteArmed
        ? _text["ConfirmDeletePortraitScheme"]
        : _text["DeletePortraitScheme"];

    public string DirectionSymbol => IsDirectionReversed ? "←" : "→";

    public string LeftPanelTitle => IsDirectionReversed
        ? _text["PortraitTarget"]
        : _text["PortraitSource"];

    public string RightPanelTitle => IsDirectionReversed
        ? _text["PortraitSource"]
        : _text["PortraitTarget"];

    public string OperationName
    {
        get
        {
            var label = GetOperationKind() switch
            {
                PortraitOperationKind.Backup => _text["PortraitBackupSelectedLabel"],
                PortraitOperationKind.Restore => _text["PortraitRestoreSelectedLabel"],
                PortraitOperationKind.Migrate => _text["PortraitMigrateSelectedLabel"],
                _ => _text["PortraitSelectDifferentSources"],
            };
            return GetOperationKind() == PortraitOperationKind.None
                ? label
                : IsDirectionReversed ? $"← {label}" : $"{label} →";
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Task.WhenAll(
            ReloadLeftAsync(cancellationToken),
            ReloadRightAsync(cancellationToken));
    }

    partial void OnSelectedLeftSourceChanged(PortraitSourceOptionViewModel? value)
    {
        SelectedLeftItem = null;
        CancelAllConfirmations();
        _ = ReloadLeftAsync();
    }

    partial void OnSelectedRightSourceChanged(PortraitSourceOptionViewModel? value)
    {
        SelectedRightItem = null;
        CancelAllConfirmations();
        _ = ReloadRightAsync();
    }

    partial void OnSelectedLeftItemChanged(PortraitListItemViewModel? value)
    {
        CancelOperationConfirmation();
        CancelDeleteConfirmation(isLeft: true);
    }

    partial void OnSelectedRightItemChanged(PortraitListItemViewModel? value)
    {
        CancelOperationConfirmation();
        CancelDeleteConfirmation(isLeft: false);
    }

    partial void OnLeftSearchTextChanged(string value)
    {
        CancelDeleteConfirmation(isLeft: true);
        ApplyLeftFilter();
    }

    partial void OnRightSearchTextChanged(string value)
    {
        CancelDeleteConfirmation(isLeft: false);
        ApplyRightFilter();
    }

    [RelayCommand(CanExecute = nameof(CanSwapDirection))]
    private void SwapDirection()
    {
        IsDirectionReversed = !IsDirectionReversed;
        CancelAllConfirmations();
        ExecuteOperationCommand.NotifyCanExecuteChanged();
    }

    private bool CanSwapDirection() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEditBackup))]
    private async Task EditBackupAsync(
        PortraitListItemViewModel? item,
        CancellationToken cancellationToken)
    {
        var backup = item?.BackupEntry;
        if (backup?.Integrity != PortraitBackupIntegrity.Valid || backup.Manifest is null)
        {
            return;
        }

        CancelAllConfirmations();
        var result = await _editDialog.ShowAsync(
            backup.Manifest.SchemeName,
            backup.Manifest.Note,
            cancellationToken);
        if (result is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var updated = await _service.UpdateBackupMetadataAsync(
                backup,
                _libraryRoot,
                result.SchemeName,
                result.Note,
                cancellationToken);
            var editedOnLeft = ReferenceEquals(item, SelectedLeftItem);
            await ReloadBackupSidesAsync(cancellationToken);
            var backupId = updated.Manifest!.BackupId;
            var visibleItem = editedOnLeft
                ? LeftItems.FirstOrDefault(candidate => candidate.BackupEntry?.Manifest?.BackupId == backupId)
                : RightItems.FirstOrDefault(candidate => candidate.BackupEntry?.Manifest?.BackupId == backupId);
            if (editedOnLeft)
            {
                SelectedLeftItem = visibleItem;
            }
            else
            {
                SelectedRightItem = visibleItem;
            }

            StatusMessage = visibleItem is null
                ? _text["PortraitSchemeUpdatedOutsideFilter"]
                : _text.Format("PortraitSchemeUpdatedFormat", updated.Manifest.SchemeName);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _text["PortraitOperationCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("UpdatePortraitSchemeFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanEditBackup(PortraitListItemViewModel? item) =>
        !IsBusy &&
        item?.BackupEntry?.Integrity == PortraitBackupIntegrity.Valid &&
        item.BackupEntry.Manifest is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteLeftBackup))]
    private Task DeleteLeftBackupAsync(CancellationToken cancellationToken) =>
        DeleteBackupAsync(isLeft: true, cancellationToken);

    private bool CanDeleteLeftBackup() =>
        !IsBusy && IsLeftBackupArea && SelectedLeftItem?.BackupEntry is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteRightBackup))]
    private Task DeleteRightBackupAsync(CancellationToken cancellationToken) =>
        DeleteBackupAsync(isLeft: false, cancellationToken);

    private bool CanDeleteRightBackup() =>
        !IsBusy && IsRightBackupArea && SelectedRightItem?.BackupEntry is not null;

    private async Task DeleteBackupAsync(bool isLeft, CancellationToken cancellationToken)
    {
        var item = isLeft ? SelectedLeftItem : SelectedRightItem;
        var backup = item?.BackupEntry
            ?? throw new InvalidOperationException(_text["PortraitBackupNotSelected"]);
        var isArmed = isLeft ? IsLeftDeleteArmed : IsRightDeleteArmed;
        if (!isArmed)
        {
            if (isLeft)
            {
                IsLeftDeleteArmed = true;
            }
            else
            {
                IsRightDeleteArmed = true;
            }

            StatusMessage = _text.Format("DeletePortraitSchemeConfirmationFormat", item!.SchemeName);
            return;
        }

        IsBusy = true;
        try
        {
            await _service.DeleteBackupAsync(backup, _libraryRoot, cancellationToken);
            ResetDeleteConfirmation();
            if (isLeft)
            {
                SelectedLeftItem = null;
                await ReloadLeftAsync(cancellationToken);
            }
            else
            {
                SelectedRightItem = null;
                await ReloadRightAsync(cancellationToken);
            }

            StatusMessage = _text.Format("PortraitSchemeDeletedFormat", item!.SchemeName);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _text["PortraitOperationCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("DeletePortraitSchemeFailedFormat", exception.Message);
        }
        finally
        {
            ResetDeleteConfirmation();
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await Task.WhenAll(
            ReloadLeftAsync(cancellationToken),
            ReloadRightAsync(cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(CanExecuteOperation))]
    private async Task ExecuteOperationAsync(CancellationToken cancellationToken)
    {
        var kind = GetOperationKind();
        if (kind is PortraitOperationKind.Restore or PortraitOperationKind.Migrate &&
            !IsConfirmationArmed)
        {
            IsConfirmationArmed = true;
            StatusMessage = _text.Format(
                "PortraitOverwriteConfirmationFormat",
                GetTargetItem()!.GearsetNumberText,
                OperationName.Trim('→', '←', ' '));
            return;
        }

        IsBusy = true;
        ResetConfirmation();
        try
        {
            if (kind == PortraitOperationKind.Backup)
            {
                var source = GetSourceItem()!.CharacterPortrait!;
                var backup = await _service.CreateBackupAsync(
                    source,
                    _libraryRoot,
                    SchemeName,
                    Note,
                    cancellationToken: cancellationToken);
                StatusMessage = _text.Format("PortraitBackupCreatedFormat", backup.Manifest!.SchemeName);
                SchemeName = string.Empty;
                Note = string.Empty;
                await ReloadTargetSideAsync(cancellationToken);
                return;
            }

            var sourceItem = GetSourceItem()!;
            var transferSource = sourceItem.CharacterPortrait is not null
                ? PortraitTransferSource.FromCharacter(sourceItem.CharacterPortrait)
                : PortraitTransferSource.FromBackup(
                    sourceItem.BackupEntry
                    ?? throw new InvalidOperationException(_text["PortraitSourceUnavailable"]));
            var target = GetTargetItem()!.CharacterPortrait
                ?? throw new InvalidOperationException(_text["PortraitTargetUnavailable"]);
            await _service.TransferAsync(transferSource, target, _libraryRoot, cancellationToken);
            StatusMessage = kind == PortraitOperationKind.Restore
                ? _text.Format("PortraitRestoredFormat", target.GearsetNumber)
                : _text.Format("PortraitMigratedFormat", target.GearsetNumber);
            await ReloadTargetSideAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _text["PortraitOperationCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("PortraitOperationFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExecuteOperation()
    {
        if (IsBusy || SelectedLeftSource is null || SelectedRightSource is null ||
            IsSameSource(SelectedLeftSource, SelectedRightSource) ||
            GetSourceItem()?.Data is null)
        {
            return false;
        }

        return GetOperationKind() switch
        {
            PortraitOperationKind.Backup =>
                !string.IsNullOrWhiteSpace(SchemeName) && !string.IsNullOrWhiteSpace(Note),
            PortraitOperationKind.Restore or PortraitOperationKind.Migrate =>
                GetTargetItem()?.CharacterPortrait is not null,
            _ => false,
        };
    }

    private PortraitOperationKind GetOperationKind()
    {
        if (SelectedLeftSource is null || SelectedRightSource is null ||
            IsSameSource(SelectedLeftSource, SelectedRightSource))
        {
            return PortraitOperationKind.None;
        }

        return (GetSourceEndpoint()!.Kind, GetTargetEndpoint()!.Kind) switch
        {
            (PortraitSourceKind.Character, PortraitSourceKind.BackupArea) => PortraitOperationKind.Backup,
            (PortraitSourceKind.BackupArea, PortraitSourceKind.Character) => PortraitOperationKind.Restore,
            (PortraitSourceKind.Character, PortraitSourceKind.Character) => PortraitOperationKind.Migrate,
            _ => PortraitOperationKind.None,
        };
    }

    private PortraitSourceOptionViewModel? GetSourceEndpoint() =>
        IsDirectionReversed ? SelectedRightSource : SelectedLeftSource;

    private PortraitSourceOptionViewModel? GetTargetEndpoint() =>
        IsDirectionReversed ? SelectedLeftSource : SelectedRightSource;

    private PortraitListItemViewModel? GetSourceItem() =>
        IsDirectionReversed ? SelectedRightItem : SelectedLeftItem;

    private PortraitListItemViewModel? GetTargetItem() =>
        IsDirectionReversed ? SelectedLeftItem : SelectedRightItem;

    private Task ReloadTargetSideAsync(CancellationToken cancellationToken) =>
        IsDirectionReversed
            ? ReloadLeftAsync(cancellationToken)
            : ReloadRightAsync(cancellationToken);

    private Task ReloadBackupSidesAsync(CancellationToken cancellationToken)
    {
        var tasks = new List<Task>(2);
        if (IsLeftBackupArea)
        {
            tasks.Add(ReloadLeftAsync(cancellationToken));
        }

        if (IsRightBackupArea)
        {
            tasks.Add(ReloadRightAsync(cancellationToken));
        }

        return Task.WhenAll(tasks);
    }

    private static bool IsSameSource(
        PortraitSourceOptionViewModel left,
        PortraitSourceOptionViewModel right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        if (left.Kind == PortraitSourceKind.BackupArea)
        {
            return true;
        }

        return string.Equals(
            Path.GetFullPath(left.CharacterDirectory!),
            Path.GetFullPath(right.CharacterDirectory!),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private async Task ReloadLeftAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _leftLoadVersion);
        var source = SelectedLeftSource;
        try
        {
            var items = await LoadItemsAsync(source, cancellationToken);
            if (version != _leftLoadVersion || source != SelectedLeftSource)
            {
                return;
            }

            _allLeftItems.Clear();
            _allLeftItems.AddRange(items);
            ApplyLeftFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == _leftLoadVersion)
            {
                LeftItems.Clear();
                StatusMessage = _text.Format("PortraitLeftReadFailedFormat", exception.Message);
            }
        }
    }

    private async Task ReloadRightAsync(CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _rightLoadVersion);
        var source = SelectedRightSource;
        try
        {
            var items = await LoadItemsAsync(source, cancellationToken);
            if (version != _rightLoadVersion || source != SelectedRightSource)
            {
                return;
            }

            _allRightItems.Clear();
            _allRightItems.AddRange(items);
            ApplyRightFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (version == _rightLoadVersion)
            {
                RightItems.Clear();
                StatusMessage = _text.Format("PortraitRightReadFailedFormat", exception.Message);
            }
        }
    }

    private async Task<IReadOnlyList<PortraitListItemViewModel>> LoadItemsAsync(
        PortraitSourceOptionViewModel? source,
        CancellationToken cancellationToken)
    {
        if (source is null)
        {
            return [];
        }

        if (source.Kind == PortraitSourceKind.Character)
        {
            var portraits = await _service.ScanCharacterAsync(
                source.CharacterDirectory!,
                cancellationToken);
            return portraits.Select(PortraitListItemViewModel.FromCharacter).ToArray();
        }

        var backups = await _service.ScanBackupsAsync(_libraryRoot, cancellationToken);
        return backups.Select(PortraitListItemViewModel.FromBackup).ToArray();
    }

    private void ApplyLeftFilter() =>
        ReplaceItems(
            LeftItems,
            IsLeftBackupArea ? FilterBackups(_allLeftItems, LeftSearchText) : _allLeftItems);

    private void ApplyRightFilter() =>
        ReplaceItems(
            RightItems,
            IsRightBackupArea ? FilterBackups(_allRightItems, RightSearchText) : _allRightItems);

    private static IReadOnlyList<PortraitListItemViewModel> FilterBackups(
        IReadOnlyList<PortraitListItemViewModel> items,
        string searchText)
    {
        var query = searchText.Trim();
        if (query.Length == 0)
        {
            return items;
        }

        return items
            .Where(item => item.IsBackup &&
                           (item.SchemeName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                            item.Note.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    private static void ReplaceItems(
        ObservableCollection<PortraitListItemViewModel> target,
        IReadOnlyList<PortraitListItemViewModel> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private void ResetConfirmation() => IsConfirmationArmed = false;

    private void ResetDeleteConfirmation()
    {
        IsLeftDeleteArmed = false;
        IsRightDeleteArmed = false;
    }

    private void CancelOperationConfirmation()
    {
        if (!IsConfirmationArmed)
        {
            return;
        }

        IsConfirmationArmed = false;
        StatusMessage = _text["PortraitSelectSpecificHint"];
    }

    private void CancelDeleteConfirmation(bool isLeft)
    {
        var isArmed = isLeft ? IsLeftDeleteArmed : IsRightDeleteArmed;
        if (!isArmed)
        {
            return;
        }

        if (isLeft)
        {
            IsLeftDeleteArmed = false;
        }
        else
        {
            IsRightDeleteArmed = false;
        }

        StatusMessage = _text["PortraitSelectSpecificHint"];
    }

    private void CancelAllConfirmations()
    {
        var hadConfirmation = IsConfirmationArmed || IsLeftDeleteArmed || IsRightDeleteArmed;
        ResetConfirmation();
        ResetDeleteConfirmation();
        if (hadConfirmation)
        {
            StatusMessage = _text["PortraitSelectSpecificHint"];
        }
    }

    private enum PortraitOperationKind
    {
        None,
        Backup,
        Restore,
        Migrate,
    }
}

public sealed record PortraitListItemViewModel(
    CharacterPortrait? CharacterPortrait,
    PortraitBackupEntry? BackupEntry,
    PortraitData? Data,
    byte ClassJobId,
    string GearsetNumberText,
    string GearsetName,
    string UpdatedAtText,
    string SchemeName,
    string Note,
    string BackupCreatedAtText,
    string IntegrityText)
{
    public IImage JobIcon => PortraitJobIconCache.Get(ClassJobId);

    public bool IsBackup => BackupEntry is not null;

    public bool IsCharacter => CharacterPortrait is not null;

    public static PortraitListItemViewModel FromCharacter(CharacterPortrait portrait) =>
        new(
            portrait,
            null,
            portrait.Data,
            portrait.ClassJobId,
            portrait.GearsetNumber.ToString("00"),
            portrait.GearsetName,
            FormatTime(portrait.Data.LastUpdatedUtc),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);

    public static PortraitListItemViewModel FromBackup(PortraitBackupEntry backup)
    {
        var manifest = backup.Manifest;
        return new(
            null,
            backup,
            backup.Data,
            manifest?.Source.ClassJobId ?? 0,
            manifest?.Source.GearsetNumber.ToString("00") ?? "—",
            manifest?.Source.GearsetName ?? ResourceTextLocalizer.Instance["UnknownAppearance"],
            FormatTime(manifest?.PortraitLastUpdatedUtc),
            manifest?.SchemeName ?? ResourceTextLocalizer.Instance["PortraitBackupCorrupted"],
            manifest?.Note ?? string.Join("；", backup.Errors),
            ResourceTextLocalizer.Instance.Format(
                "PortraitBackupCreatedAtFormat",
                (manifest?.CreatedAtUtc ?? backup.ArchiveLastWriteTimeUtc).ToLocalTime().ToString("g")),
            backup.Integrity == PortraitBackupIntegrity.Valid
                ? ResourceTextLocalizer.Instance["IntegrityValid"]
                : ResourceTextLocalizer.Instance["IntegrityCorrupted"]);
    }

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("g") ?? ResourceTextLocalizer.Instance["UnknownTime"];
}

internal static class PortraitJobIconCache
{
    private static readonly Dictionary<byte, IImage> Icons = [];
    private static readonly Lock SyncRoot = new();

    public static IImage Get(byte classJobId)
    {
        var iconId = classJobId is >= 1 and <= 42 ? classJobId : (byte)0;
        lock (SyncRoot)
        {
            if (Icons.TryGetValue(iconId, out var icon))
            {
                return icon;
            }

            var uri = new Uri($"avares://FFXIVConfigManager/Assets/Jobs/{iconId:00}.png");
            using var stream = AssetLoader.Open(uri);
            icon = new Bitmap(stream);
            Icons.Add(iconId, icon);
            return icon;
        }
    }
}
