using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVConfigManager.Application.Settings;

namespace FFXIVConfigManager.Infrastructure.Settings;

public sealed class JsonSettingsStore(string settingsPath) : ISettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<ApplicationSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(settingsPath))
        {
            return ApplicationSettings.Empty;
        }

        await using var stream = new FileStream(
            settingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var settings = await JsonSerializer.DeserializeAsync<ApplicationSettings>(
            stream,
            SerializerOptions,
            cancellationToken);

        if (settings is null)
        {
            throw new InvalidDataException("设置文件内容为空。");
        }

        if (settings.SchemaVersion > ApplicationSettings.CurrentSchemaVersion)
        {
            throw new NotSupportedException(
                $"设置文件版本 {settings.SchemaVersion} 高于当前支持的版本 " +
                $"{ApplicationSettings.CurrentSchemaVersion}。");
        }

        return settings;
    }

    public async Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(settingsPath)
            ?? throw new ArgumentException("设置文件路径必须包含目录。", nameof(settingsPath));
        Directory.CreateDirectory(directory);

        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(settingsPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    SerializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
