using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FFXIVConfigManager.Application.Updates;
using FFXIVConfigManager.Desktop.Localization;
using FFXIVConfigManager.Desktop.Services;

namespace FFXIVConfigManager.Desktop.ViewModels;

public sealed record UpdateProxyProtocolOption(string Scheme, string DisplayName);

public sealed partial class UpdateProxySettingsViewModel : ViewModelBase
{
    private readonly IApplicationUpdateProxyTester _proxyTester;
    private readonly ITextLocalizer _text;

    public UpdateProxySettingsViewModel(
        string? currentAddress,
        IApplicationUpdateProxyTester proxyTester,
        ITextLocalizer text)
    {
        _proxyTester = proxyTester;
        _text = text;
        Protocols = UpdateProxyEndpoint.SupportedSchemes
            .Select(scheme => new UpdateProxyProtocolOption(scheme, scheme.ToUpperInvariant()))
            .ToArray();

        UpdateProxyEndpoint? endpoint = null;
        if (!string.IsNullOrWhiteSpace(currentAddress))
        {
            endpoint = UpdateProxyEndpoint.Parse(currentAddress);
        }

        SelectedProtocol = Protocols.First(option =>
            string.Equals(
                option.Scheme,
                endpoint?.Scheme ?? "socks5",
                StringComparison.OrdinalIgnoreCase));
        Host = endpoint?.Host ?? "127.0.0.1";
        Port = (endpoint?.Port ?? 7890).ToString();
        StatusText = text["UpdateProxyTestNotRun"];
    }

    public event Action<UpdateProxyDialogResult?>? CloseRequested;

    public IReadOnlyList<UpdateProxyProtocolOption> Protocols { get; }

    [ObservableProperty]
    public partial UpdateProxyProtocolOption SelectedProtocol { get; set; }

    [ObservableProperty]
    public partial string Host { get; set; }

    [ObservableProperty]
    public partial string Port { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestProxyCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsBusy { get; private set; }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task TestProxyAsync(CancellationToken cancellationToken)
    {
        if (!TryCreateEndpoint(out var endpoint))
        {
            return;
        }

        IsBusy = true;
        StatusText = _text["TestingUpdateProxy"];
        try
        {
            var elapsed = await _proxyTester.TestAsync(endpoint.Address, cancellationToken);
            StatusText = _text.Format(
                "UpdateProxyTestSucceededFormat",
                Math.Round(elapsed.TotalMilliseconds));
        }
        catch (OperationCanceledException)
        {
            StatusText = _text["UpdateProxyTestCanceled"];
        }
        catch (Exception exception)
        {
            StatusText = _text.Format("UpdateProxyTestFailedFormat", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void Save()
    {
        if (TryCreateEndpoint(out var endpoint))
        {
            CloseRequested?.Invoke(new UpdateProxyDialogResult(endpoint.Address));
        }
    }

    private bool TryCreateEndpoint(out UpdateProxyEndpoint endpoint)
    {
        try
        {
            if (!int.TryParse(Port.Trim(), out var port))
            {
                throw new ArgumentException("请输入 1～65535 之间的端口。");
            }

            endpoint = UpdateProxyEndpoint.Create(SelectedProtocol.Scheme, Host, port);
            return true;
        }
        catch (ArgumentException exception)
        {
            endpoint = null!;
            StatusText = _text.Format("UpdateProxyInputInvalidFormat", exception.Message);
            return false;
        }
    }

    private bool CanInteract() => !IsBusy;
}
