using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
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
    IFolderPickerService folderPicker) : ViewModelBase
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
        new(ConfigScope.Hud, "HUD 与界面"),
        new(ConfigScope.Character, "角色设置"),
        new(ConfigScope.Controls, "操作与键位"),
        new(ConfigScope.Hotbars, "热键栏"),
        new(ConfigScope.Macros, "角色宏"),
        new(ConfigScope.Gearsets, "套装列表"),
        new(ConfigScope.UiState, "界面状态与场地标点"),
        new(ConfigScope.AllKnownFiles, "全部 14 个已知文件（高级）", isSelected: false),
    ];

    public IReadOnlyList<GameRegionOption> RegionOptions { get; } =
    [
        new(GameRegion.International, "国际服"),
        new(GameRegion.China, "国服"),
        new(GameRegion.Custom, "其他"),
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
        NavigationPage.Dashboard => "概览",
        NavigationPage.Characters => "角色管理",
        NavigationPage.Backups => "备份与恢复",
        NavigationPage.Migration => "角色迁移",
        NavigationPage.Settings => "配置与存储",
        _ => "FFXIV 角色配置管理器",
    };

    public string PageSubtitle => CurrentPage switch
    {
        NavigationPage.Dashboard => "查看配置状态和备份概况",
        NavigationPage.Characters => "管理角色标记并创建配置备份",
        NavigationPage.Backups => "校验、查看并恢复历史备份",
        NavigationPage.Migration => "在两个角色之间安全迁移配置",
        NavigationPage.Settings => "管理配置源和备份存储位置",
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
    public partial string StatusMessage { get; private set; } = "正在准备扫描配置目录……";

    [ObservableProperty]
    public partial string ProfileName { get; private set; } = "尚未发现配置源";

    [ObservableProperty]
    public partial string ConfigRoot { get; private set; } = "—";

    [ObservableProperty]
    public partial string Summary { get; private set; } = "0 个角色";

    [ObservableProperty]
    public partial string SnapshotLibraryPath { get; private set; } = "尚未设置备份库";

    [ObservableProperty]
    public partial string SnapshotHistorySummary { get; private set; } = "尚无备份";

    [ObservableProperty]
    public partial string SnapshotPreviewTitle { get; private set; } = "选择有效备份以预览差异";

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
    public partial string MigrationPreviewTitle { get; private set; } = "选择源角色和目标角色后生成迁移预览";

    [ObservableProperty]
    public partial string NewProfileName { get; set; } = "国服";

    [ObservableProperty]
    public partial GameRegionOption SelectedRegion { get; set; } =
        new(GameRegion.China, "国服");

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
        StatusMessage = "正在扫描角色配置……";

        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            SnapshotLibraryPath = settings.SnapshotLibraryPath ?? "尚未设置备份库";
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
                ProfileName = "没有可用的配置源";
                ConfigRoot = "可在下方添加自定义配置目录。";
                Summary = "0 个角色";
                StatusMessage = "当前未配置任何 FFXIV 配置目录。";
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

            Summary = $"{Characters.Count} 个角色 · {results.Count} 个配置源";
            var issues = results
                .Where(result => result.Issue is not null)
                .Select(result => $"{result.Profile.Name}：{result.Issue}")
                .ToArray();
            var failedRecoveries = recoveryResults
                .Where(result => !result.Recovered)
                .SelectMany(result => result.Errors)
                .ToArray();
            StatusMessage = failedRecoveries.Length > 0
                ? $"检测到无法自动回滚的中断事务：{string.Join("；", failedRecoveries)}"
                : recoveryResults.Count > 0
                    ? $"已自动回滚 {recoveryResults.Count} 个中断的恢复事务。"
                    : issues.Length > 0
                        ? string.Join("　", issues)
                        : Characters.Count == 0
                            ? "未发现角色配置目录。登录过角色后可在这里看到配置。"
                            : $"扫描完成于 {DateTimeOffset.Now:HH:mm:ss}";
            await LoadSnapshotsAsync(settings, aliases, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "扫描已取消。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"扫描失败：{exception.Message}";
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
            "导出软件设置与角色标记",
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
            StatusMessage = $"软件设置与角色标记已导出：{Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            StatusMessage = $"导出软件设置失败：{exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task ImportSettingsAsync(CancellationToken cancellationToken)
    {
        var path = await folderPicker.PickOpenFileAsync(
            "导入软件设置与角色标记",
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
            StatusMessage = "角色标记已合并导入；同角色目录的导入标记优先，本机配置源和备份位置保持不变。";
            await RefreshAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            StatusMessage = $"导入软件设置失败：{exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    private async Task AddProfileAsync(CancellationToken cancellationToken)
    {
        var selectedPath = await folderPicker.PickFolderAsync(
            "选择 FFXIV 配置根目录",
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
            StatusMessage = "该配置目录已经存在。";
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
            StatusMessage = $"已添加配置源“{name}”。";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"添加配置源失败：{exception.Message}";
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
            "选择备份库目录",
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
            StatusMessage = "已更新备份库目录。";
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"设置备份库失败：{exception.Message}";
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
            StatusMessage = "已移除自定义配置源，磁盘上的游戏配置未被修改。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"移除配置源失败：{exception.Message}";
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
            StatusMessage = "已有操作正在进行，请稍候。";
            return;
        }

        IsBusy = true;
        try
        {
            var settings = await settingsService.GetAsync();
            var libraryPath = settings.SnapshotLibraryPath;
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                libraryPath = await folderPicker.PickFolderAsync("选择备份库目录");
                if (string.IsNullOrWhiteSpace(libraryPath))
                {
                    StatusMessage = "创建备份已取消：尚未设置备份库。";
                    return;
                }

                await settingsService.SetSnapshotLibraryPathAsync(libraryPath);
                SnapshotLibraryPath = Path.GetFullPath(libraryPath);
            }

            StatusMessage = $"正在为 {character.FolderName.Value} 创建稳定备份……";
            var result = await createSnapshot.ExecuteAsync(profile, character, libraryPath);
            StatusMessage =
                $"备份创建并校验成功：{Path.GetFileName(result.ArchivePath)}";
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"创建备份失败：{exception.Message}";
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
        SnapshotPreviewTitle = "选择有效备份以预览差异";
        ClearRestoreSelection();

        if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
        {
            SnapshotHistorySummary = "尚未设置备份库";
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
            ? $"{entries.Count} 个备份，全部校验有效"
            : $"{entries.Count} 个备份 · {corrupted} 个损坏";
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
            StatusMessage = $"已删除备份：{Path.GetFileName(snapshot.ArchivePath)}";
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"删除备份失败：{exception.Message}";
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
            StatusMessage = "已有操作正在进行，请稍候。";
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
                ? "本机没有对应角色；可查看备份，但需先添加同角色目录才能恢复"
                : $"恢复预览：{changed} 个文件将发生变化，" +
                  $"{preview.Files.Count - changed} 个文件相同";
            _previewedSnapshot = snapshot;
            _previewedTarget = target;
            _previewedTargetProfile = targetProfile;
            CanRestorePreview = target is not null && targetProfile is not null;
            StatusMessage = "备份已重新校验，恢复预览已生成。";
        }
        catch (Exception exception)
        {
            SnapshotPreviewFiles.Clear();
            SnapshotPreviewTitle = "无法生成预览";
            ClearRestoreSelection();
            StatusMessage = $"备份预览失败：{exception.Message}";
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
                throw new InvalidOperationException("尚未设置备份库，无法创建恢复点。");
            }

            StatusMessage = "正在创建操作前恢复点并执行事务式恢复……";
            var result = await restoreSnapshot.ExecuteAsync(
                _previewedSnapshot,
                _previewedTargetProfile,
                _previewedTarget,
                settings.SnapshotLibraryPath,
                cancellationToken);
            completionMessage =
                $"成功恢复 {result.RestoreResult.RestoredFileCount} 个文件；" +
                $"恢复点：{Path.GetFileName(result.RecoveryPoint.ArchivePath)}";
            StatusMessage = completionMessage;
            ClearRestoreSelection();
        }
        catch (SnapshotRestoreException exception)
        {
            StatusMessage = exception.RollbackCompleted
                ? exception.Message
                : $"严重错误：{exception.Message}。请使用操作前恢复点手动恢复。";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "恢复已取消；已提交的文件已回滚。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"恢复失败：{exception.Message}";
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
            MigrationPreviewTitle =
                $"迁移预览：{changed} 个文件将变化，" +
                $"{preview.Files.Count - changed} 个文件相同";
            _previewedMigrationSource = SelectedMigrationSource;
            _previewedMigrationTarget = SelectedMigrationTarget;
            _previewedMigrationScopes = scopes;
            CanConfirmMigration = true;
            StatusMessage = "迁移预览已生成；确认前不会写入目标角色。";
        }
        catch (Exception exception)
        {
            ClearMigrationPreview();
            MigrationPreviewTitle = "无法生成迁移预览";
            StatusMessage = $"迁移预览失败：{exception.Message}";
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
            StatusMessage = "迁移选择已变化，请重新生成预览。";
            return;
        }

        IsBusy = true;
        string? completionMessage = null;
        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
            {
                throw new InvalidOperationException("请先设置备份库，以保存迁移源和目标恢复点。");
            }

            StatusMessage = "正在创建迁移源备份和目标恢复点……";
            var result = await migrateCharacter.ExecuteAsync(
                _previewedMigrationSource.Profile,
                _previewedMigrationSource.Character,
                _previewedMigrationTarget.Profile,
                _previewedMigrationTarget.Character,
                settings.SnapshotLibraryPath,
                currentScopes,
                cancellationToken);
            completionMessage =
                $"迁移完成：{result.RestoreResult.RestoredFileCount} 个文件；" +
                $"目标恢复点：{Path.GetFileName(result.TargetRecoveryPoint.ArchivePath)}";
            StatusMessage = completionMessage;
            ClearMigrationPreview();
        }
        catch (SnapshotRestoreException exception)
        {
            StatusMessage = exception.RollbackCompleted
                ? exception.Message
                : $"严重错误：{exception.Message}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "迁移已取消；若已开始写入，已提交文件已回滚。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"迁移失败：{exception.Message}";
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
        MigrationPreviewTitle = "选择源角色和目标角色后生成迁移预览";
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
                ? "已清除角色别名。"
                : $"已保存角色别名“{alias.Trim()}”。";
        }
        catch (Exception exception)
        {
            StatusMessage = $"保存角色别名失败：{exception.Message}";
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
            GameRegion.International => "国际服",
            GameRegion.China => "国服",
            _ => "其他",
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
        LastModified = character.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        FileSummary = $"{character.ExistingFileCount}/{character.ExpectedFileCount} 个已知文件";
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
        CharacterFolder = manifest?.Source.CharacterFolder ?? "无法读取";
        ProfileName = manifest?.Source.ProfileName ?? "未知来源";
        CreatedAt = (manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc)
            .ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss");
        FileSummary = manifest is null
            ? "Manifest 不可用"
            : $"{manifest.Files.Count} 个文件 · {FormatSize(entry.ArchiveSize)}";
        IntegrityText = entry.IntegrityStatus == SnapshotIntegrityStatus.Valid
            ? "有效"
            : "损坏";
        TypeText = manifest?.Reason switch
        {
            SnapshotReason.BeforeMigration => "迁移前恢复点",
            SnapshotReason.BeforeRestore => "恢复前恢复点",
            SnapshotReason.MigrationSource => "迁移源",
            SnapshotReason.Manual => "手动备份",
            _ => "未知类型",
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

    public string DeleteButtonText => IsDeleteArmed ? "确认删除" : "删除";

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
                SnapshotFileDifference.Identical => "相同，不需要覆盖",
                SnapshotFileDifference.Different => "内容不同，将被覆盖",
                SnapshotFileDifference.MissingFromTarget => "目标缺失，将新增",
                _ => "未找到对应的本地角色",
            });
}
