using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;

namespace FFXIVConfigManager.Application.Backups;

/// <summary>
/// 按保留数量清理角色配置的手动备份。手动备份是用户显式创建的，所以默认关闭，
/// 只有设置里开启后才生效；形象与肖像的手动备份不归这个用例管。
/// 功能关闭或备份库未设置时直接返回空结果，连扫描都不做。
/// </summary>
public sealed class PruneManualCharacterBackupsUseCase(
    ISnapshotLibraryReader snapshotLibraryReader,
    ISnapshotArchiveService snapshotArchiveService)
{
    public async Task<BackupPruneResult> ExecuteAsync(
        BackupRetentionSetting setting,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(setting);

        if (!setting.Enabled || string.IsNullOrWhiteSpace(libraryRoot))
        {
            return BackupPruneResult.Empty;
        }

        var manualBackups = (await snapshotLibraryReader.ScanAsync(libraryRoot, cancellationToken))
            .Where(entry => entry.Rule == BackupRule.Manual)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .ThenByDescending(entry => entry.ArchivePath, StringComparer.Ordinal)
            .ToArray();

        var deleted = new List<PrunedBackup>();
        var failures = new List<string>();

        foreach (var entry in manualBackups.Skip(setting.EffectiveRetentionCount))
        {
            try
            {
                await snapshotArchiveService.DeleteAsync(entry.ArchivePath, cancellationToken);
                deleted.Add(new PrunedBackup(entry.Rule, entry.ArchivePath));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException)
            {
                failures.Add($"{entry.ArchivePath}：{exception.Message}");
            }
        }

        return BackupPruneResult.Create(deleted, failures);
    }
}
