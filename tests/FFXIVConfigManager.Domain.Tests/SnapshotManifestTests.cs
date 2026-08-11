using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Domain.Tests;

public sealed class SnapshotManifestTests
{
    [Fact]
    public void Validate_AcceptsValidManifest()
    {
        var manifest = CreateManifest("files/ADDON.DAT");

        manifest.Validate();
    }

    [Theory]
    [InlineData("../ADDON.DAT")]
    [InlineData("files/../ADDON.DAT")]
    [InlineData("/files/ADDON.DAT")]
    [InlineData("C:/files/ADDON.DAT")]
    [InlineData("files\\ADDON.DAT")]
    public void Validate_RejectsUnsafeArchivePath(string path)
    {
        var manifest = CreateManifest(path);

        Assert.Throws<InvalidDataException>(manifest.Validate);
    }

    private static SnapshotManifest CreateManifest(string archivePath) =>
        new(
            SnapshotManifest.CurrentFormatVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            SnapshotReason.Manual,
            new SnapshotSource(Guid.NewGuid(), "测试", "FFXIV_CHR0000000000000001"),
            [
                new SnapshotFileEntry(
                    archivePath,
                    "ADDON.DAT",
                    1,
                    DateTimeOffset.UtcNow,
                    new string('A', 64)),
            ]);
}
