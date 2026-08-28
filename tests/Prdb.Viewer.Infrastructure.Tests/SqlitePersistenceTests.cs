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

    private static async Task<object?> ScalarAsync(ViewerDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return value is string text ? text : Convert.ToInt64(value);
    }
}
