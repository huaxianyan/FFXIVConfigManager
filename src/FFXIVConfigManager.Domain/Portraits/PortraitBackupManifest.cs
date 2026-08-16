using FFXIVConfigManager.Domain.Characters;

namespace FFXIVConfigManager.Domain.Portraits;

public enum PortraitBackupReason
{
    Manual,
    BeforeTransfer,
}

public sealed record PortraitBackupSource(
    string CharacterFolder,
    int GearsetNumber,
    byte ClassJobId,
    string GearsetName);

public sealed record PortraitBackupManifest(
    int FormatVersion,
    Guid BackupId,
    DateTimeOffset CreatedAtUtc,
    PortraitBackupReason Reason,
    string SchemeName,
    string Note,
    PortraitBackupSource Source,
    int DataSize,
    string Sha256,
    DateTimeOffset PortraitLastUpdatedUtc)
{
    public const int CurrentFormatVersion = 1;
    public const string ManifestEntryName = "manifest.json";
    public const string DataEntryName = "portrait.dat";
    public const int MaximumSchemeNameLength = 100;
    public const int MaximumNoteLength = 500;

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException($"不支持肖像备份格式版本 {FormatVersion}。");
        }

        if (BackupId == Guid.Empty || Source is null)
        {
            throw new InvalidDataException("肖像备份缺少必要信息。");
        }

        ValidateText(SchemeName, MaximumSchemeNameLength, "备份方案名");
        ValidateText(Note, MaximumNoteLength, "备份备注");
        if (!CharacterFolderName.TryCreate(Source.CharacterFolder, out _))
        {
            throw new InvalidDataException("肖像备份的来源角色目录名无效。");
        }

        if (Source.GearsetNumber is < 1 or > 100 ||
            Source.ClassJobId == 0 ||
            string.IsNullOrWhiteSpace(Source.GearsetName) ||
            Source.GearsetName.Length > 48)
        {
            throw new InvalidDataException("肖像备份的来源套装信息无效。");
        }

        if (DataSize != PortraitData.SerializedSize ||
            Sha256.Length != 64 ||
            !Sha256.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
        {
            throw new InvalidDataException("肖像备份的数据校验信息无效。");
        }
    }

    private static void ValidateText(string value, int maximumLength, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > maximumLength)
        {
            throw new InvalidDataException($"{fieldName}不能为空、包含首尾空白或超过 {maximumLength} 个字符。");
        }
    }
}
