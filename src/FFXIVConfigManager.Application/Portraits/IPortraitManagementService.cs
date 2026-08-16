using FFXIVConfigManager.Domain.Portraits;

namespace FFXIVConfigManager.Application.Portraits;

public enum PortraitBackupIntegrity
{
    Valid,
    Corrupted,
}

public sealed record CharacterPortrait(
    string CharacterDirectory,
    int GearsetNumber,
    byte ClassJobId,
    string GearsetName,
    int BannerIndex,
    PortraitData Data);

public sealed record PortraitBackupEntry(
    string ArchivePath,
    DateTimeOffset ArchiveLastWriteTimeUtc,
    PortraitBackupIntegrity Integrity,
    PortraitBackupManifest? Manifest,
    PortraitData? Data,
    IReadOnlyList<string> Errors);

public sealed record PortraitTransferSource(
    CharacterPortrait? Character,
    PortraitBackupEntry? Backup)
{
    public static PortraitTransferSource FromCharacter(CharacterPortrait character) =>
        new(character, null);

    public static PortraitTransferSource FromBackup(PortraitBackupEntry backup) =>
        new(null, backup);
}

public sealed record PortraitTransferResult(
    string TargetFilePath,
    PortraitBackupEntry RecoveryPoint);

public interface IPortraitManagementService
{
    Task<IReadOnlyList<CharacterPortrait>> ScanCharacterAsync(
        string characterDirectory,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PortraitBackupEntry>> ScanBackupsAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default);

    Task<PortraitBackupEntry> CreateBackupAsync(
        CharacterPortrait source,
        string libraryRoot,
        string schemeName,
        string note,
        PortraitBackupReason reason = PortraitBackupReason.Manual,
        CancellationToken cancellationToken = default);

    Task<PortraitTransferResult> TransferAsync(
        PortraitTransferSource source,
        CharacterPortrait target,
        string libraryRoot,
        CancellationToken cancellationToken = default);

    Task DeleteBackupAsync(
        PortraitBackupEntry backup,
        string libraryRoot,
        CancellationToken cancellationToken = default);
}
