using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Files;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Infrastructure.Discovery;

public sealed class PhysicalConfigRootScanner : IConfigRootScanner
{
    public Task<ConfigRootScanResult> ScanAsync(
        GameProfile profile,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(profile, cancellationToken), cancellationToken);

    private static ConfigRootScanResult Scan(
        GameProfile profile,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(profile.ConfigRoot))
        {
            return new ConfigRootScanResult(
                profile,
                RootExists: false,
                Characters: [],
                Issue: "配置目录不存在。请在设置中确认路径。");
        }

        try
        {
            var characters = new List<CharacterConfiguration>();

            foreach (var directory in Directory.EnumerateDirectories(
                         profile.ConfigRoot,
                         $"{CharacterFolderName.Prefix}*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var directoryName = Path.GetFileName(directory);
                if (!CharacterFolderName.TryCreate(directoryName, out var folderName))
                {
                    continue;
                }

                characters.Add(ScanCharacter(profile, folderName, directory, cancellationToken));
            }

            return new ConfigRootScanResult(profile, RootExists: true, characters);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new ConfigRootScanResult(
                profile,
                RootExists: true,
                Characters: [],
                Issue: $"扫描配置目录失败：{exception.Message}");
        }
    }

    private static CharacterConfiguration ScanCharacter(
        GameProfile profile,
        CharacterFolderName folderName,
        string directory,
        CancellationToken cancellationToken)
    {
        var files = new List<CharacterConfigFile>();

        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!ConfigFileCatalog.TryGet(Path.GetFileName(path), out var definition))
            {
                continue;
            }

            var file = new FileInfo(path);
            files.Add(new CharacterConfigFile(
                definition,
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
        }

        var latestFileWrite = files.Count == 0
            ? new DateTimeOffset(Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero)
            : files.Max(file => file.LastWriteTimeUtc);

        return new CharacterConfiguration(
            profile.Id,
            folderName,
            Path.GetFullPath(directory),
            latestFileWrite,
            files.OrderBy(file => file.Definition.FileName, StringComparer.Ordinal).ToArray());
    }
}
