using FFXIVConfigManager.Application.Snapshots;

namespace FFXIVConfigManager.Infrastructure.Snapshots;

public sealed class PhysicalSnapshotLibraryReader(
    ISnapshotArchiveService archiveService,
    int maximumConcurrency = 2) : ISnapshotLibraryReader
{
    public async Task<IReadOnlyList<SnapshotLibraryEntry>> ScanAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        if (maximumConcurrency <= 0)
        {
            throw new InvalidOperationException("快照扫描并发数必须大于零。");
        }

        var snapshotsRoot = Path.Combine(Path.GetFullPath(libraryRoot), "snapshots");
        if (!Directory.Exists(snapshotsRoot))
        {
            return [];
        }

        var archivePaths = Directory
            .EnumerateFiles(
                snapshotsRoot,
                "*.ffxivconfig.zip",
                SearchOption.AllDirectories)
            .ToArray();
        using var concurrency = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        var tasks = archivePaths.Select(path =>
            ReadEntryAsync(path, concurrency, cancellationToken));

        return await Task.WhenAll(tasks);
    }

    private async Task<SnapshotLibraryEntry> ReadEntryAsync(
        string path,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);
        try
        {
            var file = new FileInfo(path);
            var size = file.Exists ? file.Length : 0;
            var lastWriteTime = file.Exists
                ? new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)
                : DateTimeOffset.MinValue;
            var verification = await archiveService.VerifyAsync(path, cancellationToken);

            return new SnapshotLibraryEntry(
                Path.GetFullPath(path),
                size,
                lastWriteTime,
                verification.IsValid
                    ? SnapshotIntegrityStatus.Valid
                    : SnapshotIntegrityStatus.Corrupted,
                verification.Manifest,
                verification.Errors);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SnapshotLibraryEntry(
                Path.GetFullPath(path),
                0,
                DateTimeOffset.MinValue,
                SnapshotIntegrityStatus.Corrupted,
                null,
                [$"读取快照失败：{exception.Message}"]);
        }
        finally
        {
            concurrency.Release();
        }
    }
}
