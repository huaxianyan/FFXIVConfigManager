using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Backups;
using FFXIVConfigManager.Application.Settings;
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
    private readonly ScanSnapshotLibraryUseCase _scanSnapshotLibrary;
    private readonly PruneAutomaticBackupsUseCase _pruneAutomaticBackups;
    private readonly AutomaticBackupPolicy _policy;
    private readonly ITextLocalizer _text;
    private readonly string _manualLayerHint;
    private SnapshotLibraryEntry? _previewedBackup;
    private GameProfile? _previewedTargetProfile;
    private bool _restoreCompleted;

    public CharacterBackupsViewModel(
        CharacterBackupDialogContext context,
        PreviewSnapshotUseCase previewSnapshot,
        RestoreSnapshotUseCase restoreSnapshot,
        ISnapshotArchiveService archiveService,
        ScanSnapshotLibraryUseCase scanSnapshotLibrary,
        PruneAutomaticBackupsUseCase pruneAutomaticBackups,
        AutomaticBackupPolicy policy,
        BackupRetentionSetting manualCleanup,
        ITextLocalizer text)
    {
        _context = context;
        _previewSnapshot = previewSnapshot;
        _restoreSnapshot = restoreSnapshot;
        _archiveService = archiveService;
        _scanSnapshotLibrary = scanSnapshotLibrary;
        _pruneAutomaticBackups = pruneAutomaticBackups;
        _policy = policy;
        _text = text;
        ManualBackups = new ObservableCollection<BackupOptionViewModel>(
            context.Backups.Where(entry => !entry.Rule.IsAutomatic())
                .Select(entry => BackupOptionViewModel.From(entry, text)));
        AutomaticBackups = new ObservableCollection<BackupOptionViewModel>(
            context.Backups.Where(entry => entry.Rule.IsAutomatic())
                .Select(entry => BackupOptionViewModel.From(entry, text)));
        TargetProfiles = context.AvailableProfiles
            .Select(GameProfileOptionViewModel.From)
            .ToArray();
        Title = text.Format("BackupManagerTitleFormat", context.CharacterName);
        CharacterName = context.CharacterName;
        ManualTabTitle = text.Format("ManualBackupsTabFormat", ManualBackups.Count);
        AutomaticTabTitle = text.Format("AutomaticBackupsTabFormat", AutomaticBackups.Count);
        _manualLayerHint = manualCleanup.Enabled
            ? text.Format("ManualLayerHintFormat", manualCleanup.EffectiveRetentionCount)
            : text["BackupNotSelected"];

        var initialProfile = context.TargetProfile is null
            ? TargetProfiles.Count == 1 ? TargetProfiles[0] : null
            : TargetProfiles.FirstOrDefault(item => item.Profile.Id == context.TargetProfile.Id);
        SelectedTargetProfile = initialProfile;
        ApplyLayerFilter();
        StatusMessage = EmptySelectionMessage;
    }

    public ObservableCollection<BackupOptionViewModel> ManualBackups { get; }

    public ObservableCollection<BackupOptionViewModel> AutomaticBackups { get; }

    public ObservableCollection<BackupOptionViewModel> VisibleBackups { get; } = [];

    public ObservableCollection<SnapshotFilePreviewViewModel> PreviewFiles { get; } = [];

    public IReadOnlyList<GameProfileOptionViewModel> TargetProfiles { get; }

    public string Title { get; }

    public string CharacterName { get; }

    [ObservableProperty]
    public partial string ManualTabTitle { get; private set; }

    [ObservableProperty]
    public partial string AutomaticTabTitle { get; private set; }

    public bool CanSelectTargetProfile => _context.TargetCharacter is null;

    public bool Changed { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyPropertyChangedFor(nameof(IsDeleteAvailable))]
    [NotifyPropertyChangedFor(nameof(SelectedBackupDetails))]
    public partial BackupOptionViewModel? SelectedBackup { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    public partial GameProfileOptionViewModel? SelectedTargetProfile { get; set; }

    [ObservableProperty]
    public partial bool IsShowingAutomatic { get; set; }

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

    public bool IsDeleteAvailable =>
        SelectedBackup is not null && !SelectedBackup.Entry.Rule.IsAutomatic();

    public string SelectedBackupDetails =>
        SelectedBackup?.Details.Length > 0 ? SelectedBackup.Details : string.Empty;

    private bool CreateRecoveryPoint => _policy.BeforeCharacterRestore.Enabled;

    /// <summary>没有选中条目时按当前页签给出对应说明：自动页签解释清理机制，手动页签在开启清理时给出提醒。</summary>
    private string EmptySelectionMessage => IsShowingAutomatic
        ? _text.Format("AutomaticLayerHintFormat", AutomaticBackups.Count)
        : _manualLayerHint;

    partial void OnSelectedBackupChanged(BackupOptionViewModel? value) => ResetPreview(value);

    partial void OnSelectedTargetProfileChanged(GameProfileOptionViewModel? value) =>
        ResetPreview(SelectedBackup);

    partial void OnIsShowingAutomaticChanged(bool value)
    {
        SelectedBackup = null;
        ApplyLayerFilter();
        StatusMessage = EmptySelectionMessage;
    }

    private void ApplyLayerFilter()
    {
        VisibleBackups.Clear();
        var source = IsShowingAutomatic ? AutomaticBackups : ManualBackups;
        foreach (var item in source)
        {
            VisibleBackups.Add(item);
        }
    }

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
                CreateRecoveryPoint,
                cancellationToken);
            Changed = true;
            _restoreCompleted = true;
            var message = result.RecoveryPoint is not null
                ? _text.Format(
                    "RestoreSucceededFormat",
                    result.RestoreResult.RestoredFileCount,
                    Path.GetFileName(result.RecoveryPoint.ArchivePath))
                : result.CreatedTargetDirectory
                    ? _text.Format(
                        "RestoreCreatedCharacterFormat",
                        result.RestoreResult.RestoredFileCount,
                        target.Character!.FolderName.Value)
                    : _text.Format(
                        "RestoreSucceededNoRecoveryFormat",
                        result.RestoreResult.RestoredFileCount);

            if (result.RecoveryPoint is not null)
            {
                await ReloadAutomaticBackupsAsync(cancellationToken);
            }

            // 重建自动列表会清掉选中状态并改写提示，成功文案放在最后设置。
            StatusMessage = message;
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
            StatusMessage = _text["DeleteCharacterBackupWarning"];
            return;
        }

        IsBusy = true;
        try
        {
            var selected = SelectedBackup!;
            await _archiveService.DeleteAsync(selected.Entry.ArchivePath, cancellationToken);
            ManualBackups.Remove(selected);
            AutomaticBackups.Remove(selected);
            VisibleBackups.Remove(selected);
            SelectedBackup = null;
            Changed = true;
            ManualTabTitle = _text.Format("ManualBackupsTabFormat", ManualBackups.Count);
            AutomaticTabTitle = _text.Format("AutomaticBackupsTabFormat", AutomaticBackups.Count);
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

    /// <summary>
    /// 恢复时新建的恢复点不在窗口打开那一刻的快照列表里，重扫一次备份库让「自动恢复点」
    /// 页签当下就能看到它，并顺带按保留数量清理，免得刚生成的恢复点要等主窗口下次刷新
    /// 备份库才被淘汰。刷新只影响列表显示，失败时不改动恢复结果的提示。
    /// </summary>
    private async Task ReloadAutomaticBackupsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _pruneAutomaticBackups.ExecuteAsync(
                _policy,
                _context.LibraryRoot,
                cancellationToken);
            var entries = await _scanSnapshotLibrary.ExecuteAsync(
                _context.LibraryRoot,
                cancellationToken);
            AutomaticBackups.Clear();
            foreach (var entry in entries.Where(item => item.Rule.IsAutomatic()))
            {
                AutomaticBackups.Add(BackupOptionViewModel.From(entry, _text));
            }

            AutomaticTabTitle = _text.Format("AutomaticBackupsTabFormat", AutomaticBackups.Count);
            if (IsShowingAutomatic)
            {
                ApplyLayerFilter();
            }
        }
        catch (Exception)
        {
            // 刷新属于尽力而为：失败时保留原有列表，恢复成功的提示照旧。
        }
    }

    private void ResetPreview(BackupOptionViewModel? selected)
    {
        ClearPreview();
        IsDeleteArmed = false;
        StatusMessage = selected is null
            ? EmptySelectionMessage
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

    private bool CanDelete() => !IsBusy && IsDeleteAvailable;
}

