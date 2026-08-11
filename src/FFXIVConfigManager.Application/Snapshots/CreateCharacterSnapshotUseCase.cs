using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed class CreateCharacterSnapshotUseCase(
    ISnapshotArchiveService archiveService,
    TimeProvider timeProvider)
{
    public Task<CreatedSnapshot> ExecuteAsync(
        GameProfile profile,
        CharacterConfiguration character,
        string libraryRoot,
        SnapshotReason reason = SnapshotReason.Manual,
        CancellationToken cancellationToken = default)
    {
        if (!character.BelongsTo(profile))
        {
            throw new ArgumentException("角色不属于指定的配置源。", nameof(character));
        }

        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            throw new ArgumentException("快照库目录不能为空。", nameof(libraryRoot));
        }

        var files = character.Files
            .Where(file => file.Definition.IncludedInDefaultBackup)
            .Select(file => new SnapshotFileSource(
                Path.Combine(character.FullPath, file.Definition.FileName),
                $"files/{file.Definition.FileName}",
                file.Definition.FileName))
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException("该角色没有可备份的已知配置文件。");
        }

        var request = new SnapshotArchiveRequest(
            Path.GetFullPath(libraryRoot),
            Guid.NewGuid(),
            timeProvider.GetUtcNow(),
            reason,
            new SnapshotSource(
                profile.Id,
                profile.Name,
                character.FolderName.Value),
            files);

        return archiveService.CreateAsync(request, cancellationToken);
    }
}
