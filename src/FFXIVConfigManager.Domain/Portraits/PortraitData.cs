using System.Buffers.Binary;

namespace FFXIVConfigManager.Domain.Portraits;

public sealed class PortraitData
{
    public const int SerializedSize = 118;
    public const int LastUpdatedOffset = 0x5E;

    private static readonly (ushort Tag, int ValueLength)[] Fields =
    [
        (0, 2),
        (2, 2),
        (3, 2),
        (4, 2),
        (5, 2),
        (6, 4),
        (7, 2),
        (8, 6),
        (9, 6),
        (10, 2),
        (11, 2),
        (16, 4),
        (17, 2),
        (18, 4),
        (14, 4),
        (15, 2),
        (19, 4),
        (20, 4),
        (21, 4),
        (22, 6),
        (23, 4),
        (24, 4),
    ];

    private PortraitData(byte[] serializedRecord, DateTimeOffset lastUpdatedUtc)
    {
        SerializedRecord = serializedRecord;
        LastUpdatedUtc = lastUpdatedUtc;
    }

    public byte[] SerializedRecord { get; }

    public DateTimeOffset LastUpdatedUtc { get; }

    public static bool TryParse(
        ReadOnlySpan<byte> data,
        out PortraitData? portrait,
        out string? error)
    {
        portrait = null;
        error = null;
        if (data.Length != SerializedSize)
        {
            error = $"肖像记录必须正好是 {SerializedSize} 字节。";
            return false;
        }

        var offset = 0;
        foreach (var field in Fields)
        {
            var actualTag = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            if (actualTag != field.Tag)
            {
                error = $"肖像记录字段标记无效：偏移 0x{offset:X2} 应为 {field.Tag}，实际为 {actualTag}。";
                return false;
            }

            offset += sizeof(ushort) + field.ValueLength;
        }

        if (offset != SerializedSize)
        {
            error = "肖像记录字段长度无效。";
            return false;
        }

        var unixTime = BinaryPrimitives.ReadUInt32LittleEndian(data[LastUpdatedOffset..]);
        try
        {
            var lastUpdated = DateTimeOffset.FromUnixTimeSeconds(unixTime);
            portrait = new PortraitData(data.ToArray(), lastUpdated);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "肖像记录的最后更新时间无效。";
            return false;
        }
    }

    public byte[] ApplyVisualDataTo(PortraitData target, DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(target);
        var merged = target.SerializedRecord.ToArray();

        // 标记 2～20 是游戏复制肖像时使用的画面参数。保留目标的记录 ID、
        // 角色数据、装备校验和与关联状态，只替换画面并更新时间。
        SerializedRecord.AsSpan(0x04, 0x58).CopyTo(merged.AsSpan(0x04, 0x58));
        BinaryPrimitives.WriteUInt32LittleEndian(
            merged.AsSpan(LastUpdatedOffset),
            checked((uint)updatedAtUtc.ToUnixTimeSeconds()));
        return merged;
    }
}
