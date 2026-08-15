namespace FFXIVConfigManager.Application.Updates;

public sealed record ApplicationRelease(
    Version Version,
    string TagName,
    DateTimeOffset PublishedAtUtc,
    Uri ReleasePageUri,
    Uri PackageUri,
    Uri ChecksumUri);

public sealed record ApplicationUpdateStatus(
    Version CurrentVersion,
    ApplicationRelease? LatestRelease)
{
    public bool IsUpdateAvailable =>
        LatestRelease is not null && LatestRelease.Version > CurrentVersion;
}

public sealed record PreparedApplicationUpdate(
    ApplicationRelease Release,
    string PackageExecutablePath,
    string UpdaterExecutablePath,
    string WorkingDirectory,
    string PackageSha256);

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateStatus> CheckAsync(
        CancellationToken cancellationToken = default);

    Task<PreparedApplicationUpdate> PrepareAsync(
        ApplicationRelease release,
        CancellationToken cancellationToken = default);
}
