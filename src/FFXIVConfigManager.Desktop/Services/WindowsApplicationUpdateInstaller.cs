using Avalonia.Controls.ApplicationLifetimes;
using FFXIVConfigManager.Application.Updates;
using FFXIVConfigManager.Platform.Windows.Updates;

namespace FFXIVConfigManager.Desktop.Services;

public sealed class WindowsApplicationUpdateInstaller(
    IClassicDesktopStyleApplicationLifetime desktop) : IApplicationUpdateInstaller
{
    public bool IsSupported => WindowsSelfUpdate.IsSupportedInstallation;

    public void Launch(PreparedApplicationUpdate preparedUpdate)
    {
        WindowsSelfUpdate.Launch(preparedUpdate);
        desktop.Shutdown();
    }
}
