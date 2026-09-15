namespace FFXIVConfigManager.Application.Backups;

public sealed record PrunedBackup(BackupRule Rule, string ArchivePath);

/// <summary>一次清理的结果。自动恢复点与手动备份共用同一份结构，方便合并成一条状态提示。</summary>
public sealed record BackupPruneResult(
    IReadOnlyList<PrunedBackup> Deleted,
    IReadOnlyList<string> Failures)
{
    public static BackupPruneResult Empty { get; } = new([], []);

    public int DeletedCount => Deleted.Count;

    public bool HasChanges => Deleted.Count > 0;

    public bool HasFailures => Failures.Count > 0;

    public static BackupPruneResult Create(
        IReadOnlyList<PrunedBackup> deleted,
        IReadOnlyList<string> failures) =>
        deleted.Count == 0 && failures.Count == 0
            ? Empty
            : new BackupPruneResult(deleted, failures);

    public static BackupPruneResult Merge(params BackupPruneResult[] results) =>
        Create(
            results.SelectMany(result => result.Deleted).ToArray(),
            results.SelectMany(result => result.Failures).ToArray());
}
