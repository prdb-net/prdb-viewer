using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// Asks prdb again about the works this library has already established, so that what one
/// identification paid for is kept: the identity of every Actor it credited, and the facts about
/// the work the identification answer carried.
/// </summary>
/// <remarks>
/// It exists because identification never runs twice for a file it has already answered — rightly,
/// since the answer would be the same and it would cost a hash. The question here is the cheap
/// one: <c>POST /videos/batch</c> takes the identifiers the library already holds and costs no
/// hashing and no matching. After the backfill it keeps running as a slow refresh behind
/// <see cref="RefreshHorizon"/>, which is what makes an Actor who changed their name here reach
/// this library at all.
///
/// It decides nothing. No claim moves, no candidate is proposed, and an answer about a work a
/// Video is no longer identified as is dropped: this lane refreshes retained facts and does not
/// identify anything.
/// </remarks>
public sealed class EnrichmentRunner(
    ViewerDbContext database,
    IPrdbWorkDetailClient client,
    IdentificationService identification,
    ActorProfileRetention actors,
    ActorImageRetention actorImages,
    WorkIssueRecorder issues,
    TimeProvider timeProvider) : VideoFileWorkRunner(database, issues, timeProvider)
{
    /// <summary>
    /// How long a retained work's facts stand before they are asked about again. It is long
    /// because a catalogue entry changes rarely and an installation's requests are counted against
    /// a published limit; it is not never, because an Actor gains pictures and a work gains
    /// release names after it is first identified.
    /// </summary>
    private static readonly TimeSpan RefreshHorizon = TimeSpan.FromDays(30);

    private static readonly TimeSpan AvailabilityRetry = TimeSpan.FromMinutes(5);

    protected override BackgroundWorkCategory Category => BackgroundWorkCategory.Enrichment;

    protected override string Phase => BackgroundWorkPhases.Enriching;

    protected override int BatchSize => 25;

    /// <summary>
    /// The Available files of Videos whose Established Work has never been asked about in its own
    /// right, or was asked about long enough ago to ask again — and the files of Videos crediting
    /// an Actor this installation still has no profile for.
    /// </summary>
    /// <remarks>
    /// The second half is what keeps the lane running while profiles are still arriving. Several
    /// files of one Video are one question, which the batch deduplicates, and an Actor prdb had
    /// nothing to say about is answered rather than outstanding.
    /// </remarks>
    protected override IQueryable<VideoFileRow> Outstanding(Guid libraryDirectoryId)
    {
        var horizon = Now() - RefreshHorizon;
        var profileHorizon = Now() - ActorProfileRetention.RefreshHorizon;

        return Database.VideoFiles.Where(file =>
            file.LibraryDirectoryId == libraryDirectoryId &&
            file.Availability == VideoFileAvailability.Available &&
            (Database.VideoMetadata.Any(metadata =>
                metadata.VideoId == file.VideoId &&
                (metadata.EnrichedAt == null || metadata.EnrichedAt < horizon)) ||
             Database.VideoActors.Any(credit =>
                credit.VideoId == file.VideoId &&
                credit.PrdbActorId != null &&
                (!Database.Actors.Any(actor =>
                     actor.PrdbActorId == credit.PrdbActorId &&
                     actor.ProfileState != ActorProfileState.Pending &&
                     actor.FetchedAt != null &&
                     actor.FetchedAt >= profileHorizon) ||
                 Database.Actors.Any(actor =>
                     actor.PrdbActorId == credit.PrdbActorId &&
                     actor.Images.Any(image => image.State == ActorImageState.Pending))))));
    }

    /// <summary>
    /// Nothing. What is outstanding is decided by the horizon rather than by a mark this lane
    /// clears, so a new run has nothing to retry and an old answer ages into being outstanding by
    /// itself.
    /// </summary>
    protected override Task RetryEarlierFailuresAsync(
        Guid libraryDirectoryId,
        CancellationToken cancellationToken) => Task.CompletedTask;

    protected override async Task AdvanceAsync(
        BackgroundWorkRow work,
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var configuration = await Database.InstallationConfigurations
            .AsTracking()
            .SingleAsync(cancellationToken);

        if (string.IsNullOrEmpty(configuration.ActivePrdbCredential))
        {
            // No Work Issue of its own. Identification raises the Configuration blocker for the
            // same missing credential and asks for the same action, and calling an Administrator
            // twice to one repair is noise. The lane waits with its condition, which is what a
            // Waiting run is required to carry.
            await HoldAsync(
                work,
                "A verified prdb API key is required before established works can be enriched.",
                cancellationToken);
            return;
        }

        var wanted = await WantedAsync(files, cancellationToken);

        if (wanted.Count == 0)
        {
            // These files are outstanding for their Actors rather than for their work, so the
            // slice is one round of profiles and nothing is counted against the files: they stay
            // outstanding until the Actors they credit are answered for.
            await RetainProfilesAsync(work, configuration.ActivePrdbCredential, cancellationToken);
            return;
        }

        var result = await client.FetchAsync(
            configuration.ActivePrdbCredential,
            wanted.Keys.Take(client.BatchLimit).ToArray(),
            cancellationToken);

        switch (result.Status)
        {
            case WorkDetailFetchStatus.Rejected:
                configuration.PrdbConnectionStatus = PrdbConnectionStatus.Rejected;
                configuration.LastConnectionIssue = PrdbConnectionIssue.ExternalAuthority;
                configuration.LastConnectionAttemptAt = Now();
                await HoldAsync(
                    work,
                    "prdb refused the installation credential.",
                    cancellationToken);
                return;

            case WorkDetailFetchStatus.Unavailable:
                if (configuration.PrdbConnectionStatus == PrdbConnectionStatus.Verified)
                {
                    configuration.PrdbConnectionStatus = PrdbConnectionStatus.Degraded;
                    configuration.LastConnectionIssue = PrdbConnectionIssue.ExternalAvailability;
                    configuration.LastConnectionAttemptAt = Now();
                }

                await WaitAsync(
                    work,
                    "prdb is temporarily unavailable.",
                    AvailabilityRetry,
                    cancellationToken);
                return;
        }

        if (configuration.PrdbConnectionStatus is PrdbConnectionStatus.Degraded)
        {
            configuration.PrdbConnectionStatus = PrdbConnectionStatus.Verified;
            configuration.LastConnectionIssue = null;
            configuration.LastConnectionVerifiedAt = Now();
        }

        await Database.SaveChangesAsync(cancellationToken);

        foreach (var fresh in result.Works)
        {
            if (!wanted.TryGetValue(fresh.PrdbVideoId, out var videos))
            {
                continue;
            }

            foreach (var videoId in videos)
            {
                await identification.RefreshRetainedWorkAsync(videoId, fresh, cancellationToken);
            }
        }

        // A work prdb no longer answers for is asked about again at the next horizon, not on the
        // next slice. Without this the lane would offer the same unanswered work forever and
        // never reach the next one.
        var asked = wanted.Values.SelectMany(videos => videos).Distinct().ToArray();
        var now = Now();
        await Database.VideoMetadata
            .Where(metadata => asked.Contains(metadata.VideoId) &&
                               (metadata.EnrichedAt == null || metadata.EnrichedAt < now))
            .ExecuteUpdateAsync(
                update => update.SetProperty(metadata => metadata.EnrichedAt, now),
                cancellationToken);

        work.CompletedItemCount += files.Count;
        await RetainProfilesAsync(work, configuration.ActivePrdbCredential, cancellationToken);
    }

    /// <summary>
    /// One bounded round of Actor Profiles. The credits this lane has just written are what an
    /// Actor is created from, so the two belong to one slice: an installation that has learned who
    /// is in a Video learns who they are in the same pass.
    /// </summary>
    /// <remarks>
    /// It raises no Work Issue, for the reason <see cref="ActorProfileRetention"/> gives: a
    /// profile that has not arrived is a paragraph a page does not print. It does stop the lane,
    /// which is a different thing — a lane whose remaining work is Actors prdb is not answering
    /// about would otherwise ask again on the very next slice, for as long as the outage lasts,
    /// because those Actors are exactly what keeps its files outstanding.
    /// </remarks>
    private async Task RetainProfilesAsync(
        BackgroundWorkRow work,
        string credential,
        CancellationToken cancellationToken)
    {
        await actors.EnsureActorsAsync(cancellationToken);
        var status = await actors.RetainAsync(credential, cancellationToken);

        switch (status)
        {
            case ActorProfileFetchStatus.Rejected:
                await HoldAsync(
                    work,
                    "prdb refused the installation credential.",
                    cancellationToken);
                return;

            case ActorProfileFetchStatus.Unavailable:
                await WaitAsync(
                    work,
                    "prdb is temporarily unavailable.",
                    AvailabilityRetry,
                    cancellationToken);
                return;
        }

        // The pictures follow the profiles that named them, over the credential-free transport.
        // A picture that does not arrive is recorded as unavailable and tried again at the next
        // refresh, so an outage costs a gallery for a while rather than for good.
        await actorImages.RetainAsync(cancellationToken);
    }

    /// <summary>
    /// The works this slice asks about, and which Videos each one answers for. Two Videos may hold
    /// the same work — a copy in another Library Directory that was never merged — and one request
    /// answers for both.
    /// </summary>
    private async Task<Dictionary<string, List<Guid>>> WantedAsync(
        IReadOnlyList<VideoFileRow> files,
        CancellationToken cancellationToken)
    {
        var videoIds = files.Select(file => file.VideoId).Distinct().ToArray();
        var metadata = await Database.VideoMetadata
            .AsNoTracking()
            .Where(row => videoIds.Contains(row.VideoId))
            .Select(row => new { row.VideoId, row.PrdbVideoId })
            .ToListAsync(cancellationToken);
        var wanted = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in metadata)
        {
            if (!wanted.TryGetValue(row.PrdbVideoId, out var videos))
            {
                videos = [];
                wanted[row.PrdbVideoId] = videos;
            }

            videos.Add(row.VideoId);
        }

        return wanted;
    }
}
