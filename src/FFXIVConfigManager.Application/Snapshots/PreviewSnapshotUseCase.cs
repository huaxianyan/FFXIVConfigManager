using FFXIVConfigManager.Domain.Characters;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed record StableFileDigest(long Size, string Sha256);

public interface IStableFileHashService
{
    Task<StableFileDigest?> TryComputeAsync(
        string path,
        CancellationToken cancellationToken = default);
}

public enum SnapshotFileDifference
{
    Identical,
    Different,
    MissingFromTarget,
    TargetUnavailable,
}

public sealed record SnapshotFilePreview(
    string FileName,
    long SnapshotSize,
    SnapshotFileDifference Difference);

public sealed record SnapshotPreview(
    SnapshotLibraryEntry Snapshot,
    CharacterConfiguration? Target,
    IReadOnlyList<SnapshotFilePreview> Files);

public sealed class PreviewSnapshotUseCase(
    ISnapshotArchiveService archiveService,
    IStableFileHashService fileHashService)
{
    public async Task<SnapshotPreview> ExecuteAsync(
        SnapshotLibraryEntry snapshot,
        CharacterConfiguration? target,
        CancellationToken cancellationToken = default)
    {
        var verification = await archiveService.VerifyAsync(
            snapshot.ArchivePath,
            cancellationToken);
        if (!verification.IsValid || verification.Manifest is null)
        {
            throw new InvalidDataException(
                $"快照已损坏：{string.Join("；", verification.Errors)}");
        }

        var files = new List<SnapshotFilePreview>(verification.Manifest.Files.Count);
        foreach (var entry in verification.Manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (target is null)
            {
                files.Add(new SnapshotFilePreview(
                    entry.OriginalFileName,
                    entry.Size,
                    SnapshotFileDifference.TargetUnavailable));
                continue;
            }

            var targetPath = Path.Combine(target.FullPath, entry.OriginalFileName);
            var current = await fileHashService.TryComputeAsync(targetPath, cancellationToken);
            if (current is null)
            {
                files.Add(new SnapshotFilePreview(
                    entry.OriginalFileName,
                    entry.Size,
                    SnapshotFileDifference.MissingFromTarget));
                continue;
            }

            var difference = current.Size == entry.Size &&
                             string.Equals(
                                 current.Sha256,
                                 entry.Sha256,
                                 StringComparison.OrdinalIgnoreCase)
                ? SnapshotFileDifference.Identical
                : SnapshotFileDifference.Different;
            files.Add(new SnapshotFilePreview(
                entry.OriginalFileName,
                entry.Size,
                difference));
        }

        return new SnapshotPreview(snapshot, target, files);
    }
}
