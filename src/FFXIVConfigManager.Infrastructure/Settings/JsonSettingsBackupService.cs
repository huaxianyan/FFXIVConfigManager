using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVConfigManager.Application.Settings;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Profiles;

namespace FFXIVConfigManager.Infrastructure.Settings;

public sealed class JsonSettingsBackupService(
    ISettingsStore settingsStore,
    SettingsService settingsService,
    TimeProvider timeProvider) : ISettingsBackupService
{
    private const string BackupFileName = "settings.ffxivconfig-settings.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<SettingsBackupStatus> GetStatusAsync(
        string libraryRoot,
        CancellationToken cancellationToken = default)
    {
        var path = GetBackupPath(libraryRoot);
        if (!File.Exists(path))
        {
            return new SettingsBackupStatus(
                path,
                Exists: false,
                IsValid: false,
                CreatedAtUtc: null,
                SettingsBackupScope.None,
                []);
        }

        try
        {
            var document = await ReadAndValidateAsync(path, cancellationToken);
            return new SettingsBackupStatus(
                path,
                Exists: true,
                IsValid: true,
                document.CreatedAtUtc,
                document.IncludedScopes,
                []);
        }
        catch (Exception exception) when (exception is JsonException or IOException or
                                          InvalidDataException or NotSupportedException or
                                          ArgumentException)
        {
            return new SettingsBackupStatus(
                path,
                Exists: true,
                IsValid: false,
                CreatedAtUtc: null,
                SettingsBackupScope.None,
                [exception.Message]);
        }
    }

    public async Task<SettingsBackupStatus> BackupAsync(
        string libraryRoot,
        SettingsBackupScope scopes,
        CancellationToken cancellationToken = default)
    {
        ValidateScopes(scopes);
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var document = new SettingsBackupDocument(
            SettingsBackupDocument.CurrentFormatVersion,
            timeProvider.GetUtcNow(),
            scopes,
            scopes.HasFlag(SettingsBackupScope.CustomProfiles)
                ? settings.CustomProfiles
                : [],
            scopes.HasFlag(SettingsBackupScope.CharacterAliases)
                ? settings.CharacterAliases
                : []);
        var path = GetBackupPath(libraryRoot);
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{BackupFileName}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            await ReadAndValidateAsync(temporaryPath, cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return await GetStatusAsync(libraryRoot, cancellationToken);
    }

    public async Task RestoreAsync(
        string libraryRoot,
        SettingsBackupScope scopes,
        CancellationToken cancellationToken = default)
    {
        ValidateScopes(scopes);
        var document = await ReadAndValidateAsync(GetBackupPath(libraryRoot), cancellationToken);
        await settingsService.RestoreBackupAsync(document, scopes, cancellationToken);
    }

    private static async Task<SettingsBackupDocument> ReadAndValidateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("软件设置备份不存在。", path);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<SettingsBackupDocument>(
            stream,
            SerializerOptions,
            cancellationToken)
            ?? throw new InvalidDataException("软件设置备份内容为空。");
        if (document.FormatVersion != SettingsBackupDocument.CurrentFormatVersion)
        {
            throw new NotSupportedException($"不支持软件设置备份格式版本 {document.FormatVersion}。");
        }

        ValidateDocument(document);
        return document;
    }

    private static void ValidateDocument(SettingsBackupDocument document)
    {
        ValidateScopes(document.IncludedScopes);
        if (document.CreatedAtUtc == default || document.CreatedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("软件设置备份缺少有效的 UTC 创建时间。");
        }

        if (document.CustomProfiles is null || document.CharacterAliases is null)
        {
            throw new InvalidDataException("软件设置备份缺少设置数据。");
        }

        if ((!document.IncludedScopes.HasFlag(SettingsBackupScope.CustomProfiles) &&
             document.CustomProfiles.Count != 0) ||
            (!document.IncludedScopes.HasFlag(SettingsBackupScope.CharacterAliases) &&
             document.CharacterAliases.Count != 0))
        {
            throw new InvalidDataException("软件设置备份包含未声明范围的数据。");
        }

        if (document.CustomProfiles.Any(profile =>
                profile.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(profile.Name) ||
                string.IsNullOrWhiteSpace(profile.ConfigRoot) ||
                !Enum.IsDefined(profile.Region) ||
                profile.Origin != GameProfileOrigin.User))
        {
            throw new InvalidDataException("软件设置备份包含无效的自定义配置源。");
        }

        if (document.CharacterAliases.Any(alias =>
                alias.ProfileId == Guid.Empty ||
                !CharacterFolderName.TryCreate(alias.CharacterFolder, out _) ||
                string.IsNullOrWhiteSpace(alias.Alias)))
        {
            throw new InvalidDataException("软件设置备份包含无效的角色标记。");
        }
    }

    private static string GetBackupPath(string libraryRoot)
    {
        if (string.IsNullOrWhiteSpace(libraryRoot))
        {
            throw new ArgumentException("备份库目录不能为空。", nameof(libraryRoot));
        }

        return Path.Combine(Path.GetFullPath(libraryRoot), "settings", BackupFileName);
    }

    private static void ValidateScopes(SettingsBackupScope scopes)
    {
        if (scopes == SettingsBackupScope.None || (scopes & ~SettingsBackupScope.All) != 0)
        {
            throw new ArgumentException("必须选择至少一个有效的软件设置范围。", nameof(scopes));
        }
    }
}
