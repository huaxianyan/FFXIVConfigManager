namespace FFXIVConfigManager.Desktop.Services;

public sealed record UpdateProxyDialogResult(string Address);

public interface IUpdateProxyDialogService
{
    Task<UpdateProxyDialogResult?> ShowAsync(
        string? currentAddress,
        CancellationToken cancellationToken = default);
}
