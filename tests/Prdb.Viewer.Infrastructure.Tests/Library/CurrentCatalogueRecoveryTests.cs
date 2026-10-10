using System.Text.Json;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;

using Prdb.FakeCatalogue;
using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Core.Configuration;
using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

public sealed class CurrentCatalogueRecoveryTests
{
    [Fact]
    public async Task A_019_library_upgrades_without_changing_identity_and_backfills_Unknown_by_MD5()
    {
        var prdb = new FakePrdb().Recognises("unknown.mp4", "New Work", matchedBy: 5);
        await using var store = await TestDatabase.CreateAsync(prdb: prdb, hasher: new BackfillOnlyHasher(),
            targetMigration: "20260914070014_CountWhatARunEstablished");
        var now = DateTime.UtcNow;
        var directory = new LibraryDirectoryRow
        {
            Id = Guid.NewGuid(),
            Name = "Legacy",
            ContainerPath = store.LibraryMountRoot.Path,
            State = LibraryDirectoryState.Active,
            Health = LibraryDirectoryHealth.Healthy,
            ConfigurationGeneration = 1,
            CreatedAt = now,
            ActivatedAt = now
        };
        var known = new VideoRow { Id = Guid.NewGuid(), DiscoveryDate = now.AddDays(-30) };
        var unknown = new VideoRow { Id = Guid.NewGuid(), DiscoveryDate = now.AddDays(-20) };
        var remoteId = Guid.NewGuid().ToString();
        var knownFile = await FileAsync(directory, known, "onlyfans/known.mp4");
        var unknownFile = await FileAsync(directory, unknown, "unknown.mp4");
        var first = new AccountRow
        {
            Id = Guid.NewGuid(),
            Username = "first",
            NormalizedUsername = "FIRST",
            PasswordHash = new string('h', 84),
            Authority = AccountAuthority.Administrator,
            State = AccountState.Approved,
            RegisteredAt = now
        };
        var second = new AccountRow
        {
            Id = Guid.NewGuid(),
            Username = "second",
            NormalizedUsername = "SECOND",
            PasswordHash = new string('h', 84),
            Authority = AccountAuthority.User,
            State = AccountState.Approved,
            RegisteredAt = now
        };
        var claim = new IdentificationClaimRow
        {
            Id = Guid.NewGuid(),
            VideoId = known.Id,
            Dimension = IdentificationDimension.WorkIdentification,
            Status = IdentificationClaimStatus.Current,
            TargetKey = remoteId,
            TargetTitle = "Retained Work",
            EvidenceClass = IdentificationEvidenceClass.Conclusive,
            Source = IdentificationSource.AdministratorDecision,
            IsAdministrativeOverride = true,
            EstablishedAt = now
        };
        var legacySite = new IdentificationClaimRow
        {
            Id = Guid.NewGuid(),
            VideoId = known.Id,
            Dimension = IdentificationDimension.SiteRecognition,
            Status = IdentificationClaimStatus.Current,
            TargetKey = Guid.NewGuid().ToString(),
            TargetTitle = "OnlyFans",
            TargetUrl = "https://onlyfans.com/alex",
            EvidenceClass = IdentificationEvidenceClass.Conclusive,
            Source = IdentificationSource.LocalInference,
            SupportingVideoFileId = knownFile.Id,
            EstablishedAt = now
        };
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            foreach (var row in new object[] { new InstallationConfigurationRow { ActivePrdbCredential = "fixture", PrdbConnectionStatus = PrdbConnectionStatus.Verified },
                first, second, directory, known, unknown, knownFile, unknownFile, claim, legacySite,
                new VideoMetadataRow { VideoId = known.Id, PrdbVideoId = remoteId, Title = "Retained Work", FetchedAt = now },
                new PersonalVideoStateRow { AccountId = first.Id, VideoId = known.Id, PlaybackProgressMilliseconds = 1200, FavouriteAddedAt = now },
                new PersonalVideoStateRow { AccountId = second.Id, VideoId = known.Id, PlaybackProgressMilliseconds = 3400, PlayCount = 2 } })
                await Insert019RowAsync(db, row);
        }
        await store.MigrateAsync();
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            Assert.All(await db.VideoFiles.ToListAsync(TestContext.Current.CancellationToken), file => Assert.Null(file.Md5));
            Assert.Equal(RemoteCatalogueState.Unknown, (await db.VideoMetadata.SingleAsync(TestContext.Current.CancellationToken)).RemoteState);
            var vocabulary = SiteVocabulary.From([new SiteVocabularyEntry(legacySite.TargetKey, legacySite.TargetTitle, legacySite.TargetUrl)]);
            var service = scope.ServiceProvider.GetRequiredService<IdentificationService>();
            await service.ReviewLegacySiteRecognitionAsync(known.Id, vocabulary, TestContext.Current.CancellationToken);
            await service.ReviewLegacySiteRecognitionAsync(known.Id, vocabulary, TestContext.Current.CancellationToken);
            Assert.Single(await db.IdentificationCandidates.ToListAsync(TestContext.Current.CancellationToken));
            db.BackgroundWork.Add(new BackgroundWorkRow
            {
                Id = Guid.NewGuid(),
                LibraryDirectoryId = directory.Id,
                ConfigurationGeneration = 1,
                Category = BackgroundWorkCategory.Hashing,
                State = BackgroundWorkState.Queued,
                RequestedAt = now,
                UpdatedAt = now
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        for (var i = 0; i < 3; i++)
        {
            await using var scope = store.Scope();
            await scope.ServiceProvider.GetRequiredService<HashingRunner>().RunNextSliceAsync(TestContext.Current.CancellationToken);
        }
        await using (var scope = store.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
            var file = await db.VideoFiles.SingleAsync(row => row.Id == unknownFile.Id, TestContext.Current.CancellationToken);
            Assert.Equal("900150983cd24fb0d6963f7d28e17f72", file.Md5);
            Assert.Null((await db.VideoFiles.SingleAsync(row => row.Id == knownFile.Id, TestContext.Current.CancellationToken)).Md5);
            var result = await scope.ServiceProvider.GetRequiredService<IPrdbIdentificationClient>().IdentifyAsync("fixture",
                [new RemoteIdentificationRequest(file.Id, Path.GetFileName(file.RelativePath), file.Size, file.OsHash, file.PerceptualHash, file.Md5)],
                TestContext.Current.CancellationToken);
            Assert.Equal(RemoteMatchKind.Md5, result.Results.Single().MatchedBy);
            await scope.ServiceProvider.GetRequiredService<IdentificationService>().ApplyRemoteIdentificationAsync(result.Results.Single(), TestContext.Current.CancellationToken);
        }
        for (var i = 0; i < 2; i++)
        {
            await using var scope = store.Scope();
            await scope.ServiceProvider.GetRequiredService<VideoCatalogueRefresh>().RunNextSliceAsync(TestContext.Current.CancellationToken);
        }
        prdb.VideoFeedReplies.Enqueue(JsonSerializer.Serialize(new
        {
            hasMore = false,
            serverTimeUtc = "2026-10-02T00:00:00Z",
            pageSize = 1000,
            nextCursor = new { updatedAtUtc = "2026-10-02T00:00:00Z", id = remoteId },
            items = new[] { new { eventType = "deleted", video = new { id = remoteId, isDeleted = true,
                updatedAtUtc = "2026-10-02T00:00:00Z", deletedAtUtc = "2026-10-02T00:00:00Z", mergedIntoId = Guid.NewGuid().ToString() } } }
        }));
        await using (var scope = store.Scope())
            await scope.ServiceProvider.GetRequiredService<VideoCatalogueRefresh>().RunNextSliceAsync(TestContext.Current.CancellationToken);
        await using var final = store.Scope();
        var database = final.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var kept = await database.IdentificationClaims.SingleAsync(row => row.Id == claim.Id, TestContext.Current.CancellationToken);
        Assert.True(kept.IsAdministrativeOverride); Assert.Equal(remoteId, kept.TargetKey);
        Assert.Equal(known.DiscoveryDate, (await database.Videos.SingleAsync(row => row.Id == known.Id, TestContext.Current.CancellationToken)).DiscoveryDate);
        Assert.Equal(RemoteCatalogueState.Merged, (await database.VideoMetadata.SingleAsync(row => row.VideoId == known.Id, TestContext.Current.CancellationToken)).RemoteState);
        Assert.All(await database.VideoFiles.ToListAsync(TestContext.Current.CancellationToken), file => Assert.Equal(VideoFileAvailability.Available, file.Availability));
        Assert.Equal(new long?[] { 1200, 3400 }, await database.PersonalVideoStates.OrderBy(row => row.PlaybackProgressMilliseconds)
            .Select(row => row.PlaybackProgressMilliseconds).ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Single(prdb.IdentifyRequests);
        Assert.Contains("900150983cd24fb0d6963f7d28e17f72", prdb.IdentifyRequests[0].ToJsonString());
    }

    private static async Task<VideoFileRow> FileAsync(LibraryDirectoryRow directory, VideoRow video, string name)
    {
        var path = Path.Combine(directory.ContainerPath, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "abc", TestContext.Current.CancellationToken);
        return new VideoFileRow
        {
            Id = Guid.NewGuid(),
            VideoId = video.Id,
            LibraryDirectoryId = directory.Id,
            RelativePath = name,
            Size = 3,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
            Sha256 = "legacy-content",
            HashedSha256 = "legacy-content",
            IdentifiedSha256 = "legacy-content",
            OsHash = "0123456789abcdef",
            HashState = VideoFileHashState.Incomplete,
            ContainerFormat = "mp4",
            VideoCodec = "h264",
            AudioCodec = "aac",
            PublicDeliveryId = Guid.NewGuid(),
            Availability = VideoFileAvailability.Available,
            DirectPlayClassification = DirectPlayClassification.BaselineCandidate,
            InspectedAt = DateTime.UtcNow,
            SiteRecognisedPath = name
        };
    }

    // Write exactly the columns of the old schema. Current EF cannot insert a row until additive
    // migrations have run; its provider conversions still define the old stored value formats.
    private static async Task Insert019RowAsync(ViewerDbContext db, object row)
    {
        var entity = db.Model.FindEntityType(row.GetType())!;
        var table = entity.GetTableName()!;
        await db.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        using var schema = connection.CreateCommand();
        schema.CommandText = $"PRAGMA table_info(\"{table}\")";
        var columns = new HashSet<string>();
        await using (var reader = await schema.ExecuteReaderAsync(TestContext.Current.CancellationToken))
            while (await reader.ReadAsync(TestContext.Current.CancellationToken)) columns.Add(reader.GetString(1));
        var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
        var properties = entity.GetProperties().Where(property => columns.Contains(property.GetColumnName(store)!)).ToArray();
        using var insert = connection.CreateCommand();
        var verb = row is InstallationConfigurationRow ? "INSERT OR REPLACE" : "INSERT";
        insert.CommandText = $"{verb} INTO \"{table}\" ({string.Join(',', properties.Select(property => '\"' + property.GetColumnName(store)! + '\"'))}) VALUES ({string.Join(',', properties.Select((_, i) => "$p" + i))})";
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            var value = property.PropertyInfo!.GetValue(row);
            if (value is not null && property.GetTypeMapping().Converter is { } converter) value = converter.ConvertToProvider(value);
            insert.Parameters.AddWithValue("$p" + i, value ?? DBNull.Value);
        }
        await insert.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private sealed class BackfillOnlyHasher : IVideoFileHasher
    {
        public Task<VideoFileHashes> ComputeAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Established content must not be rehashed during upgrade.");
    }
}
