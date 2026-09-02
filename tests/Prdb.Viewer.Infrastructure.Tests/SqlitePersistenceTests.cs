using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;
using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests;

public sealed class SqlitePersistenceTests
{
    [Fact]
    public async Task Startup_creates_and_migrates_the_database()
    {
        await using var database = await TestDatabase.CreateAsync();

        Assert.True(File.Exists(database.Location.FilePath));

        await using var scope = database.Scope();
        var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Contains(
            "20260827000000_Initial",
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));
        Assert.Contains(
            "20260827204731_AddPersonalState",
            await context.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken));

        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(database.Location.FilePath));
        }
    }

    [Fact]
    public async Task Every_context_connection_uses_the_required_pragmas()
    {
        await using var database = await TestDatabase.CreateAsync();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = database.Scope();
            var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();

            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

            Assert.Equal("wal", await ScalarAsync(context, "PRAGMA journal_mode;"));
            Assert.Equal(2L, await ScalarAsync(context, "PRAGMA synchronous;"));
            Assert.Equal(5000L, await ScalarAsync(context, "PRAGMA busy_timeout;"));
            Assert.Equal(1L, await ScalarAsync(context, "PRAGMA foreign_keys;"));

            await context.Database.CloseConnectionAsync();
        }
    }

    [Fact]
    public async Task Video_delivery_migration_backfills_existing_video_files_safely()
    {
        await using var database = await TestDatabase.CreateAsync(
            targetMigration: "20260827193803_AddLibraryProcessing");
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var baselineFileId = Guid.CreateVersion7();
        var unknownFileId = Guid.CreateVersion7();
        var observedScanId = Guid.CreateVersion7();
        var timestamp = DateTime.SpecifyKind(new DateTime(2026, 8, 27, 12, 0, 0), DateTimeKind.Utc);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO library_directory
                    (Id, Name, ContainerPath, State, Health, ConfigurationGeneration,
                     CreatedAt, ActivatedAt, RemovedAt, InitialProcessingStartedAt)
                VALUES
                    ({directoryId}, {"Main"}, {"/libraries/main"}, {"Active"}, {"Healthy"},
                     {1}, {timestamp}, {timestamp}, NULL, {timestamp});
                INSERT INTO video (Id, DiscoveryDate) VALUES ({videoId}, {timestamp});
                INSERT INTO video_file
                    (Id, VideoId, LibraryDirectoryId, RelativePath, Size, LastWriteTimeUtc,
                     Sha256, ContainerFormat, VideoCodec, AudioCodec, DurationMilliseconds,
                     Width, Height, Availability, LastObservedScanId,
                     ConsecutiveCompleteAbsences, InspectedAt)
                VALUES
                    ({baselineFileId}, {videoId}, {directoryId}, {"first.mp4"}, {10L}, {timestamp},
                     {new string('A', 64)}, {"mp4"}, {"h264"}, {"aac"}, {1000L},
                     {640}, {360}, {"Available"}, {observedScanId}, {0}, {timestamp}),
                    ({unknownFileId}, {videoId}, {directoryId}, {"second.mkv"}, {20L}, {timestamp},
                     {new string('B', 64)}, {"matroska"}, {"h264"}, {"aac"}, {2000L},
                     {1280}, {720}, {"Available"}, {observedScanId}, {0}, {timestamp});
                """, TestContext.Current.CancellationToken);
        }

        await database.MigrateAsync();

        await using var verificationScope = database.Scope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var files = await verification.VideoFiles.OrderBy(file => file.RelativePath)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, files.Select(file => file.PublicDeliveryId).Distinct().Count());
        Assert.DoesNotContain(files, file => file.PublicDeliveryId == Guid.Empty);
        Assert.Equal(DirectPlayClassification.BaselineCandidate, files[0].DirectPlayClassification);
        Assert.Equal(DirectPlayClassification.Undetermined, files[1].DirectPlayClassification);
        Assert.Equal(
            timestamp,
            (await verification.InstallationConfigurations.SingleAsync(
                TestContext.Current.CancellationToken)).FirstPlayableVideoReachedAt);
    }

    [Fact]
    public async Task The_client_playability_migration_leaves_every_video_to_be_projected_again()
    {
        await using var database = await TestDatabase.CreateAsync(
            targetMigration: "20260828190317_AddSiteRecognition");
        var directoryId = Guid.CreateVersion7();
        var videoId = Guid.CreateVersion7();
        var fileId = Guid.CreateVersion7();
        var timestamp = DateTime.SpecifyKind(new DateTime(2026, 8, 28, 12, 0, 0), DateTimeKind.Utc);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO library_directory
                    (Id, Name, ContainerPath, State, Health, ConfigurationGeneration,
                     CreatedAt, ActivatedAt, RemovedAt, InitialProcessingStartedAt)
                VALUES
                    ({directoryId}, {"Main"}, {"/libraries/main"}, {"Active"}, {"Healthy"},
                     {1}, {timestamp}, {timestamp}, NULL, {timestamp});
                INSERT INTO video
                    (Id, DiscoveryDate, DisplayLabel, SearchText, Readiness, Availability,
                     HasEstablishedWork, ReviewNeeded, CaseVersion, ProjectedAt)
                VALUES
                    ({videoId}, {timestamp}, {"first"}, {"first"}, {"ReadyForDirectPlay"},
                     {"Available"}, {false}, {false}, {0}, {timestamp});
                INSERT INTO video_file
                    (Id, VideoId, LibraryDirectoryId, RelativePath, Size, LastWriteTimeUtc,
                     Sha256, PublicDeliveryId, ContainerFormat, VideoCodec, AudioCodec,
                     DurationMilliseconds, Width, Height, Availability,
                     DirectPlayClassification, LastObservedScanId, ConsecutiveCompleteAbsences,
                     InspectedAt, HashState, PreviewState)
                VALUES
                    ({fileId}, {videoId}, {directoryId}, {"first.mp4"}, {10L}, {timestamp},
                     {new string('A', 64)}, {Guid.NewGuid()}, {"mov,mp4,m4a,3gp,3g2,mj2"},
                     {"h264"}, {"aac"}, {2000L}, {1920}, {1080}, {"Available"},
                     {"BaselineCandidate"}, {Guid.CreateVersion7()}, {0}, {timestamp},
                     {"Computed"}, {"Generated"});
                """, TestContext.Current.CancellationToken);
        }

        await database.MigrateAsync();

        await using var verificationScope = database.Scope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<ViewerDbContext>();

        // The projected column changed meaning, so every Video must be projected again. Without
        // this the library would carry the new column's default and disappear from ordinary
        // discovery until something else happened to touch it.
        var video = await verification.Videos.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Null(video.ProjectedAt);

        // The Video File keeps the classification it had, so nothing vanishes before the Library
        // Scan the upgrade queues has inspected the facts the new rules need.
        var file = await verification.VideoFiles.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(DirectPlayClassification.BaselineCandidate, file.DirectPlayClassification);
        Assert.Equal(string.Empty, file.ProfileKey);

        // Rebuilding the outstanding projections is what the Host does before it serves anything.
        Assert.True(await verificationScope.ServiceProvider
            .GetRequiredService<VideoProjection>()
            .RefreshOutstandingAsync(cancellationToken: TestContext.Current.CancellationToken));
        var projected = await verification.Videos
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(projected.ProjectedAt);
        Assert.Equal(DirectPlayClassification.BaselineCandidate, projected.BestClassification);
    }

    [Fact]
    public async Task The_diagnostics_migration_drops_work_issues_it_cannot_describe()
    {
        await using var database = await TestDatabase.CreateAsync(
            targetMigration: "20260828112033_AddIdentificationAndPreviews");
        var directoryId = Guid.CreateVersion7();
        var workId = Guid.CreateVersion7();
        var timestamp = DateTime.SpecifyKind(new DateTime(2026, 8, 28, 9, 0, 0), DateTimeKind.Utc);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO library_directory
                    (Id, Name, ContainerPath, State, Health, ConfigurationGeneration,
                     CreatedAt, ActivatedAt, RemovedAt, InitialProcessingStartedAt)
                VALUES
                    ({directoryId}, {"Main"}, {"/libraries/main"}, {"Active"}, {"Healthy"},
                     {1}, {timestamp}, {timestamp}, NULL, {timestamp});
                INSERT INTO background_work
                    (Id, Category, State, LibraryDirectoryId, ConfigurationGeneration,
                     LibraryScanId, PendingDirectoriesJson, CoverageComplete, FollowUpRequested,
                     DiscoveredCandidateCount, CompletedItemCount, IssueCount, NextAttemptAt,
                     WaitingReason, RequestedAt, StartedAt, UpdatedAt, FinishedAt)
                VALUES
                    ({workId}, {"LibraryScan"}, {"CompletedWithIssues"}, {directoryId}, {1},
                     {workId}, {"[]"}, {1}, {0}, {2}, {2}, {2}, NULL, NULL,
                     {timestamp}, {timestamp}, {timestamp}, {timestamp});
                INSERT INTO work_issue
                    (Id, BackgroundWorkId, Severity, Cause, RemediationOwner, AffectedScope,
                     Impact, RequiredAction, CreatedAt, ResolvedAt)
                VALUES
                    ({Guid.CreateVersion7()}, {workId}, {"ScopedIssue"}, {"InvalidContent"},
                     {"Administrator"}, {"broken.mp4"}, {"No video."}, {"Replace it."},
                     {timestamp}, NULL),
                    ({Guid.CreateVersion7()}, {workId}, {"ScopedIssue"}, {"SourceAccess"},
                     {"InstallationOperator"}, {"locked.mp4"}, {"Unreadable."}, {"Fix it."},
                     {timestamp}, NULL);
                """, TestContext.Current.CancellationToken);
        }

        await database.MigrateAsync();

        // The old rows cannot supply a reference, an aggregation key, or a message contract, so
        // they are dropped rather than filled with placeholders; the lanes re-derive whichever
        // obstacles still apply, and the durable work they described is untouched.
        await using var scope2 = database.Scope();
        var verification = scope2.ServiceProvider.GetRequiredService<ViewerDbContext>();
        Assert.Empty(await verification.WorkIssues.ToListAsync(TestContext.Current.CancellationToken));
        var work = await verification.BackgroundWork.SingleAsync(
            TestContext.Current.CancellationToken);
        Assert.Equal(BackgroundWorkState.CompletedWithIssues, work.State);
        Assert.False(work.CancellationRequested);
        Assert.False((await verification.InstallationConfigurations.SingleAsync(
            TestContext.Current.CancellationToken)).BackgroundWorkPaused);
    }

    [Fact]
    public async Task The_periodic_scan_migration_schedules_from_the_last_completed_scan()
    {
        await using var database = await TestDatabase.CreateAsync(
            targetMigration: "20260830163713_AddVideoQuality");
        var scannedId = Guid.CreateVersion7();
        var neverScannedId = Guid.CreateVersion7();
        var removedId = Guid.CreateVersion7();
        var activated = DateTime.SpecifyKind(new DateTime(2026, 8, 24, 12, 0, 0), DateTimeKind.Utc);
        var finished = DateTime.SpecifyKind(new DateTime(2026, 8, 27, 11, 0, 0), DateTimeKind.Utc);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO library_directory
                    (Id, Name, ContainerPath, State, Health, ConfigurationGeneration,
                     CreatedAt, ActivatedAt, RemovedAt, InitialProcessingStartedAt)
                VALUES
                    ({scannedId}, {"Scanned"}, {"/libraries/scanned"}, {"Active"}, {"Healthy"},
                     {1}, {activated}, {activated}, NULL, {activated}),
                    ({neverScannedId}, {"Untouched"}, {"/libraries/untouched"}, {"Active"},
                     {"Healthy"}, {1}, {activated}, {activated}, NULL, NULL),
                    ({removedId}, {"Gone"}, {"/libraries/gone"}, {"Removed"}, {"Healthy"},
                     {2}, {activated}, {activated}, {finished}, {activated});
                INSERT INTO background_work
                    (Id, Category, State, LibraryDirectoryId, ConfigurationGeneration,
                     LibraryScanId, CoverageComplete, FollowUpRequested, CancellationRequested,
                     Trigger, Phase, SkippedItemCount, DiscoveredCandidateCount,
                     CompletedItemCount, IssueCount, RequestedAt, UpdatedAt, FinishedAt)
                VALUES
                    ({Guid.CreateVersion7()}, {"LibraryScan"}, {"Completed"}, {scannedId}, {1},
                     {Guid.CreateVersion7()}, {true}, {false}, {false}, {"Administrator"},
                     {"Settled"}, {0}, {1}, {1}, {0}, {activated}, {finished}, {finished}),
                    ({Guid.CreateVersion7()}, {"Hashing"}, {"Completed"}, {neverScannedId}, {1},
                     NULL, {true}, {false}, {false}, {"FollowUpWork"}, {"Settled"}, {0}, {0}, {0},
                     {0}, {activated}, {activated}, {activated});
                """, TestContext.Current.CancellationToken);
        }

        await database.MigrateAsync();

        await using var verificationScope = database.Scope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var directories = await verification.LibraryDirectories
            .OrderBy(directory => directory.Name)
            .ToListAsync(TestContext.Current.CancellationToken);

        // A Library Directory that was scanned an hour ago waits out the rest of its period, one
        // that has no Scan to count from waits it out from its activation and is therefore already
        // due, and a Removed one is scheduled for nothing at all. Only the Library Scan counts:
        // the derived lane that finished later says nothing about when the tree was last read.
        Assert.Equal("Gone", directories[0].Name);
        Assert.Null(directories[0].NextScanDueAt);
        Assert.Equal("Scanned", directories[1].Name);
        Assert.Equal(finished + LibraryScanSchedule.Interval, directories[1].NextScanDueAt);
        Assert.Equal("Untouched", directories[2].Name);
        Assert.Equal(activated + LibraryScanSchedule.Interval, directories[2].NextScanDueAt);
    }

    [Fact]
    public async Task The_redundant_candidate_migration_closes_only_what_the_library_already_knows()
    {
        await using var database = await TestDatabase.CreateAsync(
            targetMigration: "20260902090936_AddPeriodicLibraryScans");
        var settled = Guid.CreateVersion7();
        var contested = Guid.CreateVersion7();
        var agreeing = Guid.CreateVersion7();
        var disagreeing = Guid.CreateVersion7();
        var confirmed = Guid.CreateVersion7();
        var timestamp = DateTime.SpecifyKind(new DateTime(2026, 9, 2, 12, 0, 0), DateTimeKind.Utc);

        await using (var scope = database.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();

            // Two Videos whose Site is established, each carrying a proposal naming that same
            // Site — the state the earlier releases left behind. One of them also carries a
            // proposal naming a different Site, which is a real decision and stays open.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO video
                    (Id, DiscoveryDate, DisplayLabel, SearchText, Availability, BestClassification,
                     Quality, HasEstablishedWork, ReviewNeeded, CaseVersion, ProjectedAt)
                VALUES
                    ({settled}, {timestamp}, {"settled"}, {"settled"}, {"Available"},
                     {"BaselineCandidate"}, {0}, {true}, {true}, {3}, {timestamp}),
                    ({contested}, {timestamp}, {"contested"}, {"contested"}, {"Available"},
                     {"BaselineCandidate"}, {0}, {true}, {true}, {7}, {timestamp});

                INSERT INTO identification_claim
                    (Id, VideoId, Dimension, Status, TargetKey, TargetTitle, TargetUrl,
                     EvidenceClass, Source, MatchedBy, IsAdministrativeOverride, EstablishedAt,
                     LastConfirmedAt, EndedAt, Note, DecidedByAccountId, SupportingVideoFileId)
                VALUES
                    ({Guid.CreateVersion7()}, {settled}, {"SiteRecognition"}, {"Current"},
                     {"site-a"}, {"Site A"}, NULL, {"Conclusive"}, {"PrdbIdentification"}, NULL,
                     {false}, {timestamp}, {timestamp}, NULL, NULL, NULL, NULL),
                    ({Guid.CreateVersion7()}, {contested}, {"SiteRecognition"}, {"Current"},
                     {"site-a"}, {"Site A"}, NULL, {"Conclusive"}, {"PrdbIdentification"}, NULL,
                     {false}, {timestamp}, {timestamp}, NULL, NULL, NULL, NULL);

                INSERT INTO identification_candidate
                    (Id, VideoId, Dimension, Status, TargetKey, TargetTitle, TargetUrl,
                     EvidenceClass, Reason, Source, MatchedBy, Confidence, EvidenceKey,
                     SupportingVideoFileId, PriorRejectionId, DecidedByAccountId, Note,
                     CreatedAt, ResolvedAt)
                VALUES
                    ({confirmed}, {settled}, {"SiteRecognition"}, {"Pending"}, {"SITE-A"},
                     {"Site A"}, NULL, {"Suggestive"}, {"SuggestiveEvidence"}, {"LocalInference"},
                     NULL, NULL, {"LocalSiteName:site a"}, NULL, NULL, NULL, NULL,
                     {timestamp}, NULL),
                    ({agreeing}, {contested}, {"SiteRecognition"}, {"Pending"}, {"site-a"},
                     {"Site A"}, NULL, {"Suggestive"}, {"SuggestiveEvidence"}, {"LocalInference"},
                     NULL, NULL, {"LocalSiteName:site a"}, NULL, NULL, NULL, NULL,
                     {timestamp}, NULL),
                    ({disagreeing}, {contested}, {"SiteRecognition"}, {"Pending"}, {"site-b"},
                     {"Site B"}, NULL, {"Suggestive"}, {"SuggestiveEvidence"}, {"LocalInference"},
                     NULL, NULL, {"LocalSiteName:site b"}, NULL, NULL, NULL, NULL,
                     {timestamp}, NULL);
                """, TestContext.Current.CancellationToken);
        }

        await database.MigrateAsync();

        await using var verificationScope = database.Scope();
        var verification = verificationScope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var candidates = (await verification.IdentificationCandidates
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToDictionary(candidate => candidate.Id);

        // A proposal naming what its Video already has established is closed as overtaken, not as
        // rejected: the evidence was never wrong, and rejecting it would suppress the same path
        // until something materially stronger appeared.
        Assert.Equal(IdentificationCandidateStatus.Superseded, candidates[confirmed].Status);
        Assert.NotNull(candidates[confirmed].ResolvedAt);

        // The comparison is the one the application makes, so a key that differs only in case is
        // the same key here too.
        Assert.Equal(IdentificationCandidateStatus.Superseded, candidates[agreeing].Status);

        // A proposal naming a different Site is a decision an Administrator still has to make.
        Assert.Equal(IdentificationCandidateStatus.Pending, candidates[disagreeing].Status);
        Assert.Null(candidates[disagreeing].ResolvedAt);

        var videos = (await verification.Videos
                .ToListAsync(TestContext.Current.CancellationToken))
            .ToDictionary(video => video.Id);

        // What the library filters on is derived from the candidates that are still Pending, so it
        // is recomputed rather than left saying what was true before.
        Assert.False(videos[settled].ReviewNeeded);
        Assert.True(videos[contested].ReviewNeeded);

        // Both cases changed, so a screen holding either of them finds it stale and reads the
        // refreshed case instead of deciding against candidates that are gone.
        Assert.Equal(4, videos[settled].CaseVersion);
        Assert.Equal(8, videos[contested].CaseVersion);
    }

    private static async Task<object?> ScalarAsync(ViewerDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return value is string text ? text : Convert.ToInt64(value);
    }
}
