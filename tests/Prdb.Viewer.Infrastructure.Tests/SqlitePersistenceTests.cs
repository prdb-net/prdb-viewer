using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

    private static async Task<object?> ScalarAsync(ViewerDbContext context, string sql)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        var value = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        return value is string text ? text : Convert.ToInt64(value);
    }
}
