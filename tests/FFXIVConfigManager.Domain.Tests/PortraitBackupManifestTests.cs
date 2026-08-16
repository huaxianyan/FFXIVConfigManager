using FFXIVConfigManager.Domain.Portraits;

namespace FFXIVConfigManager.Domain.Tests;

public sealed class PortraitBackupManifestTests
{
    [Theory]
    [InlineData("", "备注")]
    [InlineData("方案", "")]
    [InlineData(" 方案", "备注")]
    [InlineData("方案", "备注 ")]
    public void Validate_RejectsMissingOrUntrimmedSchemeMetadata(string schemeName, string note)
    {
        var manifest = CreateManifest(schemeName, note);

        Assert.Throws<InvalidDataException>(manifest.Validate);
    }

    [Fact]
    public void Validate_AcceptsSinglePortraitBackupMetadata()
    {
        var manifest = CreateManifest("高难方案", "保留当前构图。");

        manifest.Validate();
    }

    private static PortraitBackupManifest CreateManifest(string schemeName, string note) =>
        new(
            PortraitBackupManifest.CurrentFormatVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            PortraitBackupReason.Manual,
            schemeName,
            note,
            new PortraitBackupSource("FFXIV_CHR1111111111111111", 1, 19, "骑士"),
            PortraitData.SerializedSize,
            new string('A', 64),
            DateTimeOffset.UtcNow);
}
