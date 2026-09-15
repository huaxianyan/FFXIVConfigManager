using FFXIVConfigManager.Domain.Appearances;
using FFXIVConfigManager.Domain.Portraits;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Backups;

/// <summary>
/// 归档所在的目录层级：手动目录或 automatic/ 自动目录。只描述位置，不描述归属。
/// </summary>
public enum BackupCategory
{
    Manual,
    Automatic,
}

/// <summary>
/// 备份的归属，对应设置页「自动备份」里的功能开关。
/// <see cref="Manual"/> 是用户显式创建的手动备份：默认永不清理，
/// 只有设置页「手动备份清理」开启后，角色配置那一部分才按保留数量淘汰。
/// </summary>
public enum BackupRule
{
    Manual,
    CharacterRestore,
    CharacterMigration,
    Appearance,
    Portrait,
}

public static class BackupRules
{
    public static bool IsAutomatic(this BackupRule rule) => rule != BackupRule.Manual;

    /// <summary>把归属还原成目录层级，供只需要区分手动与自动目录的校验调用使用。</summary>
    public static BackupCategory ToPathCategory(this BackupRule rule) =>
        rule.IsAutomatic() ? BackupCategory.Automatic : BackupCategory.Manual;

    /// <summary>
    /// 角色配置备份的归属。落在手动目录且类型为手动时才算手动备份，
    /// 否则按类型区分恢复前与迁移前；迁移来源快照随迁移一起计数。
    /// </summary>
    public static BackupRule ResolveCharacter(
        BackupCategory pathCategory,
        SnapshotReason? reason)
    {
        if (pathCategory == BackupCategory.Manual &&
            reason is null or SnapshotReason.Manual)
        {
            return BackupRule.Manual;
        }

        return reason switch
        {
            SnapshotReason.BeforeMigration or SnapshotReason.MigrationSource =>
                BackupRule.CharacterMigration,
            _ => BackupRule.CharacterRestore,
        };
    }

    /// <summary>
    /// 角色形象备份的归属。迁移时程序自动创建的来源备份类型仍是手动，
    /// 依靠 automatic/ 目录把它归入形象规则。
    /// </summary>
    public static BackupRule ResolveAppearance(
        BackupCategory pathCategory,
        AppearanceBackupReason? reason)
    {
        if (pathCategory == BackupCategory.Manual &&
            reason is null or AppearanceBackupReason.Manual)
        {
            return BackupRule.Manual;
        }

        return BackupRule.Appearance;
    }

    public static BackupRule ResolvePortrait(
        BackupCategory pathCategory,
        PortraitBackupReason? reason)
    {
        if (pathCategory == BackupCategory.Manual &&
            reason is null or PortraitBackupReason.Manual)
        {
            return BackupRule.Manual;
        }

        return BackupRule.Portrait;
    }
}
