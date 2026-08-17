namespace FFXIVConfigManager.Desktop.Services;

public sealed record PortraitBackupEditResult(string SchemeName, string Note);

public interface IPortraitBackupEditDialogService
{
    Task<PortraitBackupEditResult?> ShowAsync(
        string schemeName,
        string note,
        CancellationToken cancellationToken = default);
}
