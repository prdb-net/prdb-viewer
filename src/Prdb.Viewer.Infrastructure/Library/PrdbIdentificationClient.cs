using System.Net;

using Prdb.Sdk;
using Prdb.Sdk.Generated.Models;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Configuration;

using PrdbFileHashes = Prdb.Hashing.FileHashes;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Identifies local files through the documented public prdb API using the official SDK. It sends
/// only the file name, size, and content hashes, and it never mirrors the remote hash database.
/// </summary>
public sealed class PrdbIdentificationClient(IHttpMessageHandlerFactory handlers)
    : IPrdbIdentificationClient
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    public async Task<IdentificationBatchResult> IdentifyAsync(
        string credential,
        IReadOnlyList<RemoteIdentificationRequest> files,
        CancellationToken cancellationToken = default)
    {
        var status = new ResponseStatusOption();
        var client = PrdbClientFactory.Create(
            credential,
            transport: handlers.CreateHandler(PrdbConnectionVerifier.TransportName),
            retry: PrdbRetryOptions.Disabled,
            timeout: RequestTimeout);
        var request = new IdentifyVideosRequest
        {
            IncludeVideoDetails = true,
            Files = files.Select(file => new IdentifyVideoFileDto
            {
                Ref = file.VideoFileId.ToString("n"),
                Filename = file.FileName,
                Filesize = file.FileSize,
                OsHash = ForLookup(file.OsHash),
                PHash = ForLookup(file.PerceptualHash),
            }).ToList(),
        };

        try
        {
            var response = await client.Videos.Identify.PostAsync(
                request,
                configuration => configuration.Options.Add(status),
                cancellationToken);

            if (response?.Results is null)
            {
                return new IdentificationBatchResult(
                    IdentificationBatchStatus.Unavailable,
                    [],
                    "prdb returned no identification results.");
            }

            return new IdentificationBatchResult(
                IdentificationBatchStatus.Identified,
                response.Results.Select(Map).OfType<RemoteIdentification>().ToArray());
        }
        catch (Exception exception) when (exception is not OperationCanceledException ||
                                          !cancellationToken.IsCancellationRequested)
        {
            return status.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? new IdentificationBatchResult(
                    IdentificationBatchStatus.Rejected,
                    [],
                    "prdb refused the installation credential.")
                : new IdentificationBatchResult(
                    IdentificationBatchStatus.Unavailable,
                    [],
                    $"prdb could not be reached ({(int?)status.StatusCode ?? 0}).");
        }
    }

    private static string? ForLookup(string? hash) =>
        string.IsNullOrEmpty(hash) ? null : PrdbFileHashes.ForPrdbLookup(hash);

    private static RemoteIdentification? Map(IdentifyVideoResultDto result)
    {
        if (!Guid.TryParseExact(result.Ref, "N", out var videoFileId))
        {
            return null;
        }

        return new RemoteIdentification(
            videoFileId,
            result.MatchedBy switch
            {
                0 => RemoteMatchKind.OsHash,
                1 => RemoteMatchKind.PerceptualHash,
                2 => RemoteMatchKind.Filename,
                3 => RemoteMatchKind.ReleaseName,
                4 => RemoteMatchKind.Site,
                _ => null,
            },
            result.Confidence switch
            {
                1 => RemoteMatchConfidence.Partial,
                2 => RemoteMatchConfidence.Probable,
                3 => RemoteMatchConfidence.Strong,
                4 => RemoteMatchConfidence.Exact,
                5 => RemoteMatchConfidence.Ambiguous,
                _ => RemoteMatchConfidence.None,
            },
            result.VideoId?.ToString(),
            (result.Candidates ?? [])
                .Where(candidate => candidate is not null)
                .Select(candidate => candidate!.Value.ToString())
                .ToArray(),
            Site(result.Site?.Id, result.Site?.Title, result.Site?.Url),
            Work(result.Video));
    }

    private static RemoteSite? Site(Guid? id, string? title, string? url) =>
        id is null || string.IsNullOrWhiteSpace(title)
            ? null
            : new RemoteSite(id.Value.ToString(), title, url);

    private static RemoteWork? Work(VideoDetailDto? video) =>
        video?.Id is null || string.IsNullOrWhiteSpace(video.Title)
            ? null
            : new RemoteWork(
                video.Id.Value.ToString(),
                video.Title,
                Site(video.Site?.Id, video.Site?.Title, video.Site?.Url),
                (video.Actors ?? [])
                    .Select(actor => actor.Name)
                    .OfType<string>()
                    .ToArray(),
                (video.Images ?? []).Select(image => image.Url).FirstOrDefault(),
                video.ReleaseDate?.DateTime,
                video.DurationMs);
}
