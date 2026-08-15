using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVConfigManager.Application.Appearances;
using FFXIVConfigManager.Domain.Appearances;

namespace FFXIVConfigManager.Infrastructure.Appearances;

public interface IAppearanceRestoreFaultInjector
{
    void AfterTargetReplaced();
}

public sealed class NoAppearanceRestoreFaultInjector : IAppearanceRestoreFaultInjector
{
    public void AfterTargetReplaced()
    {
    }
}

public sealed class ZipAppearanceBackupService(
    IAppearanceRestoreFaultInjector? faultInjector = null) : IAppearanceBackupService
{
    private const int StableReadAttempts = 3;
    private const string ArchiveExtension = ".ffxivappearance.zip";
    private readonly IAppearanceRestoreFaultInjector _faultInjector =
        faultInjector ?? new NoAppearanceRestoreFaultInjector();

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<AppearanceSlot>> ScanSlotsAsync(
        string configRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(configRoot);
        if (!Directory.Exists(root))
        {
            return [];
        }

        await RecoverInterruptedRestoresAsync(root, cancellationToken);
        var slots = new List<AppearanceSlot>();
        for (var slot = 1; slot <= AppearanceData.MaximumSlot; slot++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(root, AppearanceData.GetSlotFileName(slot));
            if (!File.Exists(path))
            {
                continue;
            }

            var data = await ReadStableAppearanceAsync(path, cancellationToken);
            AppearanceData.TryParse(data, out var appearance, out var error);
            slots.Add(new AppearanceSlot(slot, path, appearance, error));
        }

        return slots;
    }

    public async Task<IReadOnlyList<AppearanceBackupEntry>> ScanBackupsAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(Path.GetFullPath(libraryRoot), "appearance-backups");
        if (!Directory.Exists(root))
        {
            return [];
        }

        var entries = new List<AppearanceBackupEntry>();
        foreach (var path in Directory.EnumerateFiles(root, $"*{ArchiveExtension}", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(await VerifyAsync(path, cancellationToken));
        }

        return entries
            .OrderByDescending(entry => entry.Manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc)
            .ToArray();
    }

    public async Task<AppearanceBackupEntry> CreateBackupAsync(
        string sourceFilePath,
        string libraryRoot,
        AppearanceBackupReason reason = AppearanceBackupReason.Manual,
        CancellationToken cancellationToken = default)
    {
        var sourcePath = Path.GetFullPath(sourceFilePath);
        var sourceFileName = Path.GetFileName(sourcePath);
        var data = await ReadStableAppearanceAsync(sourcePath, cancellationToken);
        if (!AppearanceData.TryParse(data, out var appearance, out var error))
        {
            throw new InvalidDataException($"无法备份无效的角色形象文件：{error}");
        }

        var now = DateTimeOffset.UtcNow;
        var backupId = Guid.NewGuid();
        var directory = Path.Combine(
            Path.GetFullPath(libraryRoot),
            "appearance-backups",
            now.ToString("yyyy"),
            now.ToString("MM"));
        Directory.CreateDirectory(directory);
        var archiveName = $"{now:yyyyMMddTHHmmssfffZ}_{backupId:N}{ArchiveExtension}";
        var finalPath = Path.Combine(directory, archiveName);
        var temporaryPath = Path.Combine(directory, $".{archiveName}.tmp");
        var manifest = new AppearanceBackupManifest(
            AppearanceBackupManifest.CurrentFormatVersion,
            backupId,
            now,
            reason,
            sourceFileName,
            data.Length,
            Convert.ToHexString(SHA256.HashData(data)),
            appearance!);
        manifest.Validate();

        try
        {
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var manifestEntry = archive.CreateEntry(
                    AppearanceBackupManifest.ManifestEntryName,
                    CompressionLevel.Fastest);
                await using (var stream = manifestEntry.Open())
                {
                    await JsonSerializer.SerializeAsync(
                        stream,
                        manifest,
                        SerializerOptions,
                        cancellationToken);
                }

                var dataEntry = archive.CreateEntry(
                    AppearanceBackupManifest.DataEntryName,
                    CompressionLevel.Fastest);
                await using var dataStream = dataEntry.Open();
                await dataStream.WriteAsync(data, cancellationToken);
            }

            var verification = await VerifyAsync(temporaryPath, cancellationToken);
            if (verification.Integrity != AppearanceBackupIntegrity.Valid)
            {
                throw new InvalidDataException(
                    $"新角色形象备份校验失败：{string.Join("；", verification.Errors)}");
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return verification with { ArchivePath = finalPath };
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public async Task<AppearanceRestoreResult> RestoreAsync(
        AppearanceBackupEntry backup,
        string targetConfigRoot,
        int targetSlot,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        _ = AppearanceData.GetSlotFileName(targetSlot);
        var verified = await VerifyAsync(backup.ArchivePath, cancellationToken);
        if (verified.Integrity != AppearanceBackupIntegrity.Valid || verified.Manifest is null)
        {
            throw new InvalidDataException(
                $"角色形象备份无效：{string.Join("；", verified.Errors)}");
        }

        var data = await ReadArchiveDataAsync(backup.ArchivePath, cancellationToken);
        var root = Path.GetFullPath(targetConfigRoot);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"目标配置源目录不存在：{root}");
        }

        await RecoverInterruptedRestoresAsync(root, cancellationToken);
        var targetPath = Path.Combine(root, AppearanceData.GetSlotFileName(targetSlot));
        AppearanceBackupEntry? recoveryPoint = null;
        if (File.Exists(targetPath))
        {
            recoveryPoint = await CreateBackupAsync(
                targetPath,
                libraryRoot,
                AppearanceBackupReason.BeforeRestore,
                cancellationToken);
        }

        var operationId = Guid.NewGuid();
        var transactionRoot = Path.Combine(
            root,
            ".ffxivconfigmanager",
            "appearance-transactions",
            operationId.ToString("N"));
        var stagedPath = Path.Combine(transactionRoot, "staged.dat");
        var rollbackPath = Path.Combine(transactionRoot, "rollback.dat");
        var journalPath = Path.Combine(transactionRoot, "journal.json");
        Directory.CreateDirectory(transactionRoot);
        var journal = new AppearanceRestoreJournal(
            operationId,
            targetPath,
            rollbackPath,
            File.Exists(targetPath),
            Convert.ToHexString(SHA256.HashData(data)),
            AppearanceRestoreState.Preparing);

        try
        {
            await WriteThroughAsync(stagedPath, data, cancellationToken);
            if (journal.HadOriginal)
            {
                var original = await ReadStableAppearanceAsync(targetPath, cancellationToken);
                await WriteThroughAsync(rollbackPath, original, cancellationToken);
            }

            journal = journal with { State = AppearanceRestoreState.Committing };
            await SaveJournalAsync(journalPath, journal, cancellationToken);
            File.Move(stagedPath, targetPath, overwrite: true);
            _faultInjector.AfterTargetReplaced();

            var written = await File.ReadAllBytesAsync(targetPath, cancellationToken);
            if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(written)),
                    journal.ExpectedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("角色形象写入后校验失败。");
            }

            TryDeleteDirectory(transactionRoot);
            TryDeleteEmptyTransactionParents(root);
            return new AppearanceRestoreResult(targetPath, recoveryPoint);
        }
        catch (Exception exception)
        {
            var rollbackError = await TryRollbackAsync(journal, CancellationToken.None);
            if (rollbackError is null)
            {
                TryDeleteDirectory(transactionRoot);
                TryDeleteEmptyTransactionParents(root);
                if (exception is OperationCanceledException)
                {
                    throw;
                }

                throw new IOException($"恢复失败，目标栏位已回滚：{exception.Message}", exception);
            }

            throw new IOException(
                $"恢复失败且回滚不完整：{exception.Message}；回滚错误：{rollbackError}",
                exception);
        }
    }

    public Task DeleteAsync(string archivePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Path.GetFullPath(archivePath);
        if (!path.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能删除角色形象备份文件。");
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private static async Task<AppearanceBackupEntry> VerifyAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(archivePath);
        var lastWriteTime = File.Exists(path)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        try
        {
            await using var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count != 2)
            {
                throw new InvalidDataException("备份必须且只能包含 Manifest 和角色形象数据。");
            }

            var manifestEntry = archive.GetEntry(AppearanceBackupManifest.ManifestEntryName)
                ?? throw new InvalidDataException("备份缺少 manifest.json。");
            if (manifestEntry.Length is <= 0 or > 64 * 1024)
            {
                throw new InvalidDataException("角色形象备份 Manifest 大小无效。");
            }

            AppearanceBackupManifest? manifest;
            await using (var stream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<AppearanceBackupManifest>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            }

            manifest?.Validate();
            if (manifest is null)
            {
                throw new InvalidDataException("备份 Manifest 为空。");
            }

            var dataEntry = archive.GetEntry(AppearanceBackupManifest.DataEntryName)
                ?? throw new InvalidDataException("备份缺少角色形象数据。");
            if (dataEntry.Length != AppearanceData.FileSize)
            {
                throw new InvalidDataException("角色形象数据大小无效。");
            }

            byte[] data;
            await using (var stream = dataEntry.Open())
            using (var memory = new MemoryStream(AppearanceData.FileSize))
            {
                await stream.CopyToAsync(memory, cancellationToken);
                data = memory.ToArray();
            }

            if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(data)),
                    manifest.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("角色形象数据哈希不匹配。");
            }

            if (!AppearanceData.TryParse(data, out var parsed, out var error) ||
                parsed != manifest.Appearance)
            {
                throw new InvalidDataException($"角色形象元数据不匹配：{error}");
            }

            return new AppearanceBackupEntry(
                path,
                lastWriteTime,
                AppearanceBackupIntegrity.Valid,
                manifest,
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return new AppearanceBackupEntry(
                path,
                lastWriteTime,
                AppearanceBackupIntegrity.Corrupted,
                null,
                [$"读取备份失败：{exception.Message}"]);
        }
    }

    private static async Task<byte[]> ReadArchiveDataAsync(
        string archivePath,
        CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(archivePath);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        var entry = archive.GetEntry(AppearanceBackupManifest.DataEntryName)
            ?? throw new InvalidDataException("备份缺少角色形象数据。");
        await using var stream = entry.Open();
        using var memory = new MemoryStream(AppearanceData.FileSize);
        await stream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static async Task<byte[]> ReadStableAppearanceAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= StableReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = new FileInfo(path);
            if (!before.Exists)
            {
                throw new FileNotFoundException("角色形象文件不存在。", path);
            }

            var beforeLength = before.Length;
            var beforeWrite = before.LastWriteTimeUtc;
            byte[] data;
            await using (var stream = new FileStream(
                             path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                data = new byte[stream.Length];
                await stream.ReadExactlyAsync(data, cancellationToken);
            }

            var after = new FileInfo(path);
            if (after.Exists && after.Length == beforeLength && after.LastWriteTimeUtc == beforeWrite)
            {
                return data;
            }

            if (attempt < StableReadAttempts)
            {
                await Task.Delay(300, cancellationToken);
            }
        }

        throw new IOException($"角色形象文件在读取期间持续变化：{Path.GetFileName(path)}");
    }

    private static async Task WriteThroughAsync(
        string path,
        byte[] data,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SaveJournalAsync(
        string path,
        AppearanceRestoreJournal journal,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, journal, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task RecoverInterruptedRestoresAsync(
        string configRoot,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(configRoot, ".ffxivconfigmanager", "appearance-transactions");
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var journalPath = Path.Combine(directory, "journal.json");
            if (!File.Exists(journalPath))
            {
                TryDeleteDirectory(directory);
                continue;
            }

            AppearanceRestoreJournal? journal;
            await using (var stream = File.OpenRead(journalPath))
            {
                journal = await JsonSerializer.DeserializeAsync<AppearanceRestoreJournal>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            }

            if (journal is null ||
                !Path.GetFullPath(journal.TargetPath).StartsWith(
                    Path.GetFullPath(configRoot) + Path.DirectorySeparatorChar,
                    GetPathComparison()))
            {
                throw new InvalidDataException("角色形象恢复事务日志无效。");
            }

            var error = await TryRollbackAsync(journal, cancellationToken);
            if (error is not null)
            {
                throw new IOException($"无法回滚中断的角色形象恢复：{error}");
            }

            TryDeleteDirectory(directory);
        }

        TryDeleteEmptyTransactionParents(configRoot);
    }

    private static async Task<string?> TryRollbackAsync(
        AppearanceRestoreJournal journal,
        CancellationToken cancellationToken)
    {
        try
        {
            if (journal.HadOriginal)
            {
                if (!File.Exists(journal.RollbackPath))
                {
                    throw new FileNotFoundException("找不到回滚副本。", journal.RollbackPath);
                }

                var data = await File.ReadAllBytesAsync(journal.RollbackPath, cancellationToken);
                var temporary = $"{journal.TargetPath}.{journal.OperationId:N}.rollback";
                await WriteThroughAsync(temporary, data, cancellationToken);
                File.Move(temporary, journal.TargetPath, overwrite: true);
            }
            else if (File.Exists(journal.TargetPath))
            {
                File.Delete(journal.TargetPath);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteEmptyTransactionParents(string configRoot)
    {
        var appRoot = Path.Combine(configRoot, ".ffxivconfigmanager");
        var transactions = Path.Combine(appRoot, "appearance-transactions");
        try
        {
            if (Directory.Exists(transactions) && !Directory.EnumerateFileSystemEntries(transactions).Any())
            {
                Directory.Delete(transactions);
            }

            if (Directory.Exists(appRoot) && !Directory.EnumerateFileSystemEntries(appRoot).Any())
            {
                Directory.Delete(appRoot);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private enum AppearanceRestoreState
    {
        Preparing,
        Committing,
    }

    private sealed record AppearanceRestoreJournal(
        Guid OperationId,
        string TargetPath,
        string RollbackPath,
        bool HadOriginal,
        string ExpectedSha256,
        AppearanceRestoreState State);
}
