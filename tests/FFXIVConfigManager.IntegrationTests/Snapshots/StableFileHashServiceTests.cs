using System.Security.Cryptography;
using System.Text;
using FFXIVConfigManager.Infrastructure.Snapshots;

namespace FFXIVConfigManager.IntegrationTests.Snapshots;

public sealed class StableFileHashServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-hash-{Guid.NewGuid():N}");

    [Fact]
    public async Task TryComputeAsync_ReturnsSizeAndSha256ForStableFile()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "ADDON.DAT");
        await File.WriteAllTextAsync(path, "stable");
        var service = new StableFileHashService();

        var digest = await service.TryComputeAsync(path);

        Assert.NotNull(digest);
        Assert.Equal(6, digest.Size);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("stable"))),
            digest.Sha256);
    }

    [Fact]
    public async Task TryComputeAsync_MissingFileReturnsNull()
    {
        var service = new StableFileHashService();

        Assert.Null(await service.TryComputeAsync(Path.Combine(_root, "missing.DAT")));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
