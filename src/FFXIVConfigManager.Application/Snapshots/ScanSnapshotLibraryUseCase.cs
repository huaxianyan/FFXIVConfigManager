namespace FFXIVConfigManager.Application.Snapshots;

public sealed class ScanSnapshotLibraryUseCase(ISnapshotLibraryReader libraryReader)
{
    public async Task<IReadOnlyList<SnapshotLibraryEntry>> ExecuteAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return [];
        }

        var entries = await libraryReader.ScanAsync(libraryRoot, cancellationToken);
        return entries
            .OrderByDescending(entry =>
                entry.Manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc)
            .ThenBy(entry => entry.ArchivePath, StringComparer.Ordinal)
            .ToArray();
    }
}
