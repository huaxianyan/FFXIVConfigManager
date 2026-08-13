namespace FFXIVConfigManager.Application.Snapshots;

public sealed class VerifySnapshotUseCase(ISnapshotArchiveService archiveService)
{
    public Task<SnapshotVerificationResult> ExecuteAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("备份路径不能为空。", nameof(archivePath));
        }

        return archiveService.VerifyAsync(archivePath, cancellationToken);
    }
}
