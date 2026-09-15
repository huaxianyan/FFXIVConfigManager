using FFXIVConfigManager.Application.Backups;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Snapshots;

public enum SnapshotIntegrityStatus
{
    Valid,
    Corrupted,
}

public sealed record SnapshotLibraryEntry(
    string ArchivePath,
    long ArchiveSize,
    DateTimeOffset ArchiveLastWriteTimeUtc,
    SnapshotIntegrityStatus IntegrityStatus,
    SnapshotManifest? Manifest,
    IReadOnlyList<string> Errors,
    BackupRule Rule)
{
    public DateTimeOffset CreatedAtUtc =>
        Manifest?.CreatedAtUtc ?? ArchiveLastWriteTimeUtc;

    public bool IsAutomatic => Rule.IsAutomatic();
}

public interface ISnapshotLibraryReader
{
    Task<IReadOnlyList<SnapshotLibraryEntry>> ScanAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default);
}
