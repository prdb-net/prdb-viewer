using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// Inspects every file as the conservative cross-client baseline, so a test that is not about
/// playability gets Videos an unqualified client can play.
/// </summary>
internal sealed class FixtureProbe(Func<string, bool>? accepts = null) : IMediaProbe
{
    public static readonly MediaConfiguration Baseline =
        new("matroska,webm", "vp8", "vorbis")
        {
            Width = 1920,
            Height = 1080,
            FrameRate = 25,
            BitDepth = 8,
            VideoBitrate = 2_000_000,
            AudioChannels = 2,
            AudioSampleRate = 48_000,
        };

    /// <summary>Ordinary H.264/AAC in MP4: a broad candidate, and a client question.</summary>
    public static readonly MediaConfiguration ClientDependent =
        new("mov,mp4,m4a,3gp,3g2,mj2", "h264", "aac")
        {
            VideoProfile = "High",
            VideoLevel = 40,
            Width = 1920,
            Height = 1080,
            FrameRate = 25,
            BitDepth = 8,
            VideoBitrate = 4_000_000,
            AudioChannels = 2,
            AudioSampleRate = 48_000,
        };

    public Task<MediaProbeFacts?> InspectAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((accepts?.Invoke(path) ?? true)
            ? new MediaProbeFacts(Baseline, 12_345)
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
    /// <summary>
    /// The Actor prdb sends with a work, named and identified. The identity is derived from the
    /// name so two tests naming the same Actor mean the same Actor, which is what a test about
    /// their page needs and what a random one could not give it.
    /// </summary>
    public static RemoteActor Actor(string name)
    {
        var digest = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant()));

        return new RemoteActor(name, new Guid(digest).ToString());
    }

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
            new RemoteWork(prdbVideoId, title, site, [Actor("Alex Doe")], null, null, 12_345)));

    /// <summary>Recognises a file, and names who prdb says is in it.</summary>
    public FixtureIdentificationClient Credits(
        string fileName,
        string prdbVideoId,
        string title,
        IReadOnlyList<string> actors,
        RemoteSite? site = null) =>
        Answer(fileName, id => new RemoteIdentification(
            id,
            RemoteMatchKind.OsHash,
            RemoteMatchConfidence.Exact,
            prdbVideoId,
            [],
            site,
            new RemoteWork(
                prdbVideoId,
                title,
                site,
                actors.Select(Actor).ToArray(),
                null,
                null,
                12_345)));

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

/// <summary>
/// A stand-in for the works prdb answers with when the Enrichment lane asks about them. A test
/// that says nothing about enrichment gets an installation prdb has nothing further to add to.
/// </summary>
internal sealed class FixtureWorkDetailClient : IPrdbWorkDetailClient
{
    private readonly Dictionary<string, RemoteWork> works = new(StringComparer.OrdinalIgnoreCase);

    public WorkDetailFetchStatus Status { get; set; } = WorkDetailFetchStatus.Fetched;

    public int Calls { get; private set; }

    public List<string> Asked { get; } = [];

    public FixtureWorkDetailClient Answers(RemoteWork work)
    {
        works[work.PrdbVideoId] = work;
        return this;
    }

    public Task<WorkDetailFetchResult> FetchAsync(
        string credential,
        IReadOnlyList<string> prdbVideoIds,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Asked.AddRange(prdbVideoIds);

        if (Status != WorkDetailFetchStatus.Fetched)
        {
            return Task.FromResult(new WorkDetailFetchResult(Status, [], "The fixture refused."));
        }

        return Task.FromResult(new WorkDetailFetchResult(
            WorkDetailFetchStatus.Fetched,
            prdbVideoIds
                .Select(id => works.GetValueOrDefault(id))
                .OfType<RemoteWork>()
                .ToArray()));
    }
}

/// <summary>
/// A stand-in for what prdb says about an Actor. A test that says nothing about profiles gets an
/// installation whose Actors are names and nothing more, which is a real state and the one every
/// screen has to draw anyway.
/// </summary>
internal sealed class FixtureActorProfileClient : IPrdbActorProfileClient
{
    private readonly Dictionary<string, RemoteActorProfile> profiles =
        new(StringComparer.OrdinalIgnoreCase);

    public ActorProfileFetchStatus Status { get; set; } = ActorProfileFetchStatus.Fetched;

    public int Calls { get; private set; }

    public List<string> Asked { get; } = [];

    public FixtureActorProfileClient Answers(RemoteActorProfile profile)
    {
        profiles[profile.Id] = profile;
        return this;
    }

    /// <summary>One Actor as prdb holds them, named and described.</summary>
    public FixtureActorProfileClient Describes(
        string actorId,
        string name,
        IReadOnlyList<RemoteActorImage>? images = null,
        IReadOnlyList<RemoteActorAlias>? aliases = null) =>
        Answers(new RemoteActorProfile(
            actorId,
            name,
            "Female",
            new DateTime(1994, 3, 17, 0, 0, 0, DateTimeKind.Utc),
            "Exact",
            null,
            "Example City",
            "Brown",
            "Green",
            null,
            170,
            null,
            null,
            null,
            "Example Nation",
            null,
            2014,
            null,
            "A small star behind the left ear.",
            null,
            images ?? [],
            aliases ?? [],
            [new RemoteActorLink("https://example.invalid/actor", "Twitter")],
            [$"{name} has been in front of a camera since 2014."]));

    public Task<ActorProfileFetchResult> FetchAsync(
        string credential,
        IReadOnlyList<string> prdbActorIds,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        Asked.AddRange(prdbActorIds);

        if (Status != ActorProfileFetchStatus.Fetched)
        {
            return Task.FromResult(new ActorProfileFetchResult(Status, [], "The fixture refused."));
        }

        return Task.FromResult(new ActorProfileFetchResult(
            ActorProfileFetchStatus.Fetched,
            prdbActorIds
                .Select(id => profiles.GetValueOrDefault(id))
                .OfType<RemoteActorProfile>()
                .ToArray()));
    }
}

/// <summary>
/// A stand-in for the pictures prdb offers, over the transport that carries no credential. It
/// answers with a one-pixel PNG, or with nothing at all, which are the two cases the retention has
/// to tell apart.
/// </summary>
internal sealed class FixtureRetainedImageClient : IRetainedImageClient
{
    private static readonly byte[] Png =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
    ];

    public bool Answers { get; set; } = true;

    public List<string> Asked { get; } = [];

    public Task<RetainedImage> FetchAsync(string url, CancellationToken cancellationToken = default)
    {
        Asked.Add(url);

        return Task.FromResult(Answers
            ? new RetainedImage(Png, "image/png")
            : new RetainedImage(null, null));
    }
}
