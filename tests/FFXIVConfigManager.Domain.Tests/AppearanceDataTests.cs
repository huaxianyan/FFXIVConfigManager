using System.Buffers.Binary;
using System.Text;
using FFXIVConfigManager.Domain.Appearances;

namespace FFXIVConfigManager.Domain.Tests;

public sealed class AppearanceDataTests
{
    [Fact]
    public void TryParse_ReadsIdentificationMetadata()
    {
        var data = CreateData(AppearanceRace.AuRa, 11, AppearanceGender.Female, "晨曦测试");

        var valid = AppearanceData.TryParse(data, out var metadata, out var error);

        Assert.True(valid, error);
        Assert.NotNull(metadata);
        Assert.Equal(AppearanceRace.AuRa, metadata.Race);
        Assert.Equal((byte)11, metadata.Tribe);
        Assert.Equal(AppearanceGender.Female, metadata.Gender);
        Assert.Equal("晨曦测试", metadata.Comment);
    }

    [Fact]
    public void TryParse_RejectsChecksumMismatch()
    {
        var data = CreateData(AppearanceRace.Hyur, 1, AppearanceGender.Male, "测试");
        data[0x30] ^= 0x01;

        Assert.False(AppearanceData.TryParse(data, out _, out var error));
        Assert.Contains("校验和", error);
    }

    [Theory]
    [InlineData(1, "FFXIV_CHARA_01.dat")]
    [InlineData(40, "FFXIV_CHARA_40.dat")]
    public void GetSlotFileName_UsesGameSlotRange(int slot, string expected) =>
        Assert.Equal(expected, AppearanceData.GetSlotFileName(slot));

    internal static byte[] CreateData(
        AppearanceRace race,
        byte tribe,
        AppearanceGender gender,
        string comment)
    {
        var data = new byte[AppearanceData.FileSize];
        BinaryPrimitives.WriteUInt32LittleEndian(data, AppearanceData.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8);
        data[0x10] = (byte)race;
        data[0x11] = (byte)gender;
        data[0x12] = 1;
        data[0x14] = tribe;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x2C), 1_700_000_000);
        Encoding.UTF8.GetBytes(comment).CopyTo(data.AsSpan(0x30));
        BinaryPrimitives.WriteUInt32LittleEndian(
            data.AsSpan(8),
            AppearanceData.CalculateChecksum(data.AsSpan(0x10)));
        return data;
    }
}
