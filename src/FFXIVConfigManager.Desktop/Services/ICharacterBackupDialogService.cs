using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Desktop.Services;

public sealed record CharacterBackupDialogContext(
    string CharacterName,
    GameProfile? TargetProfile,
    CharacterConfiguration? TargetCharacter,
    IReadOnlyList<GameProfile> AvailableProfiles,
    string LibraryRoot,
    IReadOnlyList<SnapshotLibraryEntry> Backups);

public interface ICharacterBackupDialogService
{
    Task<bool> ShowAsync(
        CharacterBackupDialogContext context,
        CancellationToken cancellationToken = default);
}
