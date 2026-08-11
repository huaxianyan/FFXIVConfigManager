using FFXIVConfigManager.Domain.Characters;

namespace FFXIVConfigManager.Domain.Tests;

public sealed class CharacterFolderNameTests
{
    [Theory]
    [InlineData("FFXIV_CHR00400000020E9E17")]
    [InlineData("ffxiv_chr00400000020e9e17")]
    public void TryCreate_AcceptsValidNameAndNormalizesCase(string value)
    {
        var result = CharacterFolderName.TryCreate(value, out var folderName);

        Assert.True(result);
        Assert.Equal("FFXIV_CHR00400000020E9E17", folderName.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("FFXIV_CHR00400000020E9E1")]
    [InlineData("FFXIV_CHR00400000020E9E170")]
    [InlineData("FFXIV_CHR00400000020E9E1Z")]
    [InlineData("OTHER_CHR00400000020E9E17")]
    public void TryCreate_RejectsInvalidName(string? value)
    {
        Assert.False(CharacterFolderName.TryCreate(value, out _));
    }
}
