using System.Buffers.Binary;
using FFXIVConfigManager.Domain.Portraits;

namespace FFXIVConfigManager.Domain.Tests;

public sealed class PortraitDataTests
{
    [Fact]
    public void TryParse_RejectsUnexpectedFieldTag()
    {
        var data = CreateRecord(1_700_000_000);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), 99);

        Assert.False(PortraitData.TryParse(data, out _, out var error));
        Assert.Contains("字段标记无效", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ApplyVisualDataTo_PreservesTargetIdentityAndUpdatesTime()
    {
        var sourceBytes = CreateRecord(1_700_000_000, 10);
        var targetBytes = CreateRecord(1_600_000_000, 80);
        Assert.True(PortraitData.TryParse(sourceBytes, out var source, out _));
        Assert.True(PortraitData.TryParse(targetBytes, out var target, out _));
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        var merged = source!.ApplyVisualDataTo(target!, now);

        Assert.Equal(sourceBytes.AsSpan(4, 0x58).ToArray(), merged.AsSpan(4, 0x58).ToArray());
        Assert.Equal(targetBytes.AsSpan(0, 4).ToArray(), merged.AsSpan(0, 4).ToArray());
        Assert.Equal(targetBytes.AsSpan(0x62).ToArray(), merged.AsSpan(0x62).ToArray());
        Assert.Equal(1_800_000_000u,
            BinaryPrimitives.ReadUInt32LittleEndian(merged.AsSpan(PortraitData.LastUpdatedOffset)));
    }

    private static byte[] CreateRecord(uint updatedAt, byte value = 1)
    {
        var fields = new (ushort Tag, int Length)[]
        {
            (0, 2), (2, 2), (3, 2), (4, 2), (5, 2), (6, 4), (7, 2),
            (8, 6), (9, 6), (10, 2), (11, 2), (16, 4), (17, 2), (18, 4),
            (14, 4), (15, 2), (19, 4), (20, 4), (21, 4), (22, 6), (23, 4), (24, 4),
        };
        var record = new byte[PortraitData.SerializedSize];
        var offset = 0;
        foreach (var field in fields)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(offset), field.Tag);
            offset += 2;
            record.AsSpan(offset, field.Length).Fill(value);
            offset += field.Length;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(PortraitData.LastUpdatedOffset), updatedAt);
        return record;
    }
}
