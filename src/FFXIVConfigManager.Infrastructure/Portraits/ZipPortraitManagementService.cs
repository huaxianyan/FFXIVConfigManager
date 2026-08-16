using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVConfigManager.Application.Portraits;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Portraits;

namespace FFXIVConfigManager.Infrastructure.Portraits;

public interface IPortraitTransferFaultInjector
{
    void AfterTargetReplaced();
}

public sealed class NoPortraitTransferFaultInjector : IPortraitTransferFaultInjector
{
    public void AfterTargetReplaced()
    {
    }
}

public sealed class ZipPortraitManagementService(
    TimeProvider? timeProvider = null,
    IPortraitTransferFaultInjector? faultInjector = null) : IPortraitManagementService
{
    private const string ArchiveExtension = ".ffxivportrait.zip";
    private const int StableReadAttempts = 3;
    private const int GearsetRecordCount = 100;
    private const int GearsetRecordSize = 452;
    private const int GearsetContentOffset = 17;
    private const int BannerSegmentIndex = 23;
    private const int BannerHeaderSize = 32;
    private const byte GearsetMask = 0x73;
    private const byte UiSaveMask = 0x31;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly IPortraitTransferFaultInjector _faultInjector =
        faultInjector ?? new NoPortraitTransferFaultInjector();

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<IReadOnlyList<CharacterPortrait>> ScanCharacterAsync(
        string characterDirectory,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(characterDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"角色目录不存在：{root}");
        }

        await RecoverInterruptedTransfersAsync(root, cancellationToken);
        var gearsetBytes = await ReadStableFileAsync(
            Path.Combine(root, "GEARSET.DAT"),
            cancellationToken);
        var uiSaveBytes = await ReadStableFileAsync(
            Path.Combine(root, "UISAVE.DAT"),
            cancellationToken);
        var gearsets = ParseGearsets(gearsetBytes);
        var uiSave = ParseUiSave(uiSaveBytes);
        var portraits = new List<CharacterPortrait>();

        foreach (var gearset in gearsets.Where(item => item.BannerIndex >= 0))
        {
            if (gearset.BannerIndex >= uiSave.Portraits.Count)
            {
                throw new InvalidDataException(
                    $"套装 {gearset.Number:00} 引用的肖像索引 {gearset.BannerIndex} 超出 BANNER 记录范围。");
            }

            portraits.Add(new CharacterPortrait(
                root,
                gearset.Number,
                gearset.ClassJobId,
                gearset.Name,
                gearset.BannerIndex,
                uiSave.Portraits[gearset.BannerIndex]));
        }

        return portraits.OrderBy(item => item.GearsetNumber).ToArray();
    }

    public async Task<IReadOnlyList<PortraitBackupEntry>> ScanBackupsAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        var root = GetBackupRoot(libraryRoot);
        if (!Directory.Exists(root))
        {
            return [];
        }

        var entries = new List<PortraitBackupEntry>();
        foreach (var path in Directory.EnumerateFiles(root, $"*{ArchiveExtension}", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(await VerifyArchiveAsync(path, cancellationToken));
        }

        return entries
            .OrderByDescending(entry => entry.Manifest?.CreatedAtUtc ?? entry.ArchiveLastWriteTimeUtc)
            .ToArray();
    }

    public async Task<PortraitBackupEntry> CreateBackupAsync(
        CharacterPortrait source,
        string libraryRoot,
        string schemeName,
        string note,
        PortraitBackupReason reason = PortraitBackupReason.Manual,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var current = (await ScanCharacterAsync(source.CharacterDirectory, cancellationToken))
            .SingleOrDefault(item => item.GearsetNumber == source.GearsetNumber)
            ?? throw new InvalidOperationException("来源套装已不存在，请刷新后重试。");
        if (current.BannerIndex != source.BannerIndex ||
            !current.Data.SerializedRecord.AsSpan().SequenceEqual(source.Data.SerializedRecord))
        {
            throw new InvalidOperationException("来源套装的肖像数据或关联已经变化，请刷新后重试。");
        }

        return await CreateArchiveAsync(
            current,
            libraryRoot,
            schemeName,
            note,
            reason,
            cancellationToken);
    }

    public async Task<PortraitTransferResult> TransferAsync(
        PortraitTransferSource source,
        CharacterPortrait target,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var targetDirectory = Path.GetFullPath(target.CharacterDirectory);
        if (source.Character is not null && string.Equals(
                Path.GetFullPath(source.Character.CharacterDirectory),
                targetDirectory,
                GetPathComparison()))
        {
            throw new InvalidOperationException("同一角色内不能迁移肖像。");
        }

        var sourceData = await ResolveTransferSourceAsync(source, cancellationToken);
        await RecoverInterruptedTransfersAsync(targetDirectory, cancellationToken);
        var currentTarget = (await ScanCharacterAsync(targetDirectory, cancellationToken))
            .SingleOrDefault(item => item.GearsetNumber == target.GearsetNumber)
            ?? throw new InvalidOperationException("目标套装已不存在，请刷新后重试。");
        if (currentTarget.BannerIndex != target.BannerIndex)
        {
            throw new InvalidOperationException("目标套装的肖像关联已经变化，请刷新后重试。");
        }

        var targetPath = Path.Combine(targetDirectory, "UISAVE.DAT");
        var original = await ReadStableFileAsync(targetPath, cancellationToken);
        var parsed = ParseUiSave(original);
        var currentRecord = parsed.Portraits[currentTarget.BannerIndex];
        var currentGearset = ParseGearsets(await ReadStableFileAsync(
                Path.Combine(targetDirectory, "GEARSET.DAT"),
                cancellationToken))
            .SingleOrDefault(item => item.Number == currentTarget.GearsetNumber)
            ?? throw new InvalidOperationException("目标套装在提交前已不存在，请刷新后重试。");
        if (currentGearset.BannerIndex != currentTarget.BannerIndex ||
            !currentRecord.SerializedRecord.AsSpan().SequenceEqual(currentTarget.Data.SerializedRecord))
        {
            throw new InvalidOperationException("目标套装的肖像数据或关联已经变化，请刷新后重试。");
        }

        var recoveryPoint = await CreateArchiveAsync(
            currentTarget,
            libraryRoot,
            $"{currentTarget.GearsetNumber:00} {currentTarget.GearsetName} · 操作前恢复点",
            "肖像恢复或迁移前自动创建。",
            PortraitBackupReason.BeforeTransfer,
            cancellationToken);
        var mergedRecord = sourceData.ApplyVisualDataTo(
            parsed.Portraits[currentTarget.BannerIndex],
            _timeProvider.GetUtcNow());
        var updated = parsed.ReplacePortrait(currentTarget.BannerIndex, mergedRecord);
        await ReplaceTransactionallyAsync(
            targetDirectory,
            targetPath,
            original,
            updated,
            currentTarget.BannerIndex,
            cancellationToken);
        return new PortraitTransferResult(targetPath, recoveryPoint);
    }

    public Task DeleteBackupAsync(
        PortraitBackupEntry backup,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(backup);
        cancellationToken.ThrowIfCancellationRequested();
        var root = Path.GetFullPath(GetBackupRoot(libraryRoot));
        var path = Path.GetFullPath(backup.ArchivePath);
        var relativePath = Path.GetRelativePath(root, path);
        if (relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath) || !path.EndsWith(ArchiveExtension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能删除当前肖像备份区中的方案文件。");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("要删除的肖像备份方案已经不存在。", path);
        }

        File.Delete(path);
        DeleteEmptyBackupDirectories(Path.GetDirectoryName(path), root);
        return Task.CompletedTask;
    }

    private async Task<PortraitData> ResolveTransferSourceAsync(
        PortraitTransferSource source,
        CancellationToken cancellationToken)
    {
        if ((source.Character is null) == (source.Backup is null))
        {
            throw new ArgumentException("肖像操作必须且只能指定一个来源。", nameof(source));
        }

        if (source.Character is not null)
        {
            var selected = source.Character;
            var current = (await ScanCharacterAsync(selected.CharacterDirectory, cancellationToken))
                .SingleOrDefault(item => item.GearsetNumber == selected.GearsetNumber)
                ?? throw new InvalidOperationException("来源套装已不存在，请刷新后重试。");
            if (current.BannerIndex != selected.BannerIndex ||
                !current.Data.SerializedRecord.AsSpan().SequenceEqual(selected.Data.SerializedRecord))
            {
                throw new InvalidOperationException("来源套装的肖像数据或关联已经变化，请刷新后重试。");
            }

            return current.Data;
        }

        var selectedBackup = source.Backup!;
        var verified = await VerifyArchiveAsync(selectedBackup.ArchivePath, cancellationToken);
        if (verified.Integrity != PortraitBackupIntegrity.Valid || verified.Data is null ||
            verified.Manifest?.BackupId != selectedBackup.Manifest?.BackupId)
        {
            throw new InvalidDataException("来源肖像备份已变化或完整性校验失败，请刷新后重试。");
        }

        return verified.Data;
    }

    private async Task<PortraitBackupEntry> CreateArchiveAsync(
        CharacterPortrait source,
        string libraryRoot,
        string schemeName,
        string note,
        PortraitBackupReason reason,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var backupId = Guid.NewGuid();
        var data = source.Data.SerializedRecord;
        var manifest = new PortraitBackupManifest(
            PortraitBackupManifest.CurrentFormatVersion,
            backupId,
            now,
            reason,
            schemeName.Trim(),
            note.Trim(),
            new PortraitBackupSource(
                Path.GetFileName(source.CharacterDirectory),
                source.GearsetNumber,
                source.ClassJobId,
                source.GearsetName),
            data.Length,
            Convert.ToHexString(SHA256.HashData(data)),
            source.Data.LastUpdatedUtc);
        manifest.Validate();

        var directory = Path.Combine(
            GetBackupRoot(libraryRoot),
            now.ToString("yyyy"),
            now.ToString("MM"));
        Directory.CreateDirectory(directory);
        var archiveName = $"{now:yyyyMMddTHHmmssfffZ}_{backupId:N}{ArchiveExtension}";
        var finalPath = Path.Combine(directory, archiveName);
        var temporaryPath = Path.Combine(directory, $".{archiveName}.tmp");

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
                    PortraitBackupManifest.ManifestEntryName,
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
                    PortraitBackupManifest.DataEntryName,
                    CompressionLevel.Fastest);
                await using var dataStream = dataEntry.Open();
                await dataStream.WriteAsync(data, cancellationToken);
            }

            var verification = await VerifyArchiveAsync(temporaryPath, cancellationToken);
            if (verification.Integrity != PortraitBackupIntegrity.Valid)
            {
                throw new InvalidDataException(
                    $"新肖像备份校验失败：{string.Join("；", verification.Errors)}");
            }

            File.Move(temporaryPath, finalPath, overwrite: false);
            return verification with { ArchivePath = finalPath };
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task<PortraitBackupEntry> VerifyArchiveAsync(
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
            using var archive = new ZipArchive(input, ZipArchiveMode.Read);
            if (archive.Entries.Count != 2)
            {
                throw new InvalidDataException("肖像备份必须且只能包含 Manifest 和肖像数据。");
            }

            var manifestEntry = archive.GetEntry(PortraitBackupManifest.ManifestEntryName)
                ?? throw new InvalidDataException("肖像备份缺少 manifest.json。");
            if (manifestEntry.Length is <= 0 or > 64 * 1024)
            {
                throw new InvalidDataException("肖像备份 Manifest 大小无效。");
            }

            PortraitBackupManifest? manifest;
            await using (var stream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<PortraitBackupManifest>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            }

            manifest?.Validate();
            if (manifest is null)
            {
                throw new InvalidDataException("肖像备份 Manifest 为空。");
            }

            var dataEntry = archive.GetEntry(PortraitBackupManifest.DataEntryName)
                ?? throw new InvalidDataException("肖像备份缺少 portrait.dat。");
            if (dataEntry.Length != PortraitData.SerializedSize)
            {
                throw new InvalidDataException("肖像备份的数据大小无效。");
            }

            byte[] data;
            await using (var stream = dataEntry.Open())
            using (var memory = new MemoryStream(PortraitData.SerializedSize))
            {
                await stream.CopyToAsync(memory, cancellationToken);
                data = memory.ToArray();
            }

            if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(data)),
                    manifest.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("肖像备份的数据哈希不匹配。");
            }

            if (!PortraitData.TryParse(data, out var portrait, out var error))
            {
                throw new InvalidDataException($"肖像备份的数据无效：{error}");
            }

            return new PortraitBackupEntry(
                path,
                lastWriteTime,
                PortraitBackupIntegrity.Valid,
                manifest,
                portrait,
                []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return new PortraitBackupEntry(
                path,
                lastWriteTime,
                PortraitBackupIntegrity.Corrupted,
                null,
                null,
                [$"读取肖像备份失败：{exception.Message}"]);
        }
    }

    private async Task ReplaceTransactionallyAsync(
        string targetDirectory,
        string targetPath,
        byte[] original,
        byte[] updated,
        int bannerIndex,
        CancellationToken cancellationToken)
    {
        var operationId = Guid.NewGuid();
        var transactionRoot = Path.Combine(
            targetDirectory,
            ".ffxivconfigmanager",
            "portrait-transactions",
            operationId.ToString("N"));
        var stagedPath = Path.Combine(transactionRoot, "staged.dat");
        var rollbackPath = Path.Combine(transactionRoot, "rollback.dat");
        var journalPath = Path.Combine(transactionRoot, "journal.json");
        Directory.CreateDirectory(transactionRoot);
        var journal = new PortraitTransferJournal(
            operationId,
            targetPath,
            rollbackPath,
            Convert.ToHexString(SHA256.HashData(original)),
            Convert.ToHexString(SHA256.HashData(updated)),
            PortraitTransferState.Preparing);

        try
        {
            await WriteThroughAsync(stagedPath, updated, cancellationToken);
            await WriteThroughAsync(rollbackPath, original, cancellationToken);
            var currentTarget = await ReadStableFileAsync(targetPath, cancellationToken);
            if (!currentTarget.AsSpan().SequenceEqual(original))
            {
                throw new IOException("目标 UISAVE.DAT 在提交前已被其他程序修改，请刷新后重试。");
            }

            journal = journal with { State = PortraitTransferState.Committing };
            await SaveJournalAsync(journalPath, journal, cancellationToken);
            File.Move(stagedPath, targetPath, overwrite: true);
            _faultInjector.AfterTargetReplaced();

            var written = await ReadStableFileAsync(targetPath, cancellationToken);
            var writtenHash = Convert.ToHexString(SHA256.HashData(written));
            if (!string.Equals(writtenHash, journal.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("肖像写入后哈希校验失败。");
            }

            _ = ParseUiSave(written).Portraits[bannerIndex];
            TryDeleteDirectory(transactionRoot);
            TryDeleteEmptyTransactionParents(targetDirectory);
        }
        catch (Exception exception)
        {
            var rollbackError = journal.State == PortraitTransferState.Committing
                ? await TryRollbackAsync(journal, CancellationToken.None)
                : null;
            if (rollbackError is null)
            {
                TryDeleteDirectory(transactionRoot);
                TryDeleteEmptyTransactionParents(targetDirectory);
                if (exception is OperationCanceledException)
                {
                    throw;
                }

                throw new IOException($"肖像操作失败，目标文件已回滚：{exception.Message}", exception);
            }

            throw new IOException(
                $"肖像操作失败且回滚不完整：{exception.Message}；回滚错误：{rollbackError}",
                exception);
        }
    }

    private static IReadOnlyList<GearsetInfo> ParseGearsets(byte[] file)
    {
        if (file.Length < GearsetContentOffset + GearsetRecordCount * GearsetRecordSize)
        {
            throw new InvalidDataException("GEARSET.DAT 长度不足。");
        }

        var fileType = BinaryPrimitives.ReadUInt32LittleEndian(file);
        var maximumSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4));
        var contentSize = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8));
        if ((fileType & 0xFFFF) != 5 || maximumSize + 32 != file.Length ||
            contentSize < GearsetRecordCount * GearsetRecordSize + 1 ||
            contentSize > file.Length - 16)
        {
            throw new InvalidDataException("GEARSET.DAT 文件头无效。");
        }

        var content = file.AsSpan(GearsetContentOffset, checked((int)contentSize - 1)).ToArray();
        ApplyMask(content, GearsetMask);
        var result = new List<GearsetInfo>();
        for (var index = 0; index < GearsetRecordCount; index++)
        {
            var record = content.AsSpan(index * GearsetRecordSize, GearsetRecordSize);
            var flags = record[0x3B];
            if ((flags & 0x01) == 0)
            {
                continue;
            }

            var number = index + 1;
            var nameBytes = record.Slice(5, 48);
            var terminator = nameBytes.IndexOf((byte)0);
            if (terminator >= 0)
            {
                nameBytes = nameBytes[..terminator];
            }

            string name;
            try
            {
                name = StrictUtf8.GetString(nameBytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException($"套装 {number:00} 的名称不是有效 UTF-8。", exception);
            }

            var classJobId = record[0x35];
            var storedBannerIndex = record[0x3A];
            result.Add(new GearsetInfo(
                number,
                classJobId,
                string.IsNullOrEmpty(name) ? $"套装 {number:00}" : name,
                storedBannerIndex == 0 ? -1 : storedBannerIndex - 1));
        }

        return result;
    }

    private static ParsedUiSave ParseUiSave(byte[] file)
    {
        if (file.Length < 16)
        {
            throw new InvalidDataException("UISAVE.DAT 长度不足。");
        }

        var encryptedLength = BinaryPrimitives.ReadInt32LittleEndian(file.AsSpan(8));
        if (encryptedLength < 16 || encryptedLength > file.Length - 16)
        {
            throw new InvalidDataException("UISAVE.DAT 加密载荷长度无效。");
        }

        var decrypted = file.AsSpan(16, encryptedLength).ToArray();
        ApplyMask(decrypted, UiSaveMask);
        var position = 16;
        while (position < decrypted.Length)
        {
            if (decrypted.Length - position < 20)
            {
                throw new InvalidDataException("UISAVE.DAT 分段头被截断。");
            }

            var index = BinaryPrimitives.ReadInt16LittleEndian(decrypted.AsSpan(position));
            var length = BinaryPrimitives.ReadInt32LittleEndian(decrypted.AsSpan(position + 8));
            if (length < 0 || (long)position + 20 + length > decrypted.Length)
            {
                throw new InvalidDataException($"UISAVE.DAT 分段 {index} 长度无效。");
            }

            if (index == BannerSegmentIndex)
            {
                var dataOffset = position + 16;
                var data = decrypted.AsSpan(dataOffset, length);
                if (length < BannerHeaderSize ||
                    BinaryPrimitives.ReadUInt32LittleEndian(data) != 1 ||
                    BinaryPrimitives.ReadInt32LittleEndian(data[0x14..]) != length - BannerHeaderSize ||
                    (length - BannerHeaderSize) % PortraitData.SerializedSize != 0)
                {
                    throw new InvalidDataException("UISAVE.DAT 的 BANNER 分段结构无效。");
                }

                var portraits = new List<PortraitData>();
                for (var offset = BannerHeaderSize; offset < length; offset += PortraitData.SerializedSize)
                {
                    if (!PortraitData.TryParse(
                            data.Slice(offset, PortraitData.SerializedSize),
                            out var portrait,
                            out var error))
                    {
                        throw new InvalidDataException($"BANNER 肖像记录无效：{error}");
                    }

                    portraits.Add(portrait!);
                }

                return new ParsedUiSave(file, decrypted, dataOffset, portraits);
            }

            position += 20 + length;
        }

        throw new InvalidDataException("UISAVE.DAT 中没有 BANNER 分段。");
    }

    private static async Task<byte[]> ReadStableFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= StableReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = GetFileVersion(path);
            byte[] data;
            await using (var stream = new FileStream(
                             path,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.ReadWrite | FileShare.Delete,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                data = new byte[stream.Length];
                await stream.ReadExactlyAsync(data, cancellationToken);
            }

            var after = GetFileVersion(path);
            if (before == after)
            {
                return data;
            }

            if (attempt < StableReadAttempts)
            {
                await Task.Delay(300, cancellationToken);
            }
        }

        throw new IOException($"文件在读取期间持续变化：{Path.GetFileName(path)}");
    }

    private static FileVersion GetFileVersion(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("角色肖像所需的配置文件不存在。", path);
        }

        return new FileVersion(info.Length, info.LastWriteTimeUtc);
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
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(data, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task SaveJournalAsync(
        string path,
        PortraitTransferJournal journal,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await JsonSerializer.SerializeAsync(stream, journal, SerializerOptions, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task RecoverInterruptedTransfersAsync(
        string characterDirectory,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(
            characterDirectory,
            ".ffxivconfigmanager",
            "portrait-transactions");
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

            PortraitTransferJournal? journal;
            await using (var stream = File.OpenRead(journalPath))
            {
                journal = await JsonSerializer.DeserializeAsync<PortraitTransferJournal>(
                    stream,
                    SerializerOptions,
                    cancellationToken);
            }

            if (journal is null ||
                !string.Equals(
                    Path.GetDirectoryName(Path.GetFullPath(journal.TargetPath)),
                    Path.GetFullPath(characterDirectory),
                    GetPathComparison()))
            {
                throw new InvalidDataException("肖像事务日志的目标路径无效。");
            }

            if (journal.State == PortraitTransferState.Committing)
            {
                var error = await TryRollbackAsync(journal, cancellationToken);
                if (error is not null)
                {
                    throw new IOException($"无法恢复中断的肖像事务：{error}");
                }
            }

            TryDeleteDirectory(directory);
        }

        TryDeleteEmptyTransactionParents(characterDirectory);
    }

    private static async Task<string?> TryRollbackAsync(
        PortraitTransferJournal journal,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(journal.RollbackPath))
            {
                return "回滚副本不存在。";
            }

            var rollback = await File.ReadAllBytesAsync(journal.RollbackPath, cancellationToken);
            var rollbackHash = Convert.ToHexString(SHA256.HashData(rollback));
            if (!string.Equals(rollbackHash, journal.OriginalSha256, StringComparison.OrdinalIgnoreCase))
            {
                return "回滚副本哈希不匹配。";
            }

            if (File.Exists(journal.TargetPath))
            {
                var current = await File.ReadAllBytesAsync(journal.TargetPath, cancellationToken);
                var currentHash = Convert.ToHexString(SHA256.HashData(current));
                if (string.Equals(currentHash, journal.OriginalSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (!string.Equals(currentHash, journal.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return "目标文件包含事务之外的新修改，已拒绝覆盖该文件。";
                }
            }

            var temporary = $"{journal.TargetPath}.{journal.OperationId:N}.rollback";
            await File.WriteAllBytesAsync(temporary, rollback, cancellationToken);
            File.Move(temporary, journal.TargetPath, overwrite: true);
            var restored = await File.ReadAllBytesAsync(journal.TargetPath, cancellationToken);
            if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(restored)),
                    journal.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return "目标文件回滚后哈希不匹配。";
            }

            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return exception.Message;
        }
    }

    private static void ApplyMask(Span<byte> data, byte mask)
    {
        for (var index = 0; index < data.Length; index++)
        {
            data[index] ^= mask;
        }
    }

    private static string GetBackupRoot(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            throw new ArgumentException("备份库目录不能为空。", nameof(libraryRoot));
        }

        return Path.Combine(Path.GetFullPath(libraryRoot), "portrait-backups");
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void DeleteEmptyBackupDirectories(string? directory, string root)
    {
        while (directory is not null &&
               !string.Equals(directory, root, GetPathComparison()) &&
               Directory.Exists(directory) &&
               !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
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
        catch
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
        catch
        {
        }
    }

    private static void TryDeleteEmptyTransactionParents(string characterDirectory)
    {
        var managerRoot = Path.Combine(characterDirectory, ".ffxivconfigmanager");
        var transactionsRoot = Path.Combine(managerRoot, "portrait-transactions");
        try
        {
            if (Directory.Exists(transactionsRoot) && !Directory.EnumerateFileSystemEntries(transactionsRoot).Any())
            {
                Directory.Delete(transactionsRoot);
            }

            if (Directory.Exists(managerRoot) && !Directory.EnumerateFileSystemEntries(managerRoot).Any())
            {
                Directory.Delete(managerRoot);
            }
        }
        catch
        {
        }
    }

    private sealed record GearsetInfo(
        int Number,
        byte ClassJobId,
        string Name,
        int BannerIndex);

    private sealed record ParsedUiSave(
        byte[] OriginalFile,
        byte[] DecryptedPayload,
        int BannerDataOffset,
        IReadOnlyList<PortraitData> Portraits)
    {
        public byte[] ReplacePortrait(int bannerIndex, byte[] record)
        {
            if (record.Length != PortraitData.SerializedSize || bannerIndex < 0 || bannerIndex >= Portraits.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(bannerIndex));
            }

            var decrypted = DecryptedPayload.ToArray();
            record.CopyTo(
                decrypted,
                BannerDataOffset + BannerHeaderSize + bannerIndex * PortraitData.SerializedSize);
            ApplyMask(decrypted, UiSaveMask);
            var updated = OriginalFile.ToArray();
            decrypted.CopyTo(updated, 16);
            return updated;
        }
    }

    private sealed record FileVersion(long Length, DateTime LastWriteTimeUtc);

    private enum PortraitTransferState
    {
        Preparing,
        Committing,
    }

    private sealed record PortraitTransferJournal(
        Guid OperationId,
        string TargetPath,
        string RollbackPath,
        string OriginalSha256,
        string ExpectedSha256,
        PortraitTransferState State);
}
