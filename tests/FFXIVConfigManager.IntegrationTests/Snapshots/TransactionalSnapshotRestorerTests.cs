using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Snapshots;
using FFXIVConfigManager.Infrastructure.Snapshots;

namespace FFXIVConfigManager.IntegrationTests.Snapshots;

public sealed class TransactionalSnapshotRestorerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"FFXIVConfigManager-restore-{Guid.NewGuid():N}");

    [Fact]
    public async Task RestoreAsync_ReplacesFilesAndCleansTransactionDirectory()
    {
        var created = await CreateSnapshotAsync(("ADDON.DAT", "new-addon"), ("HOTBAR.DAT", "new-hotbar"));
        var target = CreateTarget(("ADDON.DAT", "old-addon"), ("HOTBAR.DAT", "old-hotbar"));
        var restorer = new TransactionalSnapshotRestorer();

        var result = await restorer.RestoreAsync(new SnapshotRestoreRequest(
            created.ArchivePath,
            created.Manifest,
            target));

        Assert.Equal(2, result.RestoredFileCount);
        Assert.Equal("new-addon", await File.ReadAllTextAsync(Path.Combine(target, "ADDON.DAT")));
        Assert.Equal("new-hotbar", await File.ReadAllTextAsync(Path.Combine(target, "HOTBAR.DAT")));
        Assert.False(Directory.Exists(Path.Combine(target, ".ffxivconfigmanager")));
    }

    [Fact]
    public async Task RestoreAsync_FailureAfterFirstCommitRollsBackAllChanges()
    {
        var created = await CreateSnapshotAsync(("ADDON.DAT", "new-addon"), ("HOTBAR.DAT", "new-hotbar"));
        var target = CreateTarget(("ADDON.DAT", "old-addon"), ("HOTBAR.DAT", "old-hotbar"));
        var restorer = new TransactionalSnapshotRestorer(new FailAtIndexFaultInjector(1));

        var exception = await Assert.ThrowsAsync<SnapshotRestoreException>(() =>
            restorer.RestoreAsync(new SnapshotRestoreRequest(
                created.ArchivePath,
                created.Manifest,
                target)));

        Assert.True(exception.RollbackCompleted);
        Assert.Equal("old-addon", await File.ReadAllTextAsync(Path.Combine(target, "ADDON.DAT")));
        Assert.Equal("old-hotbar", await File.ReadAllTextAsync(Path.Combine(target, "HOTBAR.DAT")));
        Assert.False(Directory.Exists(Path.Combine(target, ".ffxivconfigmanager")));
    }

    [Fact]
    public async Task RestoreAsync_CancellationAfterCommitRollsBackChanges()
    {
        var created = await CreateSnapshotAsync(("ADDON.DAT", "new-addon"));
        var target = CreateTarget(("ADDON.DAT", "old-addon"));
        using var cancellation = new CancellationTokenSource();
        var restorer = new TransactionalSnapshotRestorer(
            new CancelAtIndexFaultInjector(0, cancellation));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            restorer.RestoreAsync(
                new SnapshotRestoreRequest(
                    created.ArchivePath,
                    created.Manifest,
                    target),
                cancellation.Token));

        Assert.Equal("old-addon", await File.ReadAllTextAsync(Path.Combine(target, "ADDON.DAT")));
        Assert.False(Directory.Exists(Path.Combine(target, ".ffxivconfigmanager")));
    }

    [Fact]
    public async Task RecoverAsync_RollsBackTransactionInterruptedAfterCommit()
    {
        var target = CreateTarget(("ADDON.DAT", "new-addon"));
        var operationId = Guid.NewGuid();
        var transaction = Path.Combine(
            target,
            ".ffxivconfigmanager",
            "transactions",
            operationId.ToString("N"));
        var rollback = Path.Combine(transaction, "rollback");
        Directory.CreateDirectory(rollback);
        await File.WriteAllTextAsync(Path.Combine(rollback, "ADDON.DAT"), "old-addon");
        var journal = $$"""
            {
              "operationId": "{{operationId}}",
              "archivePath": "snapshot.zip",
              "targetDirectory": {{System.Text.Json.JsonSerializer.Serialize(Path.GetFullPath(target))}},
              "state": "Committing",
              "files": [
                {
                  "fileName": "ADDON.DAT",
                  "expectedSha256": "{{new string('A', 64)}}",
                  "state": "Committed",
                  "hadOriginal": true,
                  "originalSha256": "{{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("old-addon")))}}"
                }
              ]
            }
            """;
        await File.WriteAllTextAsync(Path.Combine(transaction, "journal.json"), journal);
        var restorer = new TransactionalSnapshotRestorer();

        var results = await restorer.RecoverAsync([target]);

        var result = Assert.Single(results);
        Assert.True(result.Recovered);
        Assert.Equal(operationId, result.OperationId);
        Assert.Equal("old-addon", await File.ReadAllTextAsync(Path.Combine(target, "ADDON.DAT")));
        Assert.False(Directory.Exists(Path.Combine(target, ".ffxivconfigmanager")));
    }

    [Fact]
    public async Task RestoreAsync_RollbackDeletesFileThatDidNotExistBeforeRestore()
    {
        var created = await CreateSnapshotAsync(("ADDON.DAT", "new-addon"), ("HOTBAR.DAT", "new-hotbar"));
        var target = CreateTarget(("HOTBAR.DAT", "old-hotbar"));
        var restorer = new TransactionalSnapshotRestorer(new FailAtIndexFaultInjector(1));

        var exception = await Assert.ThrowsAsync<SnapshotRestoreException>(() =>
            restorer.RestoreAsync(new SnapshotRestoreRequest(
                created.ArchivePath,
                created.Manifest,
                target)));

        Assert.True(exception.RollbackCompleted);
        Assert.False(File.Exists(Path.Combine(target, "ADDON.DAT")));
        Assert.Equal("old-hotbar", await File.ReadAllTextAsync(Path.Combine(target, "HOTBAR.DAT")));
    }

    private async Task<CreatedSnapshot> CreateSnapshotAsync(
        params (string FileName, string Content)[] files)
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        var sources = new List<SnapshotFileSource>();
        foreach (var (fileName, content) in files)
        {
            var path = Path.Combine(source, fileName);
            await File.WriteAllTextAsync(path, content);
            sources.Add(new SnapshotFileSource(path, $"files/{fileName}", fileName));
        }

        var service = new ZipSnapshotArchiveService();
        return await service.CreateAsync(new SnapshotArchiveRequest(
            Path.Combine(_root, "library"),
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-08-11T04:00:00Z"),
            SnapshotReason.Manual,
            new SnapshotSource(Guid.NewGuid(), "测试", "FFXIV_CHR0000000000000001"),
            sources));
    }

    private string CreateTarget(params (string FileName, string Content)[] files)
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        foreach (var (fileName, content) in files)
        {
            File.WriteAllText(Path.Combine(target, fileName), content);
        }

        return target;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FailAtIndexFaultInjector(int failureIndex) : ITransactionFaultInjector
    {
        public void BeforeReplace(string fileName, int index)
        {
            if (index == failureIndex)
            {
                throw new IOException($"Injected failure for {fileName}");
            }
        }
    }

    private sealed class CancelAtIndexFaultInjector(
        int cancellationIndex,
        CancellationTokenSource cancellation) : ITransactionFaultInjector
    {
        public void BeforeReplace(string fileName, int index)
        {
            if (index == cancellationIndex)
            {
                cancellation.Cancel();
            }
        }
    }
}
