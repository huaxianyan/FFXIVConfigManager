using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Desktop.Services;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Desktop.ViewModels;

public partial class MainViewModel(
    ScanProfilesUseCase scanProfiles,
    SettingsService settingsService,
    CreateCharacterSnapshotUseCase createSnapshot,
    ScanSnapshotLibraryUseCase scanSnapshotLibrary,
    PreviewSnapshotUseCase previewSnapshot,
    IFolderPickerService folderPicker) : ViewModelBase
{
    private readonly List<SnapshotRowViewModel> _allSnapshots = [];
    private readonly Dictionary<(Guid ProfileId, string Folder), CharacterConfiguration> _currentCharacters = [];

    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    public ObservableCollection<SnapshotRowViewModel> Snapshots { get; } = [];

    public ObservableCollection<SnapshotFilePreviewViewModel> SnapshotPreviewFiles { get; } = [];

    public IReadOnlyList<GameRegionOption> RegionOptions { get; } =
    [
        new(GameRegion.International, "国际服"),
        new(GameRegion.China, "国服"),
        new(GameRegion.Custom, "其他"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(SelectSnapshotLibraryCommand))]
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
    public partial string SnapshotLibraryPath { get; private set; } = "尚未设置快照库";

    [ObservableProperty]
    public partial string SnapshotHistorySummary { get; private set; } = "尚无快照";

    [ObservableProperty]
    public partial string SnapshotPreviewTitle { get; private set; } = "选择有效快照以预览差异";

    [ObservableProperty]
    public partial string SnapshotFilter { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewProfileName { get; set; } = "国服";

    [ObservableProperty]
    public partial GameRegionOption SelectedRegion { get; set; } =
        new(GameRegion.China, "国服");

    [RelayCommand(CanExecute = nameof(CanRunCommand))]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "正在扫描角色配置……";

        try
        {
            var settings = await settingsService.GetAsync(cancellationToken);
            SnapshotLibraryPath = settings.SnapshotLibraryPath ?? "尚未设置快照库";
            var aliases = settings.CharacterAliases
                .GroupBy(item => (item.ProfileId, item.CharacterFolder))
                .ToDictionary(group => group.Key, group => group.Last().Alias);
            var results = await scanProfiles.ExecuteAsync(cancellationToken);

            Characters.Clear();
            Profiles.Clear();
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
                Profiles.Add(ProfileRowViewModel.From(
                    result.Profile,
                    RemoveProfileAsync));

                foreach (var character in result.Characters)
                {
                    _currentCharacters[(result.Profile.Id, character.FolderName.Value)] = character;
                    aliases.TryGetValue(
                        (result.Profile.Id, character.FolderName.Value),
                        out var alias);
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
            StatusMessage = issues.Length > 0
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
            "选择快照库目录",
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
            StatusMessage = "已更新快照库目录。";
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            StatusMessage = $"设置快照库失败：{exception.Message}";
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
                libraryPath = await folderPicker.PickFolderAsync("选择快照库目录");
                if (string.IsNullOrWhiteSpace(libraryPath))
                {
                    StatusMessage = "创建快照已取消：尚未设置快照库。";
                    return;
                }

                await settingsService.SetSnapshotLibraryPathAsync(libraryPath);
                SnapshotLibraryPath = Path.GetFullPath(libraryPath);
            }

            StatusMessage = $"正在为 {character.FolderName.Value} 创建稳定快照……";
            var result = await createSnapshot.ExecuteAsync(profile, character, libraryPath);
            StatusMessage =
                $"快照创建并校验成功：{Path.GetFileName(result.ArchivePath)}";
            await ReloadSnapshotsAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"创建快照失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadSnapshotsAsync()
    {
        var settings = await settingsService.GetAsync();
        var aliases = settings.CharacterAliases
            .GroupBy(item => (item.ProfileId, item.CharacterFolder))
            .ToDictionary(group => group.Key, group => group.Last().Alias);
        await LoadSnapshotsAsync(settings, aliases, CancellationToken.None);
    }

    private async Task LoadSnapshotsAsync(
        ApplicationSettings settings,
        IReadOnlyDictionary<(Guid ProfileId, string CharacterFolder), string> aliases,
        CancellationToken cancellationToken)
    {
        _allSnapshots.Clear();
        Snapshots.Clear();
        SnapshotPreviewFiles.Clear();
        SnapshotPreviewTitle = "选择有效快照以预览差异";

        if (string.IsNullOrWhiteSpace(settings.SnapshotLibraryPath))
        {
            SnapshotHistorySummary = "尚未设置快照库";
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
                aliases.TryGetValue(
                    (entry.Manifest.Source.ProfileId, entry.Manifest.Source.CharacterFolder),
                    out alias);
            }

            _allSnapshots.Add(SnapshotRowViewModel.From(entry, alias, PreviewSnapshotAsync));
        }

        ApplySnapshotFilter();
        var corrupted = entries.Count(entry =>
            entry.IntegrityStatus == SnapshotIntegrityStatus.Corrupted);
        SnapshotHistorySummary = corrupted == 0
            ? $"{entries.Count} 个快照，全部校验有效"
            : $"{entries.Count} 个快照 · {corrupted} 个损坏";
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
            if (snapshot.Manifest is not null)
            {
                _currentCharacters.TryGetValue(
                    (snapshot.Manifest.Source.ProfileId, snapshot.Manifest.Source.CharacterFolder),
                    out target);
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
                ? "未发现快照对应的本地角色；当前只展示快照内容"
                : $"恢复预览：{changed} 个文件将发生变化，" +
                  $"{preview.Files.Count - changed} 个文件相同";
            StatusMessage = "快照已重新校验，恢复预览已生成。";
        }
        catch (Exception exception)
        {
            SnapshotPreviewFiles.Clear();
            SnapshotPreviewTitle = "无法生成预览";
            StatusMessage = $"快照预览失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
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

    private bool CanRunCommand() => !IsBusy;
}

public sealed record GameRegionOption(GameRegion Region, string DisplayName);

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
        Completeness = character.Completeness * 100;
    }

    public string ProfileName { get; }

    public string FolderName { get; }

    public string LastModified { get; }

    public string FileSummary { get; }

    public double Completeness { get; }

    [ObservableProperty]
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

    private SnapshotRowViewModel(
        SnapshotLibraryEntry entry,
        string? alias,
        Func<SnapshotLibraryEntry, Task> preview)
    {
        _entry = entry;
        _preview = preview;
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
            Path.GetFileName(entry.ArchivePath));
    }

    public string CharacterName { get; }

    public string CharacterFolder { get; }

    public string ProfileName { get; }

    public string CreatedAt { get; }

    public string FileSummary { get; }

    public string IntegrityText { get; }

    public string ErrorSummary { get; }

    public bool CanPreview { get; }

    public string SearchText { get; }

    public static SnapshotRowViewModel From(
        SnapshotLibraryEntry entry,
        string? alias,
        Func<SnapshotLibraryEntry, Task> preview) =>
        new(entry, alias, preview);

    [RelayCommand(CanExecute = nameof(CanPreviewSnapshot))]
    private Task PreviewAsync() => _preview(_entry);

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
