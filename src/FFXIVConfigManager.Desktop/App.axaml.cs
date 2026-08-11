using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Desktop.ViewModels;
using FFXIVConfigManager.Desktop.Views;
using FFXIVConfigManager.Infrastructure.Discovery;
using FFXIVConfigManager.Platform.Windows.Discovery;

namespace FFXIVConfigManager.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var profileDiscovery = OperatingSystem.IsWindows()
                ? (IProfileDiscovery)new WindowsDefaultProfileDiscovery()
                : new NoDefaultProfileDiscovery();
            var scanner = new PhysicalConfigRootScanner();
            var viewModel = new MainViewModel(new ScanProfilesUseCase(profileDiscovery, scanner));

            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            _ = viewModel.RefreshAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
