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
    IReadOnlyList<string> Errors);

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

    Task<AppearanceBackupEntry> CreateBackupAsync(
        string sourceFilePath,
        string libraryRoot,
        AppearanceBackupReason reason = AppearanceBackupReason.Manual,
        CancellationToken cancellationToken = default);

    Task<AppearanceRestoreResult> RestoreAsync(
        AppearanceBackupEntry backup,
        string targetConfigRoot,
        int targetSlot,
        string libraryRoot,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