public sealed record GameProfileOptionViewModel(GameProfile Profile, string DisplayName)
{
    public static GameProfileOptionViewModel From(GameProfile profile) =>
        new(profile, profile.Name);
}

public sealed record BackupOptionViewModel(
    SnapshotLibraryEntry Entry,
    string DisplayName,
    string Details,
    bool IsValid)
{
    public bool IsAutomatic => Entry.Rule.IsAutomatic();

    public static BackupOptionViewModel From(SnapshotLibraryEntry entry, ITextLocalizer text)
    {
        var createdAt = (entry.Manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc)
            .ToLocalTime()
            .ToString("g");
        var integrity = entry.IntegrityStatus == SnapshotIntegrityStatus.Valid
            ? text["IntegrityValid"]
            : text["IntegrityCorrupted"];
        // 左侧列表本身已经按手动／自动分页，手动备份不必再重复标一遍「手动备份」。
        var displayName = entry.Rule == BackupRule.Manual
            ? text.Format("ManualBackupOptionFormat", createdAt, integrity)
            : text.Format("BackupOptionFormat", createdAt, TypeText(entry, text), integrity);
        return new BackupOptionViewModel(
            entry,
            displayName,
            entry.Errors.Count == 0 ? string.Empty : string.Join("；", entry.Errors),
            entry.IntegrityStatus == SnapshotIntegrityStatus.Valid);
    }

    private static string TypeText(SnapshotLibraryEntry entry, ITextLocalizer text) =>
        entry.Manifest?.Reason switch
        {
            SnapshotReason.BeforeMigration => text["TypeBeforeMigration"],
            SnapshotReason.BeforeRestore => text["TypeBeforeRestore"],
            SnapshotReason.MigrationSource => text["TypeMigrationSource"],
            SnapshotReason.Manual => text["TypeManual"],
            _ => text["TypeUnknown"],
        };
}
