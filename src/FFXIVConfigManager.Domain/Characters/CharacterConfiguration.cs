using FFXIVConfigManager.Domain.Files;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Domain.Characters;

public sealed record CharacterConfigFile(
    ConfigFileDefinition Definition,
    long Length,
    DateTimeOffset LastWriteTimeUtc);

public sealed record CharacterConfiguration(
    Guid ProfileId,
    CharacterFolderName FolderName,
    string FullPath,
    DateTimeOffset LastModifiedUtc,
    IReadOnlyList<CharacterConfigFile> Files)
{
    public int ExpectedFileCount => ConfigFileCatalog.All.Count;

    public int ExistingFileCount => Files.Count;

    public double Completeness => ExpectedFileCount == 0
        ? 1
        : (double)ExistingFileCount / ExpectedFileCount;

    public bool BelongsTo(GameProfile profile) => ProfileId == profile.Id;
}
