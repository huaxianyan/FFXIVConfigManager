using System.Buffers.Binary;
using System.Text;

namespace FFXIVConfigManager.Domain.Appearances;

public enum AppearanceRace : byte
{
    Hyur = 1,
    Elezen = 2,
    Lalafell = 3,
    Miqote = 4,
    Roegadyn = 5,
    AuRa = 6,
    Hrothgar = 7,
    Viera = 8,
}

public enum AppearanceGender : byte
{
    Male = 0,
    Female = 1,
}

public sealed record AppearanceMetadata(
    AppearanceRace Race,
    byte Tribe,
    AppearanceGender Gender,
    string Comment,
    uint FormatVersion,
    DateTimeOffset SavedAtUtc);

public static class AppearanceData
{
    public const int FileSize = 212;
    public const uint Magic = 0x2013FF14;
    public const int CommentOffset = 0x30;
    public const int MaximumSlot = 40;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static bool TryParse(
        ReadOnlySpan<byte> data,
        out AppearanceMetadata? metadata,
        out string? error)
    {
        metadata = null;
        if (data.Length != FileSize)
        {
            error = $"文件大小应为 {FileSize} 字节，实际为 {data.Length} 字节。";
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            error = "文件标识无效。";
            return false;
        }

        var storedChecksum = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        if (storedChecksum != CalculateChecksum(data[0x10..]))
        {
            error = "文件校验和不匹配。";
            return false;
        }

        var raceValue = data[0x10];
        var genderValue = data[0x11];
        var tribe = data[0x14];
        if (!Enum.IsDefined(typeof(AppearanceRace), raceValue) ||
            !Enum.IsDefined(typeof(AppearanceGender), genderValue) ||
            tribe is < 1 or > 16)
        {
            error = "种族、部族或性别字段无效。";
            return false;
        }

        var commentBytes = data[CommentOffset..];
        var terminator = commentBytes.IndexOf((byte)0);
        if (terminator >= 0)
        {
            commentBytes = commentBytes[..terminator];
        }

        string comment;
        try
        {
            comment = StrictUtf8.GetString(commentBytes);
        }
        catch (DecoderFallbackException)
        {
            error = "备注不是有效的 UTF-8 文本。";
            return false;
        }

        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(data[0x2C..]);
        try
        {
            metadata = new AppearanceMetadata(
                (AppearanceRace)raceValue,
                tribe,
                (AppearanceGender)genderValue,
                comment,
                BinaryPrimitives.ReadUInt32LittleEndian(data[4..]),
                DateTimeOffset.FromUnixTimeSeconds(timestamp));
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "保存时间无效。";
            return false;
        }

        error = null;
        return true;
    }

    public static uint CalculateChecksum(ReadOnlySpan<byte> payload)
    {
        uint checksum = 0;
        for (var index = 0; index < payload.Length; index++)
        {
            checksum ^= (uint)payload[index] << (index % 24);
        }

        return checksum;
    }

    public static string GetSlotFileName(int slot)
    {
        if (slot is < 1 or > MaximumSlot)
        {
            throw new ArgumentOutOfRangeException(nameof(slot), "角色形象栏位必须在 1～40 之间。");
        }

        return $"FFXIV_CHARA_{slot:D2}.dat";
    }
}
