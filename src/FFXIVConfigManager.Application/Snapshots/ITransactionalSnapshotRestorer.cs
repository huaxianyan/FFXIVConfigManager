using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed record SnapshotRestoreRequest(
    string ArchivePath,
    SnapshotManifest Manifest,
    string TargetDirectory);

public sealed record SnapshotRestoreResult(
    Guid OperationId,
    int RestoredFileCount);

public interface ITransactionalSnapshotRestorer
{
    Task<SnapshotRestoreResult> RestoreAsync(
        SnapshotRestoreRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class SnapshotRestoreException(
    string message,
    bool rollbackCompleted,
    Exception? innerException = null) : IOException(message, innerException)
{
    public bool RollbackCompleted { get; } = rollbackCompleted;
}
