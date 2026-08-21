using Avalonia.Controls;
using FFXIVConfigManager.Application.Updates;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.ViewModels;
using FFXIVConfigManager.Desktop.Views;

namespace FFXIVConfigManager.Desktop.Services;

public sealed class AvaloniaUpdateProxyDialogService(
    Func<Window?> ownerAccessor,
    IApplicationUpdateProxyTester proxyTester,
    ITextLocalizer text) : IUpdateProxyDialogService
{
    public async Task<UpdateProxyDialogResult?> ShowAsync(
        string? currentAddress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var owner = ownerAccessor();
        if (owner is null)
        {
            return null;
        }

        var viewModel = new UpdateProxySettingsViewModel(currentAddress, proxyTester, text);
        var window = new UpdateProxySettingsWindow
        {
            DataContext = viewModel,
        };
        viewModel.CloseRequested += result => window.Close(result);
        return await window.ShowDialog<UpdateProxyDialogResult?>(owner);
    }
}
