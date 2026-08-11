using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed record CompletedSnapshotRestore(
    CreatedSnapshot RecoveryPoint,
    SnapshotRestoreResult RestoreResult);

public sealed class RestoreSnapshotUseCase(
    ISnapshotArchiveService archiveService,
    CreateCharacterSnapshotUseCase createSnapshot,
    ITransactionalSnapshotRestorer restorer)
{
    public async Task<CompletedSnapshotRestore> ExecuteAsync(
        SnapshotLibraryEntry snapshot,
        GameProfile targetProfile,
        CharacterConfiguration target,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        if (!target.BelongsTo(targetProfile))
        {
            throw new ArgumentException("目标角色不属于指定配置源。", nameof(target));
        }

        var verification = await archiveService.VerifyAsync(
            snapshot.ArchivePath,
            cancellationToken);
        if (!verification.IsValid || verification.Manifest is null)
        {
            throw new InvalidDataException(
                $"快照完整性校验失败：{string.Join("；", verification.Errors)}");
        }

        var recoveryPoint = await createSnapshot.ExecuteAsync(
            targetProfile,
            target,
            libraryRoot,
            SnapshotReason.BeforeRestore,
            cancellationToken);

        var restoreResult = await restorer.RestoreAsync(
            new SnapshotRestoreRequest(
                snapshot.ArchivePath,
                verification.Manifest,
                target.FullPath),
            cancellationToken);

        return new CompletedSnapshotRestore(recoveryPoint, restoreResult);
    }
}
