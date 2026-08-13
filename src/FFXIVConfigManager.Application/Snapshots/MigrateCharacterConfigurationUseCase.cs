using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Files;
using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Snapshots;

public sealed record CompletedCharacterMigration(
    CreatedSnapshot TargetRecoveryPoint,
    CreatedSnapshot SourceSnapshot,
    SnapshotRestoreResult RestoreResult);

public sealed class MigrateCharacterConfigurationUseCase(
    CreateCharacterSnapshotUseCase createSnapshot,
    ITransactionalSnapshotRestorer restorer)
{
    public const ConfigScope DefaultScopes =
        ConfigScope.Hud |
        ConfigScope.Character |
        ConfigScope.Controls |
        ConfigScope.Hotbars |
        ConfigScope.Macros |
        ConfigScope.Gearsets |
        ConfigScope.UiState;

    public async Task<CompletedCharacterMigration> ExecuteAsync(
        GameProfile sourceProfile,
        CharacterConfiguration source,
        GameProfile targetProfile,
        CharacterConfiguration target,
        string libraryRoot,
        ConfigScope scopes = DefaultScopes,
        CancellationToken cancellationToken = default)
    {
        if (!source.BelongsTo(sourceProfile))
        {
            throw new ArgumentException("源角色不属于指定配置源。", nameof(source));
        }

        if (!target.BelongsTo(targetProfile))
        {
            throw new ArgumentException("目标角色不属于指定配置源。", nameof(target));
        }

        if (source.ProfileId == target.ProfileId && source.FolderName == target.FolderName)
        {
            throw new InvalidOperationException("源角色和目标角色不能相同。");
        }

        if (scopes == ConfigScope.None)
        {
            throw new ArgumentException("至少选择一个迁移范围。", nameof(scopes));
        }

        var recoveryPoint = await createSnapshot.ExecuteAllKnownAsync(
            targetProfile,
            target,
            libraryRoot,
            SnapshotReason.BeforeMigration,
            cancellationToken);
        var sourceSnapshot = await createSnapshot.ExecuteMigrationSourceAsync(
            sourceProfile,
            source,
            libraryRoot,
            scopes,
            cancellationToken);
        var restoreResult = await restorer.RestoreAsync(
            new SnapshotRestoreRequest(
                sourceSnapshot.ArchivePath,
                sourceSnapshot.Manifest,
                target.FullPath),
            cancellationToken);

        return new CompletedCharacterMigration(
            recoveryPoint,
            sourceSnapshot,
            restoreResult);
    }
}
