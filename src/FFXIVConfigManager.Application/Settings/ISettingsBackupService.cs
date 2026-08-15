using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Settings;

[Flags]
public enum SettingsBackupScope
{
    None = 0,
    CharacterAliases = 1 << 0,
    CustomProfiles = 1 << 1,
    All = CharacterAliases | CustomProfiles,
}

public sealed record SettingsBackupDocument(
    int FormatVersion,
    DateTimeOffset CreatedAtUtc,
    SettingsBackupScope IncludedScopes,
    IReadOnlyList<GameProfile> CustomProfiles,
    IReadOnlyList<CharacterAliasSetting> CharacterAliases)
{
    public const int CurrentFormatVersion = 1;
}

public sealed record SettingsBackupStatus(
    string BackupPath,
    bool Exists,
    bool IsValid,
    DateTimeOffset? CreatedAtUtc,
    SettingsBackupScope IncludedScopes,
    IReadOnlyList<string> Errors);

public interface ISettingsBackupService
{
    Task<SettingsBackupStatus> GetStatusAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default);

    Task<SettingsBackupStatus> BackupAsync(
        string libraryRoot,
        SettingsBackupScope scopes,
        CancellationToken cancellationToken = default);

    Task RestoreAsync(
        string libraryRoot,
        SettingsBackupScope scopes,
        CancellationToken cancellationToken = default);
}
