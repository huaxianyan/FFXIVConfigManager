using FFXIVConfigManager.Application.Snapshots;
using FFXIVConfigManager.Domain.Characters;
using FFXIVConfigManager.Domain.Snapshots;

namespace FFXIVConfigManager.Application.Tests;

public sealed class PreviewSnapshotUseCaseTests
{
    [Fact]
    public async Task ExecuteAsync_ClassifiesIdenticalDifferentAndMissingFiles()
    {
        var hashA = new string('A', 64);
        var hashB = new string('B', 64);
        var manifest = CreateManifest(
            new SnapshotFileEntry("files/ADDON.DAT", "ADDON.DAT", 10, DateTimeOffset.UtcNow, hashA),
            new SnapshotFileEntry("files/HOTBAR.DAT", "HOTBAR.DAT", 20, DateTimeOffset.UtcNow, hashB),
            new SnapshotFileEntry("files/MACRO.DAT", "MACRO.DAT", 30, DateTimeOffset.UtcNow, hashA));
        var snapshot = new SnapshotLibraryEntry(
            "snapshot.zip",
            100,
            DateTimeOffset.UtcNow,
            SnapshotIntegrityStatus.Valid,
            manifest,
            []);
        var target = new CharacterConfiguration(
            manifest.Source.ProfileId,
            CharacterFolderName.Create(manifest.Source.CharacterFolder),
            "/target",
            DateTimeOffset.UtcNow,
            []);
        var hashes = new StubHashService(new Dictionary<string, StableFileDigest?>
        {
            ["ADDON.DAT"] = new(10, hashA),
            ["HOTBAR.DAT"] = new(20, hashA),
            ["MACRO.DAT"] = null,
        });
        var useCase = new PreviewSnapshotUseCase(
            new StubArchiveService(manifest),
            hashes);

        var preview = await useCase.ExecuteAsync(snapshot, target);

        Assert.Equal(SnapshotFileDifference.Identical, preview.Files[0].Difference);
        Assert.Equal(SnapshotFileDifference.Different, preview.Files[1].Difference);
        Assert.Equal(SnapshotFileDifference.MissingFromTarget, preview.Files[2].Difference);
    }

    private static SnapshotManifest CreateManifest(params SnapshotFileEntry[] files) =>
        new(
            1,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            SnapshotReason.Manual,
            new SnapshotSource(
                Guid.NewGuid(),
                "测试",
                "FFXIV_CHR0000000000000001"),
            files);

    private sealed class StubArchiveService(SnapshotManifest manifest) : ISnapshotArchiveService
    {
        public Task<CreatedSnapshot> CreateAsync(
            SnapshotArchiveRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SnapshotVerificationResult> VerifyAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SnapshotVerificationResult.Valid(manifest));

        public Task DeleteAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubHashService(
        IReadOnlyDictionary<string, StableFileDigest?> hashes) : IStableFileHashService
    {
        public Task<StableFileDigest?> TryComputeAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(hashes[Path.GetFileName(path)]);
    }
}
