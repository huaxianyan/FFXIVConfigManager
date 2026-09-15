using FFXIVConfigManager.Application.Appearances;
using FFXIVConfigManager.Application.Portraits;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;

namespace FFXIVConfigManager.Application.Backups;

/// <summary>
/// 按每个功能各自的保留数量清理自动备份。手动备份由
/// <see cref="PruneManualCharacterBackupsUseCase"/> 单独处理；
/// 已关闭的功能不触发清理，其已有自动备份原样保留到下次开启。
/// 备份库未设置时直接返回空结果。
/// </summary>
public sealed class PruneAutomaticBackupsUseCase(
    ISnapshotLibraryReader snapshotLibraryReader,
    ISnapshotArchiveService snapshotArchiveService,
    IAppearanceBackupService appearanceBackupService,
    IPortraitManagementService portraitManagementService)
{
    public async Task<BackupPruneResult> ExecuteAsync(
        AutomaticBackupPolicy policy,
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            return BackupPruneResult.Empty;
        }

        var candidates = new List<PruneCandidate>();

        foreach (var entry in await snapshotLibraryReader.ScanAsync(libraryRoot, cancellationToken))
        {
            candidates.Add(new PruneCandidate(
                entry.Rule,
                entry.ArchivePath,
                entry.CreatedAtUtc,
                () => snapshotArchiveService.DeleteAsync(entry.ArchivePath, cancellationToken)));
        }

        foreach (var entry in await appearanceBackupService.ScanBackupsAsync(libraryRoot, cancellationToken))
        {
            candidates.Add(new PruneCandidate(
                entry.Rule,
                entry.ArchivePath,
                entry.CreatedAtUtc,
                () => appearanceBackupService.DeleteAsync(entry.ArchivePath, cancellationToken)));
        }

        foreach (var entry in await portraitManagementService.ScanBackupsAsync(libraryRoot, cancellationToken))
        {
            candidates.Add(new PruneCandidate(
                entry.Rule,
                entry.ArchivePath,
                entry.CreatedAtUtc,
                () => portraitManagementService.DeleteBackupAsync(
                    entry,
                    libraryRoot,
                    cancellationToken)));
        }

        var deleted = new List<PrunedBackup>();
        var failures = new List<string>();

        foreach (var group in candidates
                     .Where(candidate => candidate.Rule.IsAutomatic())
                     .GroupBy(candidate => candidate.Rule))
        {
            var setting = policy.For(group.Key);
            if (!setting.Enabled)
            {
                continue;
            }

            var ordered = group
                .OrderByDescending(candidate => candidate.CreatedAtUtc)
                .ThenByDescending(candidate => candidate.ArchivePath, StringComparer.Ordinal)
                .ToArray();

            foreach (var candidate in ordered.Skip(setting.EffectiveRetentionCount))
            {
                await DeleteAsync(candidate, deleted, failures);
            }
        }

        return BackupPruneResult.Create(deleted, failures);
    }

    private static async Task DeleteAsync(
        PruneCandidate candidate,
        List<PrunedBackup> deleted,
        List<string> failures)
    {
        try
        {
            await candidate.DeleteAsync();
            deleted.Add(new PrunedBackup(candidate.Rule, candidate.ArchivePath));
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
            failures.Add($"{candidate.ArchivePath}：{exception.Message}");
        }
    }

    private sealed record PruneCandidate(
        BackupRule Rule,
        string ArchivePath,
        DateTimeOffset CreatedAtUtc,
        Func<Task> DeleteAsync);
}
