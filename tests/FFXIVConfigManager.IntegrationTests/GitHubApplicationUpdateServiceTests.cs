using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FFXIVConfigManager.Infrastructure.Updates;

namespace FFXIVConfigManager.IntegrationTests;

public sealed class GitHubApplicationUpdateServiceTests : IDisposable
{
    private static readonly Uri LatestUri = new("https://updates.test/latest");
    private static readonly Uri PackageUri = new("https://updates.test/package.zip");
    private static readonly Uri ChecksumUri = new("https://updates.test/package.zip.sha256");
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-update-service-{Guid.NewGuid():N}");

    [Fact]
    public async Task CheckAsync_NewerReleaseReportsAvailable()
    {
        using var client = CreateClient(CreatePackage(), checksumOverride: null);
        var service = new GitHubApplicationUpdateService(
            client,
            new Version(1, 0, 0),
            _root,
            LatestUri);

        var status = await service.CheckAsync();

        Assert.True(status.IsUpdateAvailable);
        Assert.Equal(new Version(1, 1, 0), status.LatestRelease!.Version);
        Assert.Equal(PackageUri, status.LatestRelease.PackageUri);
        Assert.Equal(ChecksumUri, status.LatestRelease.ChecksumUri);
    }

    [Fact]
    public async Task PrepareAsync_ValidPackageCreatesVerifiedExecutableAndUpdaterCopy()
    {
        var package = CreatePackage(includeDocumentation: true);
        using var client = CreateClient(package, checksumOverride: null);
        var service = new GitHubApplicationUpdateService(
            client,
            new Version(1, 0, 0),
            _root,
            LatestUri);
        var release = (await service.CheckAsync()).LatestRelease!;

        var prepared = await service.PrepareAsync(release);

        Assert.True(File.Exists(prepared.PackageExecutablePath));
        Assert.True(File.Exists(prepared.UpdaterExecutablePath));
        Assert.Equal(
            await File.ReadAllBytesAsync(prepared.PackageExecutablePath),
            await File.ReadAllBytesAsync(prepared.UpdaterExecutablePath));
        Assert.True(File.Exists(Path.Combine(prepared.WorkingDirectory, ".ffxiv-update")));
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(
                await File.ReadAllBytesAsync(prepared.PackageExecutablePath))),
            prepared.PackageSha256);
    }

    [Fact]
    public async Task PrepareAsync_InvalidPackageStructureRejectsAndCleansWorkingDirectory()
    {
        using var client = CreateClient(CreatePackage("unexpected.exe"), checksumOverride: null);
        var service = new GitHubApplicationUpdateService(
            client,
            new Version(1, 0, 0),
            _root,
            LatestUri);
        var release = (await service.CheckAsync()).LatestRelease!;

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(release));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public async Task PrepareAsync_ChecksumMismatchRejectsAndCleansWorkingDirectory()
    {
        using var client = CreateClient(CreatePackage(), new string('A', 64));
        var service = new GitHubApplicationUpdateService(
            client,
            new Version(1, 0, 0),
            _root,
            LatestUri);
        var release = (await service.CheckAsync()).LatestRelease!;

        await Assert.ThrowsAsync<InvalidDataException>(() => service.PrepareAsync(release));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static HttpClient CreateClient(byte[] package, string? checksumOverride)
    {
        var packageHash = checksumOverride ?? Convert.ToHexString(SHA256.HashData(package));
        var releaseJson = $$"""
            {
              "tag_name": "v1.1.0",
              "html_url": "https://updates.test/releases/v1.1.0",
              "published_at": "2026-08-15T00:00:00Z",
              "assets": [
                {
                  "name": "{{GitHubApplicationUpdateService.PackageAssetName}}",
                  "browser_download_url": "{{PackageUri}}"
                },
                {
                  "name": "{{GitHubApplicationUpdateService.ChecksumAssetName}}",
                  "browser_download_url": "{{ChecksumUri}}"
                }
              ]
            }
            """;
        return new HttpClient(new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri == LatestUri)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(releaseJson, Encoding.UTF8, "application/json"),
                };
            }

            if (request.RequestUri == PackageUri)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(package),
                };
            }

            if (request.RequestUri == ChecksumUri)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{packageHash}  {GitHubApplicationUpdateService.PackageAssetName}\n"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));
    }

    private static byte[] CreatePackage(
        string entryName = GitHubApplicationUpdateService.ExecutableName,
        bool includeDocumentation = false)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, entryName, "new executable");
            if (includeDocumentation)
            {
                WriteEntry(archive, "LICENSE", "license");
                WriteEntry(archive, "README.md", "readme");
                WriteEntry(archive, "LEGAL.md", "legal");
            }
        }

        return output.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
