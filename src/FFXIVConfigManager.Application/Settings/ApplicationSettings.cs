using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Settings;

public sealed record CharacterAliasSetting(
    Guid ProfileId,
    string CharacterFolder,
    string Alias);

public sealed record ApplicationSettings(
    int SchemaVersion,
    IReadOnlyList<GameProfile> CustomProfiles,
    IReadOnlyList<CharacterAliasSetting> CharacterAliases)
{
    public const int CurrentSchemaVersion = 1;

    public string? SnapshotLibraryPath { get; init; }

    public bool ShowOnlyTaggedCharacters { get; init; }

    public bool IsUpdateProxyEnabled { get; init; }

    public string? UpdateProxyAddress { get; init; }

    public static ApplicationSettings Empty { get; } =
        new(CurrentSchemaVersion, [], []);
}
