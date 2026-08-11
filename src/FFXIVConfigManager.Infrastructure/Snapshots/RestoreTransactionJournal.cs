namespace FFXIVConfigManager.Infrastructure.Snapshots;

internal sealed class RestoreJournal
{
    public required Guid OperationId { get; init; }
    public required string ArchivePath { get; init; }
    public required string TargetDirectory { get; init; }
    public required RestoreTransactionState State { get; set; }
    public required List<RestoreJournalItem> Files { get; init; }
}

internal sealed class RestoreJournalItem
{
    public required string FileName { get; init; }
    public required string ExpectedSha256 { get; init; }
    public required RestoreFileState State { get; set; }
    public required bool HadOriginal { get; set; }
    public string? OriginalSha256 { get; set; }
}

internal enum RestoreTransactionState
{
    Preparing,
    Committing,
    RollingBack,
    RolledBack,
    RollbackFailed,
    Completed,
}

internal enum RestoreFileState
{
    Pending,
    Prepared,
    Committed,
    RolledBack,
}
