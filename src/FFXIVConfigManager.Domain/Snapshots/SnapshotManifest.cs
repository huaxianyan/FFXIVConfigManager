namespace FFXIVConfigManager.Domain.Snapshots;

public enum SnapshotReason
{
    Manual,
    BeforeMigration,
    BeforeRestore,
    MigrationSource,
}

public sealed record SnapshotFileEntry(
    string ArchivePath,
    string OriginalFileName,
    long Size,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

public sealed record SnapshotSource(
    Guid ProfileId,
    string ProfileName,
    string CharacterFolder);

public sealed record SnapshotManifest(
    int FormatVersion,
    Guid SnapshotId,
    DateTimeOffset CreatedAtUtc,
    SnapshotReason Reason,
    SnapshotSource Source,
    IReadOnlyList<SnapshotFileEntry> Files)
{
    public const int CurrentFormatVersion = 1;
    public const string ManifestEntryName = "manifest.json";

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new InvalidDataException($"不支持备份格式版本 {FormatVersion}。");
        }

        if (SnapshotId == Guid.Empty)
        {
            throw new InvalidDataException("备份 ID 不能为空。");
        }

        if (Source is null || string.IsNullOrWhiteSpace(Source.CharacterFolder))
        {
            throw new InvalidDataException("备份缺少来源角色目录。");
        }

        if (Files is null || Files.Count == 0)
        {
            throw new InvalidDataException("备份不包含任何配置文件。");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Files)
        {
            if (file is null)
            {
                throw new InvalidDataException("备份包含空文件条目。");
            }

            if (string.IsNullOrWhiteSpace(file.OriginalFileName) ||
                file.OriginalFileName.Contains('/') ||
                file.OriginalFileName.Contains('\\') ||
                file.OriginalFileName.Contains(':') ||
                !IsSafeArchivePath(file.ArchivePath) ||
                !file.ArchivePath.Equals(
                    $"files/{file.OriginalFileName}",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException($"备份文件路径无效：{file.ArchivePath}");
            }

            if (!paths.Add(file.ArchivePath))
            {
                throw new InvalidDataException($"备份包含重复路径：{file.ArchivePath}");
            }

            if (file.Size < 0 ||
                file.Sha256.Length != 64 ||
                !file.Sha256.All(IsHexadecimal))
            {
                throw new InvalidDataException($"备份文件元数据无效：{file.ArchivePath}");
            }
        }
    }

    private static bool IsSafeArchivePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !path.StartsWith('/') &&
        !path.Contains(':') &&
        !path.Contains('\\') &&
        path.Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not "..");

    private static bool IsHexadecimal(char value) =>
        value is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
