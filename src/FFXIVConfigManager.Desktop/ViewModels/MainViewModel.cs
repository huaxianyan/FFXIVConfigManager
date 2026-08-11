using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Desktop.Services;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Desktop.ViewModels;

public partial class MainViewModel(
    ScanProfilesUseCase scanProfiles,
    SettingsService settingsService,
    IFolderPickerService folderPicker) : ViewModelBase
{
    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    public IReadOnlyList<GameRegionOption> RegionOptions { get; } =
    [
        new(GameRegion.International, "国际服"),
        new(GameRegion.China, "国服"),
        new(GameRegion.Custom, "其他"),
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddProfileCommand))]
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
            var aliases = settings.CharacterAliases.ToDictionary(
                item => (item.ProfileId, item.CharacterFolder),
                item => item.Alias);
            var results = await scanProfiles.ExecuteAsync(cancellationToken);

            Characters.Clear();
            Profiles.Clear();

            if (results.Count == 0)
            {
                ProfileName = "没有可用的配置源";
                ConfigRoot = "可在下方添加自定义配置目录。";
                Summary = "0 个角色";
                StatusMessage = "当前未配置任何 FFXIV 配置目录。";
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
                    aliases.TryGetValue(
                        (result.Profile.Id, character.FolderName.Value),
                        out var alias);
                    Characters.Add(CharacterRowViewModel.From(
                        result.Profile,
                        character,
                        alias,
                        SaveAliasAsync));
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
        var selectedPath = await folderPicker.PickConfigRootAsync(cancellationToken);
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
    private readonly Func<Guid, CharacterFolderName, string, Task> _saveAlias;

    private CharacterRowViewModel(
        GameProfile profile,
        CharacterConfiguration character,
        string? alias,
        Func<Guid, CharacterFolderName, string, Task> saveAlias)
    {
        _profileId = profile.Id;
        _characterFolder = character.FolderName;
        _saveAlias = saveAlias;
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
        Func<Guid, CharacterFolderName, string, Task> saveAlias) =>
        new(profile, character, alias, saveAlias);

    [RelayCommand]
    private Task SaveAliasAsync() => _saveAlias(_profileId, _characterFolder, Alias);
}
