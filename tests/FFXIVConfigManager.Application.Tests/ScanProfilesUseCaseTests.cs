using FFXIVConfigManager.Application.Discovery;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Application.Tests;

public sealed class ScanProfilesUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_SortsCharactersByLatestModificationFirst()
    {
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, "/config");
        var older = CreateCharacter(profile, "FFXIV_CHR0000000000000001", DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var newer = CreateCharacter(profile, "FFXIV_CHR0000000000000002", DateTimeOffset.Parse("2026-02-01T00:00:00Z"));
        var useCase = new ScanProfilesUseCase(
            new StubProfileDiscovery(profile),
            new StubScanner([older, newer]));

        var results = await useCase.ExecuteAsync();

        Assert.Equal(newer.FolderName, results[0].Characters[0].FolderName);
        Assert.Equal(older.FolderName, results[0].Characters[1].FolderName);
    }

    private static CharacterConfiguration CreateCharacter(
        GameProfile profile,
        string folder,
        DateTimeOffset lastModified) =>
        new(profile.Id, CharacterFolderName.Create(folder), folder, lastModified, []);

    private sealed class StubProfileDiscovery(GameProfile profile) : IProfileDiscovery
    {
        public Task<IReadOnlyList<GameProfile>> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameProfile>>([profile]);
    }

    private sealed class StubScanner(IReadOnlyList<CharacterConfiguration> characters) : IConfigRootScanner
    {
        public Task<ConfigRootScanResult> ScanAsync(
            GameProfile profile,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ConfigRootScanResult(profile, true, characters));
    }
}
