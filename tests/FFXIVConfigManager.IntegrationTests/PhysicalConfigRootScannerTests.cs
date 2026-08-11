using FFXIVConfigManager.Domain.Profiles;
using FFXIVConfigManager.Infrastructure.Discovery;

namespace FFXIVConfigManager.IntegrationTests;

public sealed class PhysicalConfigRootScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-{Guid.NewGuid():N}");

    [Fact]
    public async Task ScanAsync_FindsCharacterAndOnlyCataloguedFiles()
    {
        var characterDirectory = Path.Combine(_root, "FFXIV_CHR00400000020E9E17");
        Directory.CreateDirectory(Path.Combine(characterDirectory, "log"));
        await File.WriteAllTextAsync(Path.Combine(characterDirectory, "ADDON.DAT"), "hud");
        await File.WriteAllTextAsync(Path.Combine(characterDirectory, "HOTBAR.DAT"), "hotbar");
        await File.WriteAllTextAsync(Path.Combine(characterDirectory, "ADDON.DAT.old"), "old");
        await File.WriteAllTextAsync(Path.Combine(characterDirectory, "UNKNOWN.DAT"), "unknown");
        await File.WriteAllTextAsync(Path.Combine(characterDirectory, "log", "chat.log"), "private");
        Directory.CreateDirectory(Path.Combine(_root, "FFXIV_CHR_NOT_A_CHARACTER"));

        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, _root);
        var scanner = new PhysicalConfigRootScanner();

        var result = await scanner.ScanAsync(profile);

        Assert.True(result.RootExists);
        var character = Assert.Single(result.Characters);
        Assert.Equal(2, character.ExistingFileCount);
        Assert.Contains(character.Files, file => file.Definition.FileName == "ADDON.DAT");
        Assert.Contains(character.Files, file => file.Definition.FileName == "HOTBAR.DAT");
    }

    [Fact]
    public async Task ScanAsync_ReturnsIssueWhenRootDoesNotExist()
    {
        var profile = new GameProfile(Guid.NewGuid(), "测试", GameRegion.Custom, _root);
        var scanner = new PhysicalConfigRootScanner();

        var result = await scanner.ScanAsync(profile);

        Assert.False(result.RootExists);
        Assert.Empty(result.Characters);
        Assert.NotNull(result.Issue);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
