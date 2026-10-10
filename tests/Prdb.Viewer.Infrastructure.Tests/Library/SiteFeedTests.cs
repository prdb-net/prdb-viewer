using System.Net;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.FakeCatalogue;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class SiteFeedTests
{
    private const string SiteId = "6f1a2c34-0000-4000-8000-000000000002";
    private const string At = "2026-10-01T12:34:56.1234567Z";

    [Fact]
    public async Task Rate_limit_guidance_defers_the_next_attempt_without_sleeping_or_retries()
    {
        var prdb = new FakePrdb { Failure = HttpStatusCode.TooManyRequests, RetryAfter = TimeSpan.FromMinutes(37) };
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await using var scope = store.Scope();
        var directory = scope.ServiceProvider.GetRequiredService<SiteDirectory>();
        var first = await directory.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TimeSpan.FromMinutes(37), first.RetryAfter);
        var second = await directory.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SiteDirectoryRefreshVerdict.Pending, second.Verdict);
        Assert.Equal(1, prdb.RequestCount);
    }

    [Fact]
    public async Task A_malformed_success_and_caller_cancellation_cannot_publish_partial_state()
    {
        var prdb = new FakePrdb { RawBody = "{}" };
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await using var scope = store.Scope();
        var directory = scope.ServiceProvider.GetRequiredService<SiteDirectory>();
        Assert.Equal(SiteDirectoryRefreshVerdict.Unreachable,
            (await directory.RefreshAsync(TestContext.Current.CancellationToken)).Verdict);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => directory.RefreshAsync(new CancellationToken(true)));
        var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.False((await db.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken)).BaselineComplete);
        Assert.Empty(await db.SiteDirectoryEntries.ToListAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task More_than_twenty_pages_resume_between_scopes_and_activate_only_when_complete()
    {
        var prdb = new FakePrdb();
        for (var page = 1; page <= 21; page++) prdb.SitePages[page] = 1000;
        prdb.SitePages[22] = 1;
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            db.SiteDirectoryEntries.Add(new SiteDirectoryEntryRow { SiteKey = "old", Title = "Old Complete Directory" });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        for (var page = 1; page <= 22; page++)
        {
            await using var scope = store.Scope();
            var result = await scope.ServiceProvider.GetRequiredService<SiteDirectory>().RefreshAsync(TestContext.Current.CancellationToken);
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            if (page < 22)
            {
                Assert.Equal(SiteDirectoryRefreshVerdict.Pending, result.Verdict);
                Assert.Equal("old", (await db.SiteDirectoryEntries.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken)).SiteKey);
                Assert.Null((await db.InstallationConfigurations.SingleAsync(TestContext.Current.CancellationToken)).SiteDirectoryFetchedAt);
            }
            else
            {
                Assert.Equal(SiteDirectoryRefreshVerdict.Refreshed, result.Verdict);
                Assert.Equal(21001, await db.SiteDirectoryEntries.CountAsync(TestContext.Current.CancellationToken));
                Assert.Empty(await db.SiteDirectoryStaging.ToListAsync(TestContext.Current.CancellationToken));
            }
        }
        Assert.Equal(22, prdb.RequestCount);
        Assert.DoesNotContain("SinceId", prdb.Requested[0].Query);
        Assert.Contains("0001-01-01", Uri.UnescapeDataString(prdb.Requested[0].Query));
    }

    [Fact]
    public async Task Final_empty_discarded_and_future_pages_obey_the_exact_server_cursor()
    {
        var prdb = new FakePrdb();
        prdb.SiteFeedReplies.Enqueue(Page(false, At, SiteId, new { id = SiteId, isDeleted = false, updatedAtUtc = At, title = (string?)null }));
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await RefreshAsync(store);
        await using (var scope = store.Scope())
        {
            var state = await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Guid.Parse(SiteId), state.SinceId);
            Assert.Contains(".1234567", state.Since);
        }
        // An older settled bound never makes an imported future cursor move backwards.
        prdb.SiteFeedReplies.Enqueue(Page(false, "2026-10-01T12:30:00Z", null));
        await RefreshAsync(store);
        await using (var scope = store.Scope())
        {
            var state = await scope.ServiceProvider.GetRequiredService<ViewerDbContext>().CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken);
            Assert.Equal(Guid.Parse(SiteId), state.SinceId);
            Assert.Contains(".1234567", state.Since);
        }
        prdb.SiteFeedReplies.Enqueue(Page(false, "2026-10-01T13:00:00.7654321Z", null));
        await RefreshAsync(store);
        await using var final = store.Scope();
        var cursor = await final.ServiceProvider.GetRequiredService<ViewerDbContext>().CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(cursor.SinceId);
        Assert.Contains(".7654321", cursor.Since);
    }

    [Fact]
    public async Task A_tombstone_excludes_established_claim_fallback_without_deleting_provenance()
    {
        var prdb = new FakePrdb();
        prdb.SiteFeedReplies.Enqueue(Page(false, At, SiteId, new { id = SiteId, isDeleted = false, updatedAtUtc = At, title = "Known Site" }));
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await RefreshAsync(store);
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var video = new VideoRow { Id = Guid.NewGuid(), DiscoveryDate = DateTime.UtcNow };
            db.Videos.Add(video);
            db.IdentificationClaims.Add(new IdentificationClaimRow
            {
                Id = Guid.NewGuid(),
                VideoId = video.Id,
                Dimension = IdentificationDimension.SiteRecognition,
                Status = IdentificationClaimStatus.Current,
                TargetKey = SiteId,
                TargetTitle = "Known Site",
                Source = IdentificationSource.AdministratorDecision,
                IsAdministrativeOverride = true,
                EvidenceClass = IdentificationEvidenceClass.Conclusive,
                EstablishedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        prdb.SiteFeedReplies.Enqueue(Page(false, "2026-10-02T00:00:00Z", SiteId,
            new { id = SiteId, isDeleted = true, deletedAtUtc = "2026-10-02T00:00:00Z", updatedAtUtc = "2026-10-02T00:00:00Z", title = (string?)null }));
        await RefreshAsync(store);
        await using var final = store.Scope();
        var directory = await final.ServiceProvider.GetRequiredService<SiteDirectory>().ReadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(directory.Recognise("Known Site/file.mp4").Matches);
        Assert.True((await final.ServiceProvider.GetRequiredService<ViewerDbContext>().IdentificationClaims.SingleAsync(TestContext.Current.CancellationToken)).IsAdministrativeOverride);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, SiteDirectoryRefreshVerdict.Refused)]
    [InlineData(HttpStatusCode.TooManyRequests, SiteDirectoryRefreshVerdict.Unreachable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, SiteDirectoryRefreshVerdict.Unreachable)]
    public async Task A_failed_page_preserves_cache_and_cursor_and_makes_one_attempt(HttpStatusCode failure, SiteDirectoryRefreshVerdict verdict)
    {
        var prdb = new FakePrdb { Failure = failure };
        await using var store = await TestDatabase.CreateAsync(prdb: prdb);
        await LibraryPipeline.SetCredentialAsync(store, "fixture");
        await using var scope = store.Scope();
        var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        db.SiteDirectoryEntries.Add(new SiteDirectoryEntryRow { SiteKey = "old", Title = "Old Directory" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var result = await scope.ServiceProvider.GetRequiredService<SiteDirectory>().RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(verdict, result.Verdict);
        Assert.Equal(1, prdb.RequestCount);
        Assert.Equal("Old Directory", (await db.SiteDirectoryEntries.SingleAsync(TestContext.Current.CancellationToken)).Title);
        Assert.Equal(CatalogueCursor.Baseline.Since, (await db.CatalogueFeedStates.SingleAsync(TestContext.Current.CancellationToken)).Since);
    }

    private static async Task RefreshAsync(TestDatabase store)
    {
        await using var scope = store.Scope();
        await scope.ServiceProvider.GetRequiredService<SiteDirectory>().RefreshAsync(TestContext.Current.CancellationToken);
    }

    private static string Page(bool more, string at, string? id, params object[] sites) =>
        JsonSerializer.Serialize(new
        {
            hasMore = more,
            serverTimeUtc = at,
            pageSize = 1000,
            nextCursor = id is null ? null : new { updatedAtUtc = at, id },
            items = sites.Select(site => new { eventType = JsonSerializer.SerializeToElement(site).GetProperty("isDeleted").GetBoolean() ? "deleted" : "updated", site }),
        });
}
