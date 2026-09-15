using FFXIVConfigManager.Application.Backups;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Settings;

public sealed record CharacterAliasSetting(
    Guid ProfileId,
    string CharacterFolder,
    string Alias);

/// <summary>
/// 单个备份类别的开关与保留数量：四类自动恢复点各有一份，角色配置的手动备份共用一份。
/// 关闭时保留数量原样保存但不生效，已有的备份也不会被清理。
/// </summary>
public sealed record BackupRetentionSetting(
    bool Enabled,
    int RetentionCount)
{
    public const int DefaultRetentionCount = 5;
    public const int DefaultManualRetentionCount = 20;
    public const int MinimumRetentionCount = 1;
    public const int MaximumRetentionCount = 100;

    /// <summary>自动恢复点的默认设置：开启，保留 5 份。</summary>
    public static BackupRetentionSetting Default { get; } =
        new(true, DefaultRetentionCount);

    /// <summary>手动备份清理的默认设置：默认关闭，保留 20 份。</summary>
    public static BackupRetentionSetting ManualDefault { get; } =
        new(false, DefaultManualRetentionCount);

    /// <summary>配置缺失或越界时回落到默认值，保证保留数量始终有效。</summary>
    public int EffectiveRetentionCount => RetentionCount < MinimumRetentionCount
        ? DefaultRetentionCount
        : Math.Clamp(RetentionCount, MinimumRetentionCount, MaximumRetentionCount);
}

/// <summary>
/// 控制哪些覆盖类操作会在执行前自动创建恢复点，以及每类自动备份各自保留多少份。
/// 关闭某一项只是不再生成恢复点，事务日志、暂存写入和失败回滚不受影响。
/// </summary>
public sealed record AutomaticBackupPolicy
{
    public static AutomaticBackupPolicy AllEnabled { get; } = new();

    public BackupRetentionSetting BeforeCharacterRestore { get; init; } =
        BackupRetentionSetting.Default;

    public BackupRetentionSetting BeforeCharacterMigration { get; init; } =
        BackupRetentionSetting.Default;

    public BackupRetentionSetting BeforeAppearanceRestore { get; init; } =
        BackupRetentionSetting.Default;

    public BackupRetentionSetting BeforePortraitTransfer { get; init; } =
        BackupRetentionSetting.Default;

    public BackupRetentionSetting For(BackupRule rule) => rule switch
    {
        BackupRule.CharacterRestore => BeforeCharacterRestore,
        BackupRule.CharacterMigration => BeforeCharacterMigration,
        BackupRule.Appearance => BeforeAppearanceRestore,
        BackupRule.Portrait => BeforePortraitTransfer,
        _ => throw new ArgumentOutOfRangeException(
            nameof(rule),
            rule,
            "手动备份没有对应的自动备份功能。"),
    };
}

public sealed record ApplicationSettings(
    int SchemaVersion,
    IReadOnlyList<GameProfile> CustomProfiles,
    IReadOnlyList<CharacterAliasSetting> CharacterAliases)
{
    public const int CurrentSchemaVersion = 1;

    public string? SnapshotLibraryPath { get; init; }

    public bool ShowOnlyTaggedCharacters { get; init; }

    public bool IsUpdateProxyEnabled { get; init; }

    public string? UpdateProxyAddress { get; init; }

    public AutomaticBackupPolicy AutomaticBackups { get; init; } =
        AutomaticBackupPolicy.AllEnabled;

    /// <summary>
    /// 角色配置手动备份的清理策略。默认关闭，开启后按保留数量清理最早的手动备份；
    /// 形象与肖像的手动备份不参与清理。
    /// </summary>
    public BackupRetentionSetting ManualCharacterBackupCleanup { get; init; } =
        BackupRetentionSetting.ManualDefault;

    public static ApplicationSettings Empty { get; } =
        new(CurrentSchemaVersion, [], []);
}
