using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Infrastructure.Snapshots;

public interface ITransactionFaultInjector
{
    void BeforeReplace(string fileName, int index);
}

public sealed class NoTransactionFaultInjector : ITransactionFaultInjector
{
    public void BeforeReplace(string fileName, int index)
    {
    }
}

public sealed class TransactionalSnapshotRestorer(
    ITransactionFaultInjector? faultInjector = null) :
    ITransactionalSnapshotRestorer,
    IIncompleteRestoreRecovery
{
    private const int BufferSize = 128 * 1024;
    private const int StableReadAttempts = 3;
    private readonly ITransactionFaultInjector _faultInjector =
        faultInjector ?? new NoTransactionFaultInjector();
    private readonly StableFileHashService _hashService = new();

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<SnapshotRestoreResult> RestoreAsync(
        SnapshotRestoreRequest request,
        CancellationToken cancellationToken = default)
    {
        request.Manifest.Validate();
        if (!Directory.Exists(request.TargetDirectory))
        {
            throw new DirectoryNotFoundException($"目标角色目录不存在：{request.TargetDirectory}");
        }

        var operationId = Guid.NewGuid();
        var transactionRoot = Path.Combine(
            request.TargetDirectory,
            ".ffxivconfigmanager",
            "transactions",
            operationId.ToString("N"));
        var stagedRoot = Path.Combine(transactionRoot, "staged");
        var rollbackRoot = Path.Combine(transactionRoot, "rollback");
        var journalPath = Path.Combine(transactionRoot, "journal.json");
        Directory.CreateDirectory(stagedRoot);
        Directory.CreateDirectory(rollbackRoot);

        var journal = new RestoreJournal
        {
            OperationId = operationId,
            ArchivePath = request.ArchivePath,
            TargetDirectory = Path.GetFullPath(request.TargetDirectory),
            State = RestoreTransactionState.Preparing,
            Files = request.Manifest.Files
                .Select(file => new RestoreJournalItem
                {
                    FileName = file.OriginalFileName,
                    ExpectedSha256 = file.Sha256,
                    State = RestoreFileState.Pending,
                    HadOriginal = false,
                })
                .ToList(),
        };
        await SaveJournalAsync(journalPath, journal, cancellationToken);

        try
        {
            await ExtractAndValidateAsync(
                request.ArchivePath,
                request.Manifest,
                stagedRoot,
                cancellationToken);
            await PrepareRollbackCopiesAsync(
                request.TargetDirectory,
                rollbackRoot,
                journalPath,
                journal,
                cancellationToken);

            journal.State = RestoreTransactionState.Committing;
            await SaveJournalAsync(journalPath, journal, cancellationToken);

            for (var index = 0; index < journal.Files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = journal.Files[index];
                _faultInjector.BeforeReplace(item.FileName, index);

                var stagedPath = Path.Combine(stagedRoot, item.FileName);
                var targetPath = Path.Combine(request.TargetDirectory, item.FileName);
                File.Move(stagedPath, targetPath, overwrite: true);
                item.State = RestoreFileState.Committed;
                await SaveJournalAsync(journalPath, journal, cancellationToken);

                var digest = await _hashService.TryComputeAsync(targetPath, cancellationToken);
                if (digest is null ||
                    !string.Equals(digest.Sha256, item.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"写入后校验失败：{item.FileName}");
                }
            }

            journal.State = RestoreTransactionState.Completed;
            await SaveJournalAsync(journalPath, journal, cancellationToken);
            TryDeleteDirectory(transactionRoot);
            TryDeleteEmptyParents(request.TargetDirectory);
            return new SnapshotRestoreResult(operationId, journal.Files.Count);
        }
        catch (Exception exception)
        {
            var rollbackErrors = await RollbackAsync(
                request.TargetDirectory,
                rollbackRoot,
                journalPath,
                journal);
            var rollbackCompleted = rollbackErrors.Count == 0;

            if (rollbackCompleted)
            {
                TryDeleteDirectory(transactionRoot);
                TryDeleteEmptyParents(request.TargetDirectory);
                if (exception is OperationCanceledException)
                {
                    throw;
                }
            }

            var message = rollbackCompleted
                ? $"恢复失败，目标配置已回滚：{exception.Message}"
                : $"恢复失败且回滚不完整，原始错误：{exception.Message}，回滚错误：{string.Join("；", rollbackErrors)}";
            throw new SnapshotRestoreException(message, rollbackCompleted, exception);
        }
    }

    public async Task<IReadOnlyList<InterruptedRestoreRecoveryResult>> RecoverAsync(
        IEnumerable<string> targetDirectories,
        CancellationToken cancellationToken = default)
    {
        var results = new List<InterruptedRestoreRecoveryResult>();
        foreach (var target in targetDirectories.Distinct(GetPathComparer()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedTarget = Path.GetFullPath(target);
            var transactionsRoot = Path.Combine(
                normalizedTarget,
                ".ffxivconfigmanager",
                "transactions");
            if (!Directory.Exists(transactionsRoot))
            {
                continue;
            }

            foreach (var transactionDirectory in Directory.EnumerateDirectories(transactionsRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var journalPath = Path.Combine(transactionDirectory, "journal.json");
                RestoreJournal? journal = null;

                try
                {
                    await using (var stream = new FileStream(
                                     journalPath,
                                     FileMode.Open,
                                     FileAccess.Read,
                                     FileShare.Read,
                                     16 * 1024,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        journal = await JsonSerializer.DeserializeAsync<RestoreJournal>(
                                stream,
                                SerializerOptions,
                                cancellationToken)
                            ?? throw new InvalidDataException("恢复事务日志内容为空。");
                    }

                    ValidateRecoveryJournal(journal, normalizedTarget);

                    if (journal.State is RestoreTransactionState.Completed or
                        RestoreTransactionState.RolledBack ||
                        journal.Files.All(file => file.State != RestoreFileState.Committed))
                    {
                        TryDeleteDirectory(transactionDirectory);
                        results.Add(new InterruptedRestoreRecoveryResult(
                            journal.OperationId,
                            normalizedTarget,
                            Recovered: true,
                            Errors: []));
                        continue;
                    }

                    var errors = await RollbackAsync(
                        normalizedTarget,
                        Path.Combine(transactionDirectory, "rollback"),
                        journalPath,
                        journal);
                    if (errors.Count == 0)
                    {
                        TryDeleteDirectory(transactionDirectory);
                    }

                    results.Add(new InterruptedRestoreRecoveryResult(
                        journal.OperationId,
                        normalizedTarget,
                        errors.Count == 0,
                        errors));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    JsonException or
                    InvalidDataException)
                {
                    var operationId = journal?.OperationId;
                    if (operationId is null &&
                        Guid.TryParseExact(Path.GetFileName(transactionDirectory), "N", out var parsed))
                    {
                        operationId = parsed;
                    }

                    results.Add(new InterruptedRestoreRecoveryResult(
                        operationId ?? Guid.Empty,
                        normalizedTarget,
                        Recovered: false,
                        Errors: [$"无法恢复中断事务：{exception.Message}"]));
                }
            }

            TryDeleteEmptyParents(normalizedTarget);
        }

        return results;
    }

    private static async Task ExtractAndValidateAsync(
        string archivePath,
        SnapshotManifest manifest,
        string stagedRoot,
        CancellationToken cancellationToken)
    {
        await using var archiveFile = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.RandomAccess);
        using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: false);
        var entries = archive.Entries.ToDictionary(entry => entry.FullName, StringComparer.Ordinal);

        foreach (var expected in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(expected.ArchivePath, out var entry))
            {
                throw new InvalidDataException($"备份缺少文件：{expected.ArchivePath}");
            }

            var stagedPath = Path.Combine(stagedRoot, expected.OriginalFileName);
            await using var source = entry.Open();
            await using var output = new FileStream(
                stagedPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            long length = 0;
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                length += read;
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await output.FlushAsync(cancellationToken);
            var actualHash = Convert.ToHexString(hash.GetHashAndReset());
            if (length != expected.Size ||
                !string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"解压校验失败：{expected.OriginalFileName}");
            }
        }
    }

    private static async Task PrepareRollbackCopiesAsync(
        string targetRoot,
        string rollbackRoot,
        string journalPath,
        RestoreJournal journal,
        CancellationToken cancellationToken)
    {
        foreach (var item in journal.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = Path.Combine(targetRoot, item.FileName);
            if (File.Exists(targetPath))
            {
                var rollbackPath = Path.Combine(rollbackRoot, item.FileName);
                item.OriginalSha256 = await CopyStableFileAsync(
                    targetPath,
                    rollbackPath,
                    cancellationToken);
                item.HadOriginal = true;
            }

            item.State = RestoreFileState.Prepared;
            await SaveJournalAsync(journalPath, journal, cancellationToken);
        }
    }

    private static async Task<string> CopyStableFileAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= StableReadAttempts; attempt++)
        {
            var before = GetFileVersion(sourcePath);
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            await using var source = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var target = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            int read;

            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }

            await target.FlushAsync(cancellationToken);
            var after = GetFileVersion(sourcePath);
            if (before == after && new FileInfo(targetPath).Length == after.Length)
            {
                return Convert.ToHexString(hash.GetHashAndReset());
            }

            if (attempt < StableReadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
        }

        throw new IOException($"目标文件持续变化，无法创建回滚副本：{Path.GetFileName(sourcePath)}");
    }

    private async Task<IReadOnlyList<string>> RollbackAsync(
        string targetRoot,
        string rollbackRoot,
        string journalPath,
        RestoreJournal journal)
    {
        var errors = new List<string>();
        journal.State = RestoreTransactionState.RollingBack;
        await TrySaveJournalAsync(journalPath, journal);

        foreach (var item in journal.Files
                     .Where(file => file.State == RestoreFileState.Committed)
                     .Reverse())
        {
            try
            {
                var targetPath = Path.Combine(targetRoot, item.FileName);
                if (item.HadOriginal)
                {
                    var rollbackPath = Path.Combine(rollbackRoot, item.FileName);
                    var rollbackTemp = Path.Combine(targetRoot, $".{item.FileName}.{journal.OperationId:N}.rollback");
                    File.Copy(rollbackPath, rollbackTemp, overwrite: true);
                    File.Move(rollbackTemp, targetPath, overwrite: true);
                    var restored = await _hashService.TryComputeAsync(targetPath);
                    if (restored is null ||
                        !string.Equals(
                            restored.Sha256,
                            item.OriginalSha256,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"回滚后校验失败：{item.FileName}");
                    }
                }
                else if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                item.State = RestoreFileState.RolledBack;
                await TrySaveJournalAsync(journalPath, journal);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{item.FileName}：{exception.Message}");
            }
        }

        journal.State = errors.Count == 0
            ? RestoreTransactionState.RolledBack
            : RestoreTransactionState.RollbackFailed;
        await TrySaveJournalAsync(journalPath, journal);
        return errors;
    }

    private static async Task SaveJournalAsync(
        string journalPath,
        RestoreJournal journal,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{journalPath}.tmp";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         16 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                journal,
                SerializerOptions,
                cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, journalPath, overwrite: true);
    }

    private static async Task TrySaveJournalAsync(string path, RestoreJournal journal)
    {
        try
        {
            await SaveJournalAsync(path, journal, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void ValidateRecoveryJournal(
        RestoreJournal? journal,
        string expectedTarget)
    {
        if (journal is null ||
            journal.OperationId == Guid.Empty ||
            !GetPathComparer().Equals(
                Path.GetFullPath(journal.TargetDirectory),
                expectedTarget) ||
            journal.Files.Count == 0 ||
            journal.Files.Any(file =>
                string.IsNullOrWhiteSpace(file.FileName) ||
                Path.GetFileName(file.FileName) != file.FileName ||
                file.FileName.Contains('/') ||
                file.FileName.Contains('\\')))
        {
            throw new InvalidDataException("恢复事务日志无效。");
        }
    }

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static FileVersion GetFileVersion(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException("目标配置文件不存在。", path);
        }

        return new FileVersion(
            file.Length,
            new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero));
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteEmptyParents(string targetDirectory)
    {
        var applicationDirectory = Path.Combine(targetDirectory, ".ffxivconfigmanager");
        try
        {
            var transactionsDirectory = Path.Combine(applicationDirectory, "transactions");
            if (Directory.Exists(transactionsDirectory) &&
                !Directory.EnumerateFileSystemEntries(transactionsDirectory).Any())
            {
                Directory.Delete(transactionsDirectory);
            }

            if (Directory.Exists(applicationDirectory) &&
                !Directory.EnumerateFileSystemEntries(applicationDirectory).Any())
            {
                Directory.Delete(applicationDirectory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record FileVersion(long Length, DateTimeOffset LastWriteTimeUtc);
}
