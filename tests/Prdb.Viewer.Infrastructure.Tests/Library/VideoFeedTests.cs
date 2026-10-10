using System.Net;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.FakeCatalogue;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class VideoFeedTests
{
    private const string WorkId = "6f1a2c34-0000-4000-8000-000000000001";
    private const string OtherId = "6f1a2c34-0000-4000-8000-000000000099";
    private const string Boundary = "2026-10-01T00:00:00.1234567Z";

    [Fact]
    public async Task The_server_boundary_precedes_local_repair_and_concurrent_changes_are_replayed()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        prdb.VideoFeedReplies.Enqueue(Page(Boundary));
        await SliceAsync(store); // server boundary, not a remote catalogue baseline
        Assert.Contains("9999-12-31", Uri.UnescapeDataString(prdb.Requested.Last().Query));
        await SliceAsync(store); // missing detail leaves the last known content intact
        Assert.Single(prdb.VideoBatchRequests);
        prdb.VideoFeedReplies.Enqueue(Page("2026-10-01T00:00:01.1234567Z", WorkId, false, "Corrected during repair"));
        await SliceAsync(store);
        await using var scope = store.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Equal("Corrected during repair", (await db.VideoMetadata.SingleAsync(TestContext.Current.CancellationToken)).Title);
        var state = await db.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(state.BaselineComplete);
        Assert.Contains(".1234567", state.Since);
        Assert.Equal(Guid.Parse(WorkId), state.SinceId);
        Assert.DoesNotContain(prdb.Requested, uri => Uri.UnescapeDataString(uri.Query).Contains("0001-01-01"));
    }

    [Fact]
    public async Task Irrelevant_pages_advance_without_persisting_remote_Videos_and_yield_at_the_budget()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        await SliceAsync(store);
        await SliceAsync(store);
        for (var i = 1; i <= 4; i++)
        {
            var page = System.Text.Json.Nodes.JsonNode.Parse(Page($"2026-10-01T00:00:0{i}Z", OtherId, true, "Unrelated"))!;
            var template = page["items"]![0]!.DeepClone();
            var items = page["items"]!.AsArray(); items.Clear();
            for (var row = 0; row < 999; row++)
            {
                var item = template.DeepClone();
                item["video"]!["id"] = $"00000000-0000-4000-8000-{row:x12}";
                items.Add(item);
            }
            items.Add(template);
            prdb.VideoFeedReplies.Enqueue(page.ToJsonString());
            await SliceAsync(store);
        }
        var requests = prdb.RequestCount;
        Assert.False(await SliceAsync(store));
        Assert.Equal(requests, prdb.RequestCount);
        await using var scope = store.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Single(await db.Videos.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await db.ProposedWorks.ToListAsync(TestContext.Current.CancellationToken));
        var state = await db.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Guid.Parse(OtherId), state.SinceId);
        Assert.True(state.CatchUpPending);
        Assert.Contains("budget", state.Phase);
    }

    [Fact]
    public async Task A_new_local_ID_behind_the_cursor_receives_a_bounded_detail_read()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        await SliceAsync(store); await SliceAsync(store); await SliceAsync(store);
        var requests = prdb.VideoBatchRequests.Count;
        await HeldAsync(store, OtherId);
        await SliceAsync(store);
        Assert.Equal(requests + 1, prdb.VideoBatchRequests.Count);
        Assert.Contains(OtherId, prdb.VideoBatchRequests.Last().ToJsonString());
    }

    [Fact]
    public async Task Tombstones_are_separate_observations_and_never_replace_last_known_content()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        await SliceAsync(store); await SliceAsync(store);
        prdb.VideoFeedReplies.Enqueue(JsonSerializer.Serialize(new
        {
            hasMore = false,
            serverTimeUtc = "2026-10-02T00:00:00Z",
            pageSize = 1000,
            nextCursor = new { updatedAtUtc = "2026-10-02T00:00:00Z", id = WorkId },
            items = new[] { new { eventType = "deleted", video = new { id = WorkId, isDeleted = true,
                updatedAtUtc = "2026-10-02T00:00:00Z", deletedAtUtc = "2026-10-02T00:00:00Z", mergedIntoId = OtherId } } },
        }));
        await SliceAsync(store);
        await using var scope = store.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var metadata = await db.VideoMetadata.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Retained Work", metadata.Title);
        Assert.Equal(RemoteCatalogueState.Merged, metadata.RemoteState);
        Assert.Equal(OtherId, metadata.RemoteMergedIntoId);
        Assert.Equal(WorkId, (await db.IdentificationClaims.SingleAsync(TestContext.Current.CancellationToken)).TargetKey);
        Assert.Single(await db.Videos.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Paused_work_makes_no_request_and_an_authority_change_repairs_without_losing_facts()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        await SliceAsync(store);
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await db.InstallationConfigurations.ExecuteUpdateAsync(u => u.SetProperty(c => c.BackgroundWorkPaused, true), TestContext.Current.CancellationToken);
        }
        var requests = prdb.RequestCount;
        Assert.False(await SliceAsync(store)); Assert.Equal(requests, prdb.RequestCount);
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await db.InstallationConfigurations.ExecuteUpdateAsync(u => u.SetProperty(c => c.BackgroundWorkPaused, false), TestContext.Current.CancellationToken);
        }
        await LibraryPipeline.SetCredentialAsync(store, "replacement");
        await SliceAsync(store);
        await using var final = store.Scope();
        var database = final.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Equal(2, (await database.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken)).RepairGeneration);
        Assert.Equal("Retained Work", (await database.VideoMetadata.SingleAsync(TestContext.Current.CancellationToken)).Title);
    }

    [Fact]
    public async Task A_rate_limit_preserves_the_boundary_and_honours_retry_guidance_once()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        await SliceAsync(store); await SliceAsync(store);
        prdb.Failure = HttpStatusCode.TooManyRequests; prdb.RetryAfter = TimeSpan.FromHours(2);
        await SliceAsync(store);
        var count = prdb.RequestCount;
        Assert.False(await SliceAsync(store)); Assert.Equal(count, prdb.RequestCount);
        await using var scope = store.Scope();
        var state = await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(state.SinceId);
        Assert.True(state.NextAttemptAt > DateTime.UtcNow.AddMinutes(119));
    }

    [Fact]
    public async Task A_lost_cursor_repairs_every_local_ID_even_after_previous_generations()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        await using (var scope = store.Scope())
            await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().VideoMetadata.ExecuteUpdateAsync(
                update => update.SetProperty(row => row.CatalogueRepairGeneration, 7), TestContext.Current.CancellationToken);
        await SliceAsync(store); await SliceAsync(store);
        Assert.Single(prdb.VideoBatchRequests);
        await using var final = store.Scope();
        Assert.Equal(8, (await final.ServiceProvider.GetRequiredService<ViewerDbContext>().CatalogueFeedStates
            .SingleAsync(TestContext.Current.CancellationToken)).RepairGeneration);
    }

    [Fact]
    public async Task Replay_and_older_updates_cannot_resurrect_a_deleted_work_but_a_newer_update_can()
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId);
        await SliceAsync(store); await SliceAsync(store);
        var deleted = JsonSerializer.Serialize(new
        {
            hasMore = true,
            serverTimeUtc = "2026-10-03T00:00:00Z",
            pageSize = 1000,
            nextCursor = new { updatedAtUtc = "2026-10-02T00:00:00Z", id = WorkId },
            items = new[] { new { eventType = "deleted", video = new { id = WorkId, isDeleted = true,
                updatedAtUtc = "2026-10-02T00:00:00Z", deletedAtUtc = "2026-10-02T00:00:00Z" } } }
        });
        prdb.VideoFeedReplies.Enqueue(deleted);
        await SliceAsync(store);
        // The page's own cursor advances, but it contains an old current-state observation.
        var old = System.Text.Json.Nodes.JsonNode.Parse(Page("2026-10-03T00:00:00Z", WorkId, true, "Old title"))!;
        old["items"]![0]!["video"]!["updatedAtUtc"] = "2026-10-01T00:00:00Z";
        prdb.VideoFeedReplies.Enqueue(old.ToJsonString());
        await SliceAsync(store);
        await using (var scope = store.Scope())
        {
            var metadata = await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().VideoMetadata.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(RemoteCatalogueState.Deleted, metadata.RemoteState);
            Assert.Equal("Retained Work", metadata.Title);
            // A detail answer has no feed revision and cannot undo a tombstone.
            await scope.ServiceProvider.GetRequiredService<IdentificationService>().RefreshRetainedWorkAsync(
                metadata.VideoId, new RemoteWork(WorkId, "Stale batch title", null, [], null, null, null), TestContext.Current.CancellationToken);
        }
        prdb.VideoFeedReplies.Enqueue(Page("2026-10-04T00:00:00Z", WorkId, false, "Restored title"));
        await SliceAsync(store);
        await using var final = store.Scope();
        var restored = await final.ServiceProvider.GetRequiredService<ViewerDbContext>().VideoMetadata.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(RemoteCatalogueState.Active, restored.RemoteState);
        Assert.Equal("Restored title", restored.Title);
        Assert.Null(restored.RemoteDeletedAt);
        Assert.Single(await final.ServiceProvider.GetRequiredService<ViewerDbContext>().IdentificationClaims.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_interruption_at_the_cursor_commit_rolls_back_content_and_replay_is_idempotent()
    {
        var prdb = new FakePrdb(); var failure = new CursorWriteFailure();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb, interceptor: failure);
        await HeldAsync(store, WorkId); await SliceAsync(store); await SliceAsync(store);
        var page = Page("2026-10-02T00:00:00Z", WorkId, false, "Corrected title");
        prdb.VideoFeedReplies.Enqueue(page); failure.Armed = true;
        var interruption = await Assert.ThrowsAsync<DbUpdateException>(() => SliceAsync(store));
        Assert.IsType<IOException>(interruption.InnerException);
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.Equal("Retained Work", (await db.VideoMetadata.SingleAsync(TestContext.Current.CancellationToken)).Title);
            Assert.Null((await db.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken)).SinceId);
        }
        prdb.VideoFeedReplies.Enqueue(page); await SliceAsync(store);
        await using var final = store.Scope();
        var database = final.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Equal("Corrected title", (await database.VideoMetadata.SingleAsync(TestContext.Current.CancellationToken)).Title);
        Assert.Equal(Guid.Parse(WorkId), (await database.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken)).SinceId);
        Assert.Single(await database.IdentificationClaims.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Local_detail_repair_honours_quota_reset_and_RetryAfter_without_immediate_requests(bool exhaustedSuccess)
    {
        var prdb = new FakePrdb();
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await HeldAsync(store, WorkId); await SliceAsync(store);
        if (exhaustedSuccess)
        {
            prdb.ResponseHeaders["X-RateLimit-Limit-Hour"] = "1000";
            prdb.ResponseHeaders["X-RateLimit-Remaining-Hour"] = "0";
            prdb.ResponseHeaders["X-RateLimit-Reset-Hour"] = "7200";
        }
        else { prdb.Failure = HttpStatusCode.TooManyRequests; prdb.RetryAfter = TimeSpan.FromHours(2); }
        await SliceAsync(store);
        var requests = prdb.RequestCount;
        Assert.False(await SliceAsync(store)); Assert.Equal(requests, prdb.RequestCount);
        await using var final = store.Scope();
        var db = final.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.True((await db.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken)).NextAttemptAt > DateTime.UtcNow.AddMinutes(119));
        Assert.Equal("Retained Work", (await db.VideoMetadata.SingleAsync(TestContext.Current.CancellationToken)).Title);
    }

    private sealed class CursorWriteFailure : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("UPDATE \"CatalogueFeedStates\"", StringComparison.Ordinal))
            {
                Armed = false;
                throw new IOException("Simulated process interruption at cursor commit.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private static async Task HeldAsync(TestDatabase store, string id)
    {
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await using var scope = store.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var video = new VideoRow { Id = Guid.NewGuid(), DiscoveryDate = DateTime.UtcNow };
        db.Videos.Add(video);
        db.IdentificationClaims.Add(new IdentificationClaimRow
        {
            Id = Guid.NewGuid(),
            VideoId = video.Id,
            TargetKey = id,
            TargetTitle = "Retained Work",
            Dimension = IdentificationDimension.WorkIdentification,
            Status = IdentificationClaimStatus.Current,
            Source = IdentificationSource.AdministratorDecision,
            IsAdministrativeOverride = true,
            EvidenceClass = IdentificationEvidenceClass.Conclusive,
            EstablishedAt = DateTime.UtcNow
        });
        db.VideoMetadata.Add(new VideoMetadataRow { VideoId = video.Id, PrdbVideoId = id, Title = "Retained Work", FetchedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<bool> SliceAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        return await scope.ServiceProvider.GetRequiredService<VideoCatalogueRefresh>().RunNextSliceAsync(TestContext.Current.CancellationToken);
    }

    private static string Page(string at, string? id = null, bool more = false, string? title = null) => JsonSerializer.Serialize(new
    {
        hasMore = more,
        serverTimeUtc = at,
        pageSize = 1000,
        nextCursor = id is null ? null : new { updatedAtUtc = at, id },
        items = id is null ? Array.Empty<object>() : [new { eventType = "updated", video = new { id, updatedAtUtc = at, isDeleted = false, title } }],
    });
}
