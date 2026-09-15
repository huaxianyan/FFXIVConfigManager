using FFXIVConfigManager.Application.Backups;
using FFXIVConfigManager.Domain.Appearances;

namespace FFXIVConfigManager.Application.Appearances;

public enum AppearanceBackupIntegrity
{
    Valid,
    Corrupted,
}

public sealed record AppearanceSlot(
    int Slot,
    string FilePath,
    AppearanceMetadata? Appearance,
    string? Error);

public sealed record AppearanceBackupEntry(
    string ArchivePath,
    DateTimeOffset ArchiveLastWriteTimeUtc,
    AppearanceBackupIntegrity Integrity,
    AppearanceBackupManifest? Manifest,
    IReadOnlyList<string> Errors,
    BackupRule Rule)
{
    public DateTimeOffset CreatedAtUtc =>
        Manifest?.CreatedAtUtc ?? ArchiveLastWriteTimeUtc;

    public bool IsAutomatic => Rule.IsAutomatic();
}

public sealed record AppearanceRestoreResult(
    string TargetFilePath,
    AppearanceBackupEntry? RecoveryPoint);

public interface IAppearanceBackupService
{
    Task<IReadOnlyList<AppearanceSlot>> ScanSlotsAsync(
        string configRoot,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppearanceBackupEntry>> ScanBackupsAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 创建形象备份。<paramref name="category"/> 为 null 时按 <paramref name="reason"/>
    /// 判定层级：迁移来源备份用的是 Manual 类型，需要显式传入 Automatic。
    /// </summary>
    Task<AppearanceBackupEntry> CreateBackupAsync(
        string sourceFilePath,
        string libraryRoot,
        AppearanceBackupReason reason = AppearanceBackupReason.Manual,
        BackupCategory? category = null,
        CancellationToken cancellationToken = default);

    Task<AppearanceRestoreResult> RestoreAsync(
        AppearanceBackupEntry backup,
        string targetConfigRoot,
        int targetSlot,
        string libraryRoot,
        bool createRecoveryPoint = true,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
