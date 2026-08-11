using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Desktop.Services;
using FFXIVConfigManager.Desktop.ViewModels;
using FFXIVConfigManager.Desktop.Views;
using FFXIVConfigManager.Infrastructure.Discovery;
using FFXIVConfigManager.Infrastructure.Settings;
using FFXIVConfigManager.Infrastructure.Snapshots;
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
            var settingsStore = new JsonSettingsStore(ApplicationDataPaths.GetDefaultSettingsPath());
            var automaticDiscovery = OperatingSystem.IsWindows()
                ? (IProfileDiscovery)new WindowsDefaultProfileDiscovery()
                : new NoDefaultProfileDiscovery();
            var configuredDiscovery = new ConfiguredProfileDiscovery(
                automaticDiscovery,
                settingsStore);
            var scanner = new PhysicalConfigRootScanner();
            var settingsService = new SettingsService(settingsStore);
            var snapshotService = new ZipSnapshotArchiveService();
            var createSnapshot = new CreateCharacterSnapshotUseCase(
                snapshotService,
                TimeProvider.System);
            var snapshotLibraryReader = new PhysicalSnapshotLibraryReader(snapshotService);
            var scanSnapshotLibrary = new ScanSnapshotLibraryUseCase(snapshotLibraryReader);
            var previewSnapshot = new PreviewSnapshotUseCase(
                snapshotService,
                new StableFileHashService());

            MainWindow? window = null;
            var folderPicker = new AvaloniaFolderPickerService(() => window);
            var viewModel = new MainViewModel(
                new ScanProfilesUseCase(configuredDiscovery, scanner),
                settingsService,
                createSnapshot,
                scanSnapshotLibrary,
                previewSnapshot,
                folderPicker);
            window = new MainWindow
            {
                DataContext = viewModel,
            };
            desktop.MainWindow = window;

            _ = viewModel.RefreshAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
