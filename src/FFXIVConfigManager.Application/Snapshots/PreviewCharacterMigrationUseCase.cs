using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Files;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed record CharacterMigrationPreview(
    CharacterConfiguration Source,
    CharacterConfiguration Target,
    ConfigScope Scopes,
    IReadOnlyList<SnapshotFilePreview> Files);

public sealed class PreviewCharacterMigrationUseCase(IStableFileHashService fileHashService)
{
    public async Task<CharacterMigrationPreview> ExecuteAsync(
        CharacterConfiguration source,
        CharacterConfiguration target,
        ConfigScope scopes,
        CancellationToken cancellationToken = default)
    {
        if (source.ProfileId == target.ProfileId && source.FolderName == target.FolderName)
        {
            throw new InvalidOperationException("源角色和目标角色不能相同。");
        }

        var selectedFiles = source.Files
            .Where(file => scopes.HasFlag(ConfigScope.AllKnownFiles) ||
                           file.Definition.IncludedInSafeMigration &&
                           scopes.HasFlag(file.Definition.Scope))
            .ToArray();
        if (selectedFiles.Length == 0)
        {
            throw new InvalidOperationException("源角色没有符合所选范围的配置文件。");
        }

        var files = new List<SnapshotFilePreview>(selectedFiles.Length);
        foreach (var sourceFile in selectedFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = sourceFile.Definition.FileName;
            var sourceDigest = await fileHashService.TryComputeAsync(
                Path.Combine(source.FullPath, fileName),
                cancellationToken)
                ?? throw new FileNotFoundException($"源配置文件不存在：{fileName}");
            var targetDigest = await fileHashService.TryComputeAsync(
                Path.Combine(target.FullPath, fileName),
                cancellationToken);
            var difference = targetDigest is null
                ? SnapshotFileDifference.MissingFromTarget
                : targetDigest.Size == sourceDigest.Size &&
                  string.Equals(
                      targetDigest.Sha256,
                      sourceDigest.Sha256,
                      StringComparison.OrdinalIgnoreCase)
                    ? SnapshotFileDifference.Identical
                    : SnapshotFileDifference.Different;

            files.Add(new SnapshotFilePreview(fileName, sourceDigest.Size, difference));
        }

        return new CharacterMigrationPreview(source, target, scopes, files);
    }
}
