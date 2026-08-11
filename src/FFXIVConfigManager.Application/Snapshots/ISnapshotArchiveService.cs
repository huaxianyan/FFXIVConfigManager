using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed record SnapshotFileSource(
    string SourcePath,
    string ArchivePath,
    string OriginalFileName);

public sealed record SnapshotArchiveRequest(
    string LibraryRoot,
    Guid SnapshotId,
    DateTimeOffset CreatedAtUtc,
    SnapshotReason Reason,
    SnapshotSource Source,
    IReadOnlyList<SnapshotFileSource> Files);

public sealed record CreatedSnapshot(
    string ArchivePath,
    SnapshotManifest Manifest);

public sealed record SnapshotVerificationResult(
    bool IsValid,
    SnapshotManifest? Manifest,
    IReadOnlyList<string> Errors)
{
    public static SnapshotVerificationResult Valid(SnapshotManifest manifest) =>
        new(true, manifest, []);

    public static SnapshotVerificationResult Invalid(params string[] errors) =>
        new(false, null, errors);
}

public interface ISnapshotArchiveService
{
    Task<CreatedSnapshot> CreateAsync(
        SnapshotArchiveRequest request,
        CancellationToken cancellationToken = default);

    Task<SnapshotVerificationResult> VerifyAsync(
        string archivePath,
        CancellationToken cancellationToken = default);
}
