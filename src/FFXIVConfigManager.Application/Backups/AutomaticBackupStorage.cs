namespace FFXIVConfigManager.Application.Backups;

/// <summary>
/// 备份库的目录约定。自动备份写入独立的 automatic/ 子树，
/// 旧版本遗留的自动备份仍可能停留在手动目录中，由 <see cref="BackupRules"/> 按类型兜底识别。
/// </summary>
public static class AutomaticBackupStorage
{
    public const string RootDirectoryName = "automatic";
    public const string CharacterBackupsDirectoryName = "backups";
    public const string AppearanceBackupsDirectoryName = "appearance-backups";
    public const string PortraitBackupsDirectoryName = "portrait-backups";

    /// <summary>0.1.0 预览版使用过的角色备份目录，只读兼容，不再写入。</summary>
    public const string LegacyCharacterBackupsDirectoryName = "snapshots";

    public static string ResolveRoot(
        string libraryRoot,
        string directoryName,
        BackupCategory category) =>
        category == BackupCategory.Automatic
            ? Path.Combine(
                Path.GetFullPath(libraryRoot),
                RootDirectoryName,
                directoryName)
            : Path.Combine(Path.GetFullPath(libraryRoot), directoryName);

    /// <summary>
    /// 依次枚举手动目录与 automatic/ 目录中的归档，附带该归档所在层级的类别。
    /// 两个目录互不包含，同一条目不会被枚举两次。
    /// </summary>
    public static IEnumerable<(string Path, BackupCategory Category)> EnumerateArchives(
        string libraryRoot,
        string directoryName,
        string searchPattern)
    {
        foreach (var category in (BackupCategory[])[BackupCategory.Manual, BackupCategory.Automatic])
        {
            var root = ResolveRoot(libraryRoot, directoryName, category);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(
                         root,
                         searchPattern,
                         SearchOption.AllDirectories))
            {
                yield return (path, category);
            }
        }
    }
}
