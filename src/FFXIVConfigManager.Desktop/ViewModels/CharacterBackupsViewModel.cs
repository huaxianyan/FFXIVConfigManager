using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.Services;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Desktop.ViewModels;

public sealed partial class CharacterBackupsViewModel : ViewModelBase
{
    private readonly CharacterBackupDialogContext _context;
    private readonly PreviewSnapshotUseCase _previewSnapshot;
    private readonly RestoreSnapshotUseCase _restoreSnapshot;
    private readonly ISnapshotArchiveService _archiveService;
    private readonly ITextLocalizer _text;
    private SnapshotLibraryEntry? _previewedBackup;
    private GameProfile? _previewedTargetProfile;
    private bool _restoreCompleted;

    public CharacterBackupsViewModel(
        CharacterBackupDialogContext context,
        PreviewSnapshotUseCase previewSnapshot,
        RestoreSnapshotUseCase restoreSnapshot,
        ISnapshotArchiveService archiveService,
        ITextLocalizer text)
    {
        _context = context;
        _previewSnapshot = previewSnapshot;
        _restoreSnapshot = restoreSnapshot;
        _archiveService = archiveService;
        _text = text;
        Backups = new ObservableCollection<BackupOptionViewModel>(
            context.Backups.Select(entry => BackupOptionViewModel.From(entry, text)));
        TargetProfiles = context.AvailableProfiles
            .Select(GameProfileOptionViewModel.From)
            .ToArray();
        Title = text.Format("BackupManagerTitleFormat", context.CharacterName);
        CharacterName = context.CharacterName;
        StatusMessage = text["BackupNotSelected"];

        var initialProfile = context.TargetProfile is null
            ? TargetProfiles.Count == 1 ? TargetProfiles[0] : null
            : TargetProfiles.FirstOrDefault(item => item.Profile.Id == context.TargetProfile.Id);
        SelectedTargetProfile = initialProfile;
    }

    public ObservableCollection<BackupOptionViewModel> Backups { get; }

    public ObservableCollection<SnapshotFilePreviewViewModel> PreviewFiles { get; } = [];

    public IReadOnlyList<GameProfileOptionViewModel> TargetProfiles { get; }

    public string Title { get; }

    public string CharacterName { get; }

    public bool CanSelectTargetProfile => _context.TargetCharacter is null;

    public bool Changed { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial BackupOptionViewModel? SelectedBackup { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial GameProfileOptionViewModel? SelectedTargetProfile { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteButtonText))]
    public partial bool IsDeleteArmed { get; private set; }

    public string DeleteButtonText => IsDeleteArmed
        ? _text["ConfirmDeleteSelectedBackup"]
        : _text["DeleteSelectedBackup"];

    partial void OnSelectedBackupChanged(BackupOptionViewModel? value) => ResetPreview(value);

