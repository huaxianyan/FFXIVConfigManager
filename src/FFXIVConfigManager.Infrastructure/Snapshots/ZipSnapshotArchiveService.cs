using System.Buffers;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Infrastructure.Snapshots;

public sealed class ZipSnapshotArchiveService : ISnapshotArchiveService
{
    private const int BufferSize = 128 * 1024;
    private const int MaximumEntries = 500;
    private const long MaximumManifestSize = 1024 * 1024;
    private const long MaximumSingleFileSize = 64L * 1024 * 1024;
    private const long MaximumTotalSize = 512L * 1024 * 1024;
    private const int StableReadAttempts = 3;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<CreatedSnapshot> CreateAsync(
        SnapshotArchiveRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var libraryRoot = Path.GetFullPath(request.LibraryRoot);
        var stagingDirectory = Path.Combine(libraryRoot, ".staging", request.SnapshotId.ToString("N"));
        var targetDirectory = Path.Combine(
            libraryRoot,
            "snapshots",
            request.CreatedAtUtc.ToString("yyyy"),
            request.CreatedAtUtc.ToString("MM"));
        var archiveName =
            $"{request.CreatedAtUtc:yyyyMMddTHHmmssZ}_{request.Source.CharacterFolder}_{request.SnapshotId:N}.ffxivconfig.zip";
        var finalArchivePath = Path.Combine(targetDirectory, archiveName);
        var temporaryArchivePath = Path.Combine(targetDirectory, $".{archiveName}.tmp");

        Directory.CreateDirectory(stagingDirectory);
        Directory.CreateDirectory(targetDirectory);

        try
        {
            var stagedFiles = new List<StagedFile>(request.Files.Count);
            foreach (var source in request.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stagedPath = Path.Combine(stagingDirectory, source.OriginalFileName);
                stagedFiles.Add(await StageStableFileAsync(
                    source,
                    stagedPath,
                    cancellationToken));
            }

            var manifest = new SnapshotManifest(
                SnapshotManifest.CurrentFormatVersion,
                request.SnapshotId,
                request.CreatedAtUtc,
                request.Reason,
                request.Source,
                stagedFiles.Select(file => file.Entry).ToArray());
            manifest.Validate();

            await WriteArchiveAsync(
                temporaryArchivePath,
                manifest,
                stagedFiles,
                cancellationToken);

            var verification = await VerifyAsync(temporaryArchivePath, cancellationToken);
            if (!verification.IsValid)
            {
                throw new InvalidDataException(
                    $"新快照完整性校验失败：{string.Join("；", verification.Errors)}");
            }

            File.Move(temporaryArchivePath, finalArchivePath, overwrite: false);
            return new CreatedSnapshot(finalArchivePath, manifest);
        }
        finally
        {
            TryDeleteFile(temporaryArchivePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    public async Task<SnapshotVerificationResult> VerifyAsync(
        string archivePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(archivePath))
            {
                return SnapshotVerificationResult.Invalid("快照文件不存在。");
            }

            await using var file = new FileStream(
                archivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);

            if (archive.Entries.Count is 0 or > MaximumEntries)
            {
                return SnapshotVerificationResult.Invalid("快照条目数量超出允许范围。");
            }

            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entries.TryAdd(entry.FullName, entry))
                {
                    return SnapshotVerificationResult.Invalid($"快照包含重复条目：{entry.FullName}");
                }
            }

            if (!entries.TryGetValue(SnapshotManifest.ManifestEntryName, out var manifestEntry) ||
                manifestEntry.Length <= 0 ||
                manifestEntry.Length > MaximumManifestSize)
            {
                return SnapshotVerificationResult.Invalid("快照缺少有效的 manifest.json。");
            }

            SnapshotManifest? manifest;
            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<SnapshotManifest>(
                    manifestStream,
                    SerializerOptions,
                    cancellationToken);
            }

            if (manifest is null)
            {
                return SnapshotVerificationResult.Invalid("无法读取快照 Manifest。");
            }

            manifest.Validate();
            var expectedEntryNames = manifest.Files
                .Select(item => item.ArchivePath)
                .Append(SnapshotManifest.ManifestEntryName)
                .ToHashSet(StringComparer.Ordinal);

