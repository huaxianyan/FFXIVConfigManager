namespace FFXIVConfigManager.Infrastructure.Settings;

public static class ApplicationDataPaths
{
    public static string GetDefaultDirectory()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData))
        {
            throw new InvalidOperationException("无法确定当前平台的本地应用数据目录。");
        }

        return Path.Combine(localData, "FFXIVConfigManager");
    }

    public static string GetDefaultSettingsPath() =>
        Path.Combine(GetDefaultDirectory(), "settings.json");
}