    partial void OnSelectedTargetProfileChanged(GameProfileOptionViewModel? value) =>
        ResetPreview(SelectedBackup);

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync(CancellationToken cancellationToken)
    {
        var selected = SelectedBackup!;
        var target = ResolveTarget(selected.Entry);
        IsBusy = true;
        try
        {
            var preview = await _previewSnapshot.ExecuteAsync(
                selected.Entry,
                target.Character,
                cancellationToken);
            PreviewFiles.Clear();
            foreach (var file in preview.Files)
            {
                PreviewFiles.Add(SnapshotFilePreviewViewModel.From(file));
            }

            _previewedBackup = selected.Entry;
            _previewedTargetProfile = target.Profile;
            var changed = preview.Files.Count(file => file.Difference != SnapshotFileDifference.Identical);
            StatusMessage = target.Profile is null
                ? _text["SelectRestoreProfile"]
                : _text.Format(
                    "RestorePreviewFormat",
                    changed,
                    preview.Files.Count - changed);
        }
        catch (Exception exception)
        {
            ClearPreview();
            StatusMessage = _text.Format("BackupPreviewFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
            RestoreCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var target = ResolveTarget(SelectedBackup!.Entry);
        IsBusy = true;
        try
        {
            var result = await _restoreSnapshot.ExecuteAsync(
                SelectedBackup.Entry,
                target.Profile!,
                target.Character!,
                _context.LibraryRoot,
                cancellationToken);
            Changed = true;
            _restoreCompleted = true;
            StatusMessage = result.RecoveryPoint is null
                ? _text.Format(
                    "RestoreCreatedCharacterFormat",
                    result.RestoreResult.RestoredFileCount,
                    target.Character!.FolderName.Value)
                : _text.Format(
                    "RestoreSucceededFormat",
                    result.RestoreResult.RestoredFileCount,
                    Path.GetFileName(result.RecoveryPoint.ArchivePath));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _text["RestoreCanceled"];
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("RestoreFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
            RestoreCommand.NotifyCanExecuteChanged();
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
            var selected = SelectedBackup!;
            await _archiveService.DeleteAsync(selected.Entry.ArchivePath, cancellationToken);
            Backups.Remove(selected);
            SelectedBackup = null;
            Changed = true;
            StatusMessage = _text.Format(
                "BackupDeletedFormat",
                Path.GetFileName(selected.Entry.ArchivePath));
        }
        catch (Exception exception)
        {
            StatusMessage = _text.Format("DeleteBackupFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
            IsDeleteArmed = false;
        }
    }

    private void ResetPreview(BackupOptionViewModel? selected)
    {
        ClearPreview();
        IsDeleteArmed = false;
        StatusMessage = selected is null
            ? _text["BackupNotSelected"]
            : !selected.IsValid
                ? _text["BackupMustBeValid"]
                : ResolveTarget(selected.Entry).Profile is null
                    ? _text["SelectRestoreProfile"]
                    : _text["PreviewBeforeRestore"];
        RestoreCommand.NotifyCanExecuteChanged();
    }

    private void ClearPreview()
    {
        _previewedBackup = null;
        _previewedTargetProfile = null;
        PreviewFiles.Clear();
    }

    private (GameProfile? Profile, CharacterConfiguration? Character) ResolveTarget(
        SnapshotLibraryEntry backup)
    {
        if (_context.TargetProfile is not null && _context.TargetCharacter is not null)
        {
            return (_context.TargetProfile, _context.TargetCharacter);
        }

        var profile = SelectedTargetProfile?.Profile;
        var characterFolder = backup.Manifest?.Source.CharacterFolder;
        if (profile is null ||
            !CharacterFolderName.TryCreate(characterFolder, out var folderName))
        {
            return (null, null);
        }

        var fullPath = Path.Combine(profile.ConfigRoot, folderName.Value);
        return (
            profile,
            new CharacterConfiguration(
                profile.Id,
                folderName,
                Path.GetFullPath(fullPath),
                DateTimeOffset.MinValue,
                []));
    }

    private bool CanPreview() => !IsBusy && SelectedBackup?.IsValid == true;

    private bool CanRestore()
    {
        if (IsBusy || _restoreCompleted || SelectedBackup?.IsValid != true)
        {
            return false;
        }

        var target = ResolveTarget(SelectedBackup.Entry);
        return target.Profile is not null &&
               target.Character is not null &&
               ReferenceEquals(_previewedBackup, SelectedBackup.Entry) &&
               ReferenceEquals(_previewedTargetProfile, target.Profile);
    }

    private bool CanDelete() => !IsBusy && SelectedBackup is not null;
}

public sealed record GameProfileOptionViewModel(GameProfile Profile, string DisplayName)
{
    public static GameProfileOptionViewModel From(GameProfile profile) =>
        new(profile, $"{profile.Name} · {profile.ConfigRoot}");
}

public sealed record BackupOptionViewModel(
    SnapshotLibraryEntry Entry,
    string DisplayName,
    string Details,
    bool IsValid)
{
    public static BackupOptionViewModel From(SnapshotLibraryEntry entry, ITextLocalizer text)
    {
        var createdAt = (entry.Manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc)
            .ToLocalTime()
            .ToString("g");
        var type = entry.Manifest?.Reason switch
        {
            SnapshotReason.BeforeMigration => text["TypeBeforeMigration"],
            SnapshotReason.BeforeRestore => text["TypeBeforeRestore"],
            SnapshotReason.MigrationSource => text["TypeMigrationSource"],
            SnapshotReason.Manual => text["TypeManual"],
            _ => text["TypeUnknown"],
        };
        var integrity = entry.IntegrityStatus == SnapshotIntegrityStatus.Valid
            ? text["IntegrityValid"]
            : text["IntegrityCorrupted"];
        return new BackupOptionViewModel(
            entry,
            text.Format("BackupOptionFormat", createdAt, type, integrity),
            entry.Errors.Count == 0 ? string.Empty : string.Join("；", entry.Errors),
            entry.IntegrityStatus == SnapshotIntegrityStatus.Valid);
    }
}
