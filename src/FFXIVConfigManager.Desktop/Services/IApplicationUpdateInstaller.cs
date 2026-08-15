using FFXIVConfigManager.Application.Updates;

namespace FFXIVConfigManager.Desktop.Services;

public interface IApplicationUpdateInstaller
{
    bool IsSupported { get; }

    void Launch(PreparedApplicationUpdate preparedUpdate);
}
