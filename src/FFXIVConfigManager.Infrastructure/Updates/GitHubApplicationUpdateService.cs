using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FFXIVConfigManager.Application.Updates;

namespace FFXIVConfigManager.Infrastructure.Updates;

public sealed class GitHubApplicationUpdateService : IApplicationUpdateService
{
    public const string PackageAssetName = "FFXIVConfigManager-win-x64.zip";
    public const string ChecksumAssetName = $"{PackageAssetName}.sha256";
    public const string ExecutableName = "FFXIVConfigManager.exe";

    private const long MaximumDownloadSize = 256L * 1024 * 1024;
    private const long MaximumExecutableSize = 512L * 1024 * 1024;
    private static readonly Uri DefaultLatestReleaseUri = new(
        "https://api.github.com/repos/huaxianyan/FFXIVConfigManager/releases/latest");

    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;
    private readonly string _updatesRoot;
    private readonly Uri _latestReleaseUri;

    public GitHubApplicationUpdateService(
        HttpClient httpClient,
        Version currentVersion,
        string updatesRoot,
        Uri? latestReleaseUri = null)
    {
        _httpClient = httpClient;
        _currentVersion = currentVersion;
        _updatesRoot = Path.GetFullPath(updatesRoot);
        _latestReleaseUri = latestReleaseUri ?? DefaultLatestReleaseUri;

        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("FFXIVConfigManager", currentVersion.ToString(3)));
        }

        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ApplicationUpdateStatus> CheckAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(_latestReleaseUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await JsonSerializer.DeserializeAsync<GitHubReleaseDocument>(
                stream,
                cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("GitHub Release 响应内容为空。");

        if (!TryParseVersion(document.TagName, out var version))
        {
            throw new InvalidDataException($"无法识别 Release 版本：{document.TagName}");
        }

        var package = document.Assets.FirstOrDefault(asset => asset.Name == PackageAssetName);
        var checksum = document.Assets.FirstOrDefault(asset => asset.Name == ChecksumAssetName);
        if (package is null || checksum is null)
        {
            throw new InvalidDataException("最新 Release 缺少 Windows 更新包或校验文件。");
        }

        if (!Uri.TryCreate(document.HtmlUrl, UriKind.Absolute, out var releasePageUri) ||
            !Uri.TryCreate(package.DownloadUrl, UriKind.Absolute, out var packageUri) ||
            !Uri.TryCreate(checksum.DownloadUrl, UriKind.Absolute, out var checksumUri))
        {
            throw new InvalidDataException("最新 Release 包含无效的下载地址。");
        }

        var release = new ApplicationRelease(
            version,
            document.TagName,
            document.PublishedAtUtc,
            releasePageUri,
            packageUri,
            checksumUri);
        return new ApplicationUpdateStatus(_currentVersion, release);
    }

    public async Task<PreparedApplicationUpdate> PrepareAsync(
        ApplicationRelease release,
        CancellationToken cancellationToken = default)
    {
        if (release.Version <= _currentVersion)
        {
            throw new InvalidOperationException("所选 Release 不是更高版本。");
        }

        Directory.CreateDirectory(_updatesRoot);
        var workingDirectory = Path.Combine(
            _updatesRoot,
            $"{release.TagName}-{Guid.NewGuid():N}");
        var packageDirectory = Path.Combine(workingDirectory, "package");
        var updaterDirectory = Path.Combine(workingDirectory, "updater");
        Directory.CreateDirectory(packageDirectory);
        Directory.CreateDirectory(updaterDirectory);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(workingDirectory, ".ffxiv-update"),
                release.TagName,
                cancellationToken);
            var archivePath = Path.Combine(workingDirectory, PackageAssetName);
            var expectedArchiveHash = await DownloadChecksumAsync(
                release.ChecksumUri,
                cancellationToken);
            var actualArchiveHash = await DownloadFileAsync(
                release.PackageUri,
                archivePath,
                MaximumDownloadSize,
                cancellationToken);
            if (!string.Equals(
                    expectedArchiveHash,
                    actualArchiveHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包 SHA-256 与 Release 校验文件不一致。");
            }

            var packageExecutablePath = await ExtractPackageAsync(
                archivePath,
                packageDirectory,
                cancellationToken);
            var packageExecutableHash = await ComputeSha256Async(
                packageExecutablePath,
                cancellationToken);
            var updaterExecutablePath = Path.Combine(updaterDirectory, "FFXIVConfigManager.Updater.exe");
            File.Copy(packageExecutablePath, updaterExecutablePath, overwrite: false);

            return new PreparedApplicationUpdate(
                release,
                packageExecutablePath,
                updaterExecutablePath,
                workingDirectory,
                packageExecutableHash);
        }
        catch
        {
            TryDeleteDirectory(workingDirectory);
            throw;
        }
    }

    private async Task<string> DownloadChecksumAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var contents = await response.Content.ReadAsStringAsync(cancellationToken);
        if (contents.Length > 4096)
        {
            throw new InvalidDataException("更新包校验文件过大。");
        }

        var hash = contents.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("更新包校验文件格式无效。");
        }

        return hash.ToUpperInvariant();
    }

    private async Task<string> DownloadFileAsync(
        Uri uri,
        string destinationPath,
        long maximumSize,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumSize)
        {
            throw new InvalidDataException("更新包超过允许的大小。");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[128 * 1024];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total = checked(total + read);
            if (total > maximumSize)
            {
                throw new InvalidDataException("更新包超过允许的大小。");
            }

            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await destination.FlushAsync(cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<string> ExtractPackageAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        var allowedFiles = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [ExecutableName] = MaximumExecutableSize,
            ["LICENSE"] = 128 * 1024,
            ["README.md"] = 1024 * 1024,
            ["LEGAL.md"] = 1024 * 1024,
        };
        var executableEntry = files.FirstOrDefault(entry =>
            string.Equals(entry.FullName, ExecutableName, StringComparison.Ordinal));
        if (executableEntry is null ||
            files.Length > allowedFiles.Count ||
            files.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != files.Length ||
            files.Any(entry =>
                !allowedFiles.TryGetValue(entry.FullName, out var maximumSize) ||
                entry.Length is <= 0 ||
                entry.Length > maximumSize))
        {
            throw new InvalidDataException("更新包结构无效。");
        }

        var executablePath = Path.Combine(destinationDirectory, ExecutableName);
        await using var source = executableEntry.Open();
        await using var destination = new FileStream(
            executablePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        if (destination.Length != executableEntry.Length)
        {
            throw new InvalidDataException("更新包解压后的文件大小不一致。");
        }

        return executablePath;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest);
    }

    private static bool TryParseVersion(string tagName, out Version version)
    {
        var value = tagName.StartsWith('v') ? tagName[1..] : tagName;
        return Version.TryParse(value, out version!);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record GitHubReleaseDocument(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAtUtc,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset> Assets);

    private sealed record GitHubReleaseAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}
