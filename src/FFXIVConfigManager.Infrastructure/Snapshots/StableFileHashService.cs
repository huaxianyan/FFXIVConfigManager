using System.Security.Cryptography;
using FFXIVConfigManager.Application.Snapshots;

namespace FFXIVConfigManager.Infrastructure.Snapshots;

public sealed class StableFileHashService : IStableFileHashService
{
    private const int StableReadAttempts = 3;

    public async Task<StableFileDigest?> TryComputeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= StableReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = TryGetVersion(path);
            if (before is null)
            {
                return null;
            }

            try
            {
                await using var input = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var hash = Convert.ToHexString(
                    await SHA256.HashDataAsync(input, cancellationToken));
                var after = TryGetVersion(path);

                if (after is not null && before == after)
                {
                    return new StableFileDigest(after.Length, hash);
                }
            }
            catch (FileNotFoundException) when (attempt < StableReadAttempts)
            {
            }
            catch (DirectoryNotFoundException) when (attempt < StableReadAttempts)
            {
            }

            if (attempt < StableReadAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
            }
        }

        throw new IOException($"文件在读取期间持续变化，无法计算稳定哈希：{Path.GetFileName(path)}");
    }

    private static FileVersion? TryGetVersion(string path)
    {
        var file = new FileInfo(path);
        return file.Exists
            ? new FileVersion(
                file.Length,
                new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero))
            : null;
    }

    private sealed record FileVersion(long Length, DateTimeOffset LastWriteTimeUtc);
}
