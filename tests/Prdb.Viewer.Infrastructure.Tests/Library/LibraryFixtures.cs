using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

internal sealed class FixtureProbe(Func<string, bool>? accepts = null) : IMediaProbe
{
    public Task<MediaProbeFacts?> InspectAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((accepts?.Invoke(path) ?? true)
            ? new MediaProbeFacts("mp4", "h264", "aac", 12_345, 1920, 1080)
            : null);
}

internal sealed class FixtureHasher(Func<string, VideoFileHashes>? hashes = null) : IVideoFileHasher
{
    public Task<VideoFileHashes> ComputeAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(hashes?.Invoke(path) ?? new VideoFileHashes(
            OsHashOf(path),
            $"p{OsHashOf(path)[1..]}",
            null));

    public static string OsHashOf(string path) =>
        Convert.ToHexString(
                System.Security.Cryptography.MD5.HashData(
                    System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path))))
            .ToLowerInvariant()[..16];
}

internal sealed class FixturePreviewGenerator(Func<string, bool>? generates = null)
    : IPreviewImageGenerator
{
    public int Generated { get; private set; }

    public async Task<bool> TryGenerateAsync(
        string sourcePath,
        double sampleSeconds,
        int width,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (generates is not null && !generates(sourcePath))
        {
            return false;
        }

        await File.WriteAllBytesAsync(destinationPath, [0xFF, 0xD8, 0xFF], cancellationToken);
        Generated++;
        return true;
    }
}

/// <summary>
/// A stand-in for the public prdb API. Tests describe what the remote ladder answers for a file
/// name, so no test needs a credential, a network, or a real library.
/// </summary>
internal sealed class FixtureIdentificationClient : IPrdbIdentificationClient
{
    private readonly Dictionary<string, Func<Guid, RemoteIdentification>> answers = new(
        StringComparer.OrdinalIgnoreCase);

    public IdentificationBatchStatus Status { get; set; } = IdentificationBatchStatus.Identified;

    public int Calls { get; private set; }

    public List<string> Credentials { get; } = [];

    public FixtureIdentificationClient Answer(
        string fileName,
        Func<Guid, RemoteIdentification> answer)
    {
        answers[fileName] = answer;
        return this;
    }

    public FixtureIdentificationClient Conclusive(
        string fileName,
        string prdbVideoId,
        string title,
        RemoteSite? site = null) =>
        Answer(fileName, id => new RemoteIdentification(
            id,
            RemoteMatchKind.OsHash,
            RemoteMatchConfidence.Exact,
            prdbVideoId,
            [],
            site,
            new RemoteWork(prdbVideoId, title, site, ["Alex Doe"], null, null, 12_345)));

    public FixtureIdentificationClient Suggestive(
        string fileName,
        string prdbVideoId,
        string title) =>
        Answer(fileName, id => new RemoteIdentification(
            id,
            RemoteMatchKind.Filename,
            RemoteMatchConfidence.Probable,
            prdbVideoId,
            [],
            null,
            new RemoteWork(prdbVideoId, title, null, [], null, null, null)));

    public FixtureIdentificationClient Unmatched(string fileName) =>
        Answer(fileName, id => new RemoteIdentification(
            id,
            null,
            RemoteMatchConfidence.None,
            null,
            [],
            null,
            null));

    public Task<IdentificationBatchResult> IdentifyAsync(
        string credential,
        IReadOnlyList<RemoteIdentificationRequest> files,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Credentials.Add(credential);

        if (Status != IdentificationBatchStatus.Identified)
        {
            return Task.FromResult(new IdentificationBatchResult(Status, [], "fixture"));
        }

        return Task.FromResult(new IdentificationBatchResult(
            IdentificationBatchStatus.Identified,
            files
                .Where(file => answers.ContainsKey(file.FileName))
                .Select(file => answers[file.FileName](file.VideoFileId))
                .ToArray()));
    }
}

/// <summary>
/// A stand-in for the published prdb list of sites, so a test can give an installation a Site
/// Directory without a credential or a network.
/// </summary>
internal sealed class FixtureSiteDirectoryClient(params RemoteSite[] sites) : IPrdbSiteDirectoryClient
{
    public SiteDirectoryFetchStatus Status { get; set; } = SiteDirectoryFetchStatus.Fetched;

    public int Calls { get; private set; }

    public Task<SiteDirectoryFetchResult> FetchAsync(
        string credential,
        CancellationToken cancellationToken = default)
    {
        Calls++;

        return Task.FromResult(Status == SiteDirectoryFetchStatus.Fetched
            ? new SiteDirectoryFetchResult(SiteDirectoryFetchStatus.Fetched, sites)
            : new SiteDirectoryFetchResult(Status, [], "fixture"));
    }
}

internal sealed class FixtureConnectionVerifier(
    PrdbVerificationOutcome outcome = PrdbVerificationOutcome.Verified) : IPrdbConnectionVerifier
{
    public Task<PrdbVerificationOutcome> VerifyAsync(
        string credential,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(outcome);
}
