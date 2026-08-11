using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Domain.Characters;

namespace FFXIVConfigManager.Desktop.ViewModels;

public partial class MainViewModel(ScanProfilesUseCase scanProfiles) : ViewModelBase
{
    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "正在准备扫描配置目录……";

    [ObservableProperty]
    public partial string ProfileName { get; private set; } = "尚未发现配置源";

    [ObservableProperty]
    public partial string ConfigRoot { get; private set; } = "—";

    [ObservableProperty]
    public partial string Summary { get; private set; } = "0 个角色";

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        StatusMessage = "正在扫描角色配置……";

        try
        {
            var results = await scanProfiles.ExecuteAsync(cancellationToken);
            Characters.Clear();

            if (results.Count == 0)
            {
                ProfileName = "没有可用的默认配置源";
                ConfigRoot = "请在受支持的平台运行，或等待自定义配置源功能。";
                Summary = "0 个角色";
                StatusMessage = "当前平台尚未提供自动目录发现。";
                return;
            }

            var primaryResult = results[0];
            ProfileName = primaryResult.Profile.Name;
            ConfigRoot = primaryResult.Profile.ConfigRoot;

            foreach (var result in results)
            {
                foreach (var character in result.Characters)
                {
                    Characters.Add(CharacterRowViewModel.From(result.Profile.Name, character));
                }
            }

            Summary = $"{Characters.Count} 个角色 · {results.Count} 个配置源";
            StatusMessage = primaryResult.Issue ??
                (Characters.Count == 0
                    ? "未发现角色配置目录。登录过角色后可在这里看到配置。"
                    : $"扫描完成于 {DateTimeOffset.Now:HH:mm:ss}");
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

    private bool CanRefresh() => !IsBusy;
}

public sealed record CharacterRowViewModel(
    string ProfileName,
    string FolderName,
    string LastModified,
    string FileSummary,
    double Completeness)
{
    public static CharacterRowViewModel From(
        string profileName,
        CharacterConfiguration character) =>
        new(
            profileName,
            character.FolderName.Value,
            character.LastModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
            $"{character.ExistingFileCount}/{character.ExpectedFileCount} 个已知文件",
            character.Completeness * 100);
}
