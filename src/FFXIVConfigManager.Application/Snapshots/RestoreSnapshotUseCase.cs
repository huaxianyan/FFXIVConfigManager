using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed record CompletedSnapshotRestore(
    CreatedSnapshot? RecoveryPoint,
    SnapshotRestoreResult RestoreResult,
    bool CreatedTargetDirectory);

public sealed class RestoreSnapshotUseCase(
    ISnapshotArchiveService archiveService,
    CreateCharacterSnapshotUseCase createSnapshot,
    ITransactionalSnapshotRestorer restorer)
{
    public async Task<CompletedSnapshotRestore> ExecuteAsync(
        SnapshotLibraryEntry snapshot,
        GameProfile targetProfile,
        CharacterConfiguration target,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        if (!target.BelongsTo(targetProfile))
        {
            throw new ArgumentException("目标角色不属于指定配置源。", nameof(target));
        }

        var verification = await archiveService.VerifyAsync(
            snapshot.ArchivePath,
            cancellationToken);
        if (!verification.IsValid || verification.Manifest is null)
        {
            throw new InvalidDataException(
                $"备份完整性校验失败：{string.Join("；", verification.Errors)}");
        }

        var expectedTargetPath = Path.GetFullPath(Path.Combine(
            targetProfile.ConfigRoot,
            target.FolderName.Value));
        var targetPath = Path.GetFullPath(target.FullPath);
        var pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (!pathComparer.Equals(expectedTargetPath, targetPath))
        {
            throw new InvalidOperationException("目标角色目录不属于指定的配置源。");
        }

        if (!Directory.Exists(targetProfile.ConfigRoot))
        {
            throw new DirectoryNotFoundException($"目标配置源目录不存在：{targetProfile.ConfigRoot}");
        }

        var targetDirectoryExisted = Directory.Exists(targetPath);
        if (targetDirectoryExisted &&
            target.Files.Count == 0 &&
            verification.Manifest.Files.Any(file =>
                File.Exists(Path.Combine(targetPath, file.OriginalFileName))))
        {
            throw new InvalidOperationException(
                "目标角色目录包含未扫描到的配置文件，请刷新角色列表后重试。");
        }

        CreatedSnapshot? recoveryPoint = null;
        if (targetDirectoryExisted && target.Files.Count > 0)
        {
            recoveryPoint = await createSnapshot.ExecuteAllKnownAsync(
                targetProfile,
                target,
                libraryRoot,
                SnapshotReason.BeforeRestore,
                cancellationToken);
        }
        else if (!targetDirectoryExisted)
        {
            Directory.CreateDirectory(targetPath);
        }

        try
        {
            var restoreResult = await restorer.RestoreAsync(
                new SnapshotRestoreRequest(
                    snapshot.ArchivePath,
                    verification.Manifest,
                    targetPath),
                cancellationToken);

            return new CompletedSnapshotRestore(
                recoveryPoint,
                restoreResult,
                CreatedTargetDirectory: !targetDirectoryExisted);
        }
        catch
        {
            if (!targetDirectoryExisted &&
                Directory.Exists(targetPath) &&
                !Directory.EnumerateFileSystemEntries(targetPath).Any())
            {
                Directory.Delete(targetPath);
            }

            throw;
        }
    }
}
