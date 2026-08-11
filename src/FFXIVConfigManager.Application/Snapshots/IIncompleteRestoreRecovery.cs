namespace FFXIVConfigManager.Application.Snapshots;

public sealed record InterruptedRestoreRecoveryResult(
    Guid OperationId,
    string TargetDirectory,
    bool Recovered,
    IReadOnlyList<string> Errors);

public interface IIncompleteRestoreRecovery
{
    Task<IReadOnlyList<InterruptedRestoreRecoveryResult>> RecoverAsync(
        IEnumerable<string> targetDirectories,
        CancellationToken cancellationToken = default);
}
