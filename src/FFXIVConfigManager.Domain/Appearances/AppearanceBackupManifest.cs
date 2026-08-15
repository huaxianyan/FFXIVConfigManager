namespace FFXIVConfigManager.Domain.Appearances;

public enum AppearanceBackupReason
{
    Manual,
    BeforeRestore,
}

public sealed record AppearanceBackupManifest(
    int FormatVersion,
    Guid BackupId,
    DateTimeOffset CreatedAtUtc,
    AppearanceBackupReason Reason,
    string SourceFileName,
    long FileSize,
    string Sha256,
    AppearanceMetadata Appearance)
{
    public const int CurrentFormatVersion = 1;
    public const string ManifestEntryName = "manifest.json";
    public const string DataEntryName = "appearance.dat";

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException($"不支持角色形象备份格式版本 {FormatVersion}。");
        }

        if (BackupId == Guid.Empty || Appearance is null)
        {
            throw new InvalidDataException("角色形象备份缺少必要信息。");
        }

        if (!IsAppearanceFileName(SourceFileName))
        {
            throw new InvalidDataException("角色形象备份的源文件名无效。");
        }

        if (FileSize != AppearanceData.FileSize ||
            Sha256.Length != 64 ||
            !Sha256.All(value => value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
        {
            throw new InvalidDataException("角色形象备份的文件元数据无效。");
        }
    }

    private static bool IsAppearanceFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
        {
            return false;
        }

        for (var slot = 1; slot <= AppearanceData.MaximumSlot; slot++)
        {
            if (string.Equals(
                    fileName,
                    AppearanceData.GetSlotFileName(slot),
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
