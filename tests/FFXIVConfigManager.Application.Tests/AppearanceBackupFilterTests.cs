using FFXIVConfigManager.Application.Appearances;
using FFXIVConfigManager.Domain.Appearances;

namespace FFXIVConfigManager.Application.Tests;

public sealed class AppearanceBackupFilterTests
{
    private static readonly AppearanceMetadata Appearance = new(
        AppearanceRace.AuRa,
        11,
        AppearanceGender.Female,
        "晨曦白发测试",
        8,
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void Matches_CombinesRaceGenderAndCommentSearch()
    {
        Assert.True(AppearanceBackupFilter.Matches(
            Appearance,
            AppearanceRace.AuRa,
            AppearanceGender.Female,
            "白发"));
        Assert.False(AppearanceBackupFilter.Matches(
            Appearance,
            AppearanceRace.AuRa,
            AppearanceGender.Male,
            "白发"));
        Assert.False(AppearanceBackupFilter.Matches(
            Appearance,
            AppearanceRace.Hyur,
            AppearanceGender.Female,
            "白发"));
        Assert.False(AppearanceBackupFilter.Matches(
            Appearance,
            AppearanceRace.AuRa,
            AppearanceGender.Female,
            "黑发"));
    }

    [Fact]
    public void Matches_SearchIsCaseInsensitiveAndTrimmed()
    {
        var appearance = Appearance with { Comment = "White Hair" };

        Assert.True(AppearanceBackupFilter.Matches(appearance, null, null, "  white  "));
    }
}