            if (entries.Keys.Any(name => !expectedEntryNames.Contains(name)))
            {
                return SnapshotVerificationResult.Invalid("快照包含 Manifest 未声明的条目。");
            }

            long totalSize = 0;
            foreach (var expected in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!entries.TryGetValue(expected.ArchivePath, out var entry))
                {
                    return SnapshotVerificationResult.Invalid(
                        $"快照缺少文件：{expected.ArchivePath}");
                }

                if (entry.Length != expected.Size || entry.Length > MaximumSingleFileSize)
                {
                    return SnapshotVerificationResult.Invalid(
                        $"快照文件大小不匹配：{expected.ArchivePath}");
                }

                totalSize = checked(totalSize + entry.Length);
                if (totalSize > MaximumTotalSize)
                {
                    return SnapshotVerificationResult.Invalid("快照解压后总大小超出允许范围。");
                }

                await using var entryStream = entry.Open();
                var actualHash = await ComputeSha256Async(entryStream, cancellationToken);
                if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return SnapshotVerificationResult.Invalid(
                        $"快照文件哈希不匹配：{expected.ArchivePath}");
                }
            }

            return SnapshotVerificationResult.Valid(manifest);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidDataException or
            JsonException or
            NotSupportedException or
            OverflowException or
            UnauthorizedAccessException)
        {
            return SnapshotVerificationResult.Invalid($"快照校验失败：{exception.Message}");
        }
    }

    private static async Task<StagedFile> StageStableFileAsync(
        SnapshotFileSource source,
        string stagedPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= StableReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = GetFileVersion(source.SourcePath);
            TryDeleteFile(stagedPath);

            string hash;
            await using (var input = new FileStream(
                             source.SourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(
                             stagedPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                hash = await CopyAndHashAsync(input, output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            var after = GetFileVersion(source.SourcePath);
            var stagedLength = new FileInfo(stagedPath).Length;
            if (before == after && stagedLength == after.Length)
            {
                return new StagedFile(
                    stagedPath,
                    new SnapshotFileEntry(
                        source.ArchivePath,
                        source.OriginalFileName,
                        stagedLength,
                        after.LastWriteTimeUtc,
                        hash));
            }

            if (attempt < StableReadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
        }

        throw new IOException($"文件在读取期间持续变化，无法获得稳定副本：{source.OriginalFileName}");
    }

    private static async Task WriteArchiveAsync(
        string archivePath,
        SnapshotManifest manifest,
        IReadOnlyList<StagedFile> stagedFiles,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        var manifestEntry = archive.CreateEntry(
            SnapshotManifest.ManifestEntryName,
            CompressionLevel.Fastest);
        await using (var manifestStream = manifestEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                manifestStream,
                manifest,
                SerializerOptions,
                cancellationToken);
        }

        foreach (var staged in stagedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = archive.CreateEntry(staged.Entry.ArchivePath, CompressionLevel.Fastest);
            await using var entryStream = entry.Open();
            await using var sourceStream = new FileStream(
                staged.Path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await sourceStream.CopyToAsync(entryStream, BufferSize, cancellationToken);
        }
    }

    private static async Task<string> CopyAndHashAsync(
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            int read;
            while ((read = await input.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<string> ComputeSha256Async(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            int read;
            while ((read = await stream.ReadAsync(
                       buffer.AsMemory(0, buffer.Length),
                       cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static FileVersion GetFileVersion(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("配置文件不存在。", path);
        }

        return new FileVersion(
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
    }

    private static void ValidateRequest(SnapshotArchiveRequest request)
    {
        if (request.SnapshotId == Guid.Empty || request.Files.Count == 0)
        {
            throw new ArgumentException("快照请求无效。", nameof(request));
        }

        var archivePaths = new HashSet<string>(StringComparer.Ordinal);
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in request.Files)
        {
            if (Path.GetFileName(file.OriginalFileName) != file.OriginalFileName ||
                !file.ArchivePath.Equals($"files/{file.OriginalFileName}", StringComparison.Ordinal) ||
                !archivePaths.Add(file.ArchivePath) ||
                !fileNames.Add(file.OriginalFileName))
            {
                throw new ArgumentException("快照请求包含无效或重复的文件路径。", nameof(request));
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record StagedFile(string Path, SnapshotFileEntry Entry);

    private sealed record FileVersion(long Length, DateTimeOffset LastWriteTimeUtc);
}
