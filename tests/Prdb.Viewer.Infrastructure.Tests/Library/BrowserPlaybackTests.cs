using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Persistence;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// Runs the real inspection, hashing, and preview tools over generated clips, so the classification
/// the browser depends on is proved against actual media rather than a fixture's opinion of it.
/// </summary>
public sealed class BrowserPlaybackTests
{
    [Fact]
    public async Task Real_media_is_inspected_classified_hashed_and_previewed_for_the_browser()
    {
        Assert.SkipUnless(
            BrowserPlaybackFixtures.FfmpegIsAvailable,
            "ffmpeg and ffprobe are required for the browser playback fixtures.");

        await using var store = await TestDatabase.CreateAsync(
            identificationClient: new FixtureIdentificationClient());
        var source = Path.Combine(store.LibraryMountRoot.Path, "source");
        var baseline = await BrowserPlaybackFixtures.BaselineMp4Async(source);
        await BrowserPlaybackFixtures.ClientDependentWebmAsync(source);
        await BrowserPlaybackFixtures.UnsupportedMpegAsync(source);

        // A recognised extension whose content is not audiovisual is a Scoped Issue, not a Video.
        await File.WriteAllBytesAsync(
            Path.Combine(source, "not-really-video.mp4"),
            [0, 1, 2, 3, 4, 5, 6, 7],
            TestContext.Current.CancellationToken);
        var beforeBytes = await File.ReadAllBytesAsync(
            baseline,
            TestContext.Current.CancellationToken);
        var beforeWritten = File.GetLastWriteTimeUtc(baseline);

        await LibraryPipeline.ActivateAsync(store, source);
        await LibraryPipeline.SetCredentialAsync(store, "installation-key");
        await LibraryPipeline.DrainAsync(store);

        await using var scope = store.Scope();
        var database = scope.ServiceProvider.GetRequiredService<ViewerDbContext>();
        var files = await database.VideoFiles
            .OrderBy(file => file.RelativePath)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, files.Count);

        var mp4 = files.Single(file => file.RelativePath == "baseline.mp4");
        Assert.Equal("h264", mp4.VideoCodec);
        Assert.Equal("aac", mp4.AudioCodec);
        Assert.Equal(DirectPlayClassification.BaselineCandidate, mp4.DirectPlayClassification);
        Assert.InRange(mp4.DurationMilliseconds, 3_500, 4_500);
        Assert.Equal(320, mp4.Width);
        Assert.Equal(240, mp4.Height);

        var webm = files.Single(file => file.RelativePath == "client-dependent.webm");
        Assert.Equal("vp9", webm.VideoCodec);
        Assert.Equal(DirectPlayClassification.ClientDependent, webm.DirectPlayClassification);

        var mpeg = files.Single(file => file.RelativePath == "unsupported.mpg");
        Assert.Equal(DirectPlayClassification.Unsupported, mpeg.DirectPlayClassification);

        // Every admitted file carries a real preview image and at least one usable content hash.
        // A container the hashing library reports no value for leaves the file Incomplete rather
        // than Failed, and it stays identifiable and playable.
        Assert.All(files, file =>
        {
            Assert.NotEqual(VideoFileHashState.Failed, file.HashState);
            Assert.True(file.OsHash is not null || file.PerceptualHash is not null);
            Assert.Equal(VideoFilePreviewState.Generated, file.PreviewState);
            Assert.NotNull(file.PublicPreviewId);
        });
        Assert.Equal(VideoFileHashState.Computed, mp4.HashState);
        Assert.NotNull(mp4.OsHash);
        Assert.NotNull(mp4.PerceptualHash);
        Assert.Equal(VideoFileHashState.Incomplete, webm.HashState);
        Assert.NotNull(webm.PerceptualHash);

        var artifacts = scope.ServiceProvider.GetRequiredService<DerivedArtifactStore>();
        var preview = artifacts.PreviewFullPath(mp4.PreviewRelativePath!);
        var previewBytes = await File.ReadAllBytesAsync(
            preview,
            TestContext.Current.CancellationToken);
        Assert.True(previewBytes.Length > 0);
        Assert.Equal([0xFF, 0xD8, 0xFF], previewBytes[..3]);
        Assert.StartsWith(artifacts.PreviewsRoot, preview, StringComparison.Ordinal);

        // The unreadable candidate is explained without stopping the files beside it.
        var issue = Assert.Single(await database.WorkIssues.ToListAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(WorkIssueCause.InvalidContent, issue.Cause);
        Assert.Equal("not-really-video.mp4", issue.AffectedScope);

        // Nothing beneath the Library Directory was written.
        Assert.Equal(
            beforeBytes,
            await File.ReadAllBytesAsync(baseline, TestContext.Current.CancellationToken));
        Assert.Equal(beforeWritten, File.GetLastWriteTimeUtc(baseline));

        var delivery = await scope.ServiceProvider
            .GetRequiredService<VideoDeliveryService>()
            .OpenAsync(mp4.PublicDeliveryId, TestContext.Current.CancellationToken);
        Assert.NotNull(delivery);
        await delivery.Content.DisposeAsync();
        Assert.Equal("video/mp4", delivery.ContentType);

        var webmDelivery = await scope.ServiceProvider
            .GetRequiredService<VideoDeliveryService>()
            .OpenAsync(webm.PublicDeliveryId, TestContext.Current.CancellationToken);
        Assert.NotNull(webmDelivery);
        await webmDelivery.Content.DisposeAsync();
        Assert.Equal("video/webm", webmDelivery.ContentType);
    }
}
