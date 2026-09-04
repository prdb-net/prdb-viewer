using System.Net;

using Prdb.FakeCatalogue;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// The client that actually talks to prdb, over the transport it actually uses.
///
/// Everywhere else in this suite <c>IPrdbIdentificationClient</c> is replaced outright, which
/// tests what the lanes do with an answer and nothing about how an answer is obtained. The whole
/// of it — what the SDK puts on the wire, how a reply becomes the records the rest of the code
/// reads, and what each documented failure turns into — went untested, and it is the part that
/// decides what an Administrator is told when something is wrong.
///
/// The failures matter most, because they cannot be produced any other way. Nobody can ask the
/// real service for a 503.
/// </summary>
public sealed class PrdbIdentificationClientTests
{
    private static readonly Guid FirstFile = Guid.Parse("01a01a22-70e8-7bd0-a34e-000000000001");
    private static readonly Guid SecondFile = Guid.Parse("01a01a22-70e8-7bd0-a34e-000000000002");

    [Fact]
    public async Task A_recognised_file_arrives_as_a_Work_with_its_Site_and_confidence()
    {
        var prdb = new FakePrdb().Recognises("known.mp4", "A Known Work", "Example Site");

        var result = await IdentifyAsync(prdb, (FirstFile, "known.mp4"));

        Assert.Equal(IdentificationBatchStatus.Identified, result.Status);
        var identification = Assert.Single(result.Results);

        // The file the answer belongs to is carried by `ref`, which prdb echoes back unchanged.
        // Losing that association would file one Video's identity against another's.
        Assert.Equal(FirstFile, identification.VideoFileId);

        // On the wire these are integers. Read as anything else they would land on the wrong
        // member of each enum, and a file would be filed automatically on evidence that did not
        // warrant it.
        Assert.Equal(RemoteMatchConfidence.Exact, identification.Confidence);
        Assert.Equal(RemoteMatchKind.OsHash, identification.MatchedBy);

        Assert.Equal("A Known Work", identification.Work?.Title);
        Assert.Equal("Example Site", identification.Site?.Title);
        Assert.Equal("Example Site", identification.Work?.Site?.Title);
    }

    [Fact]
    public async Task An_actor_arrives_with_the_identity_prdb_holds_for_them()
    {
        var prdb = new FakePrdb()
            .Recognises("known.mp4", "A Known Work", "Example Site", 4, 0, "Alex Doe", "Sam Roe");

        var result = await IdentifyAsync(prdb, (FirstFile, "known.mp4"));

        // The name is what a facet shows; the identity is what an Actor's own page is reached by,
        // and it is on the wire in every answer this client has ever received (ADR 0020).
        var actors = Assert.Single(result.Results).Work?.Actors;
        Assert.Equal(["Alex Doe", "Sam Roe"], actors?.Select(actor => actor.Name));
        Assert.Equal(
            [CatalogueEntry.Identifier("actor:Alex Doe").ToString(),
             CatalogueEntry.Identifier("actor:Sam Roe").ToString()],
            actors?.Select(actor => actor.Id));
    }

    [Fact]
    public async Task The_rest_of_what_the_answer_carries_is_read_rather_than_dropped()
    {
        var prdb = new FakePrdb().Recognises("known.mp4", "A Known Work", "Example Site");

        var work = (await IdentifyAsync(prdb, (FirstFile, "known.mp4"))).Results.Single().Work;

        Assert.NotNull(work);

        // One identification answer pays for all of this, and all of it used to be thrown away in
        // the line that read the title.
        Assert.Equal("Example Site Network", work.Network?.Title);
        Assert.Equal(["A.Known.Work.1080p.WEB-DL"], work.ReleaseNames);
        Assert.Equal(4_000, work.Duration?.SpreadMilliseconds);
        Assert.Equal(3, work.Duration?.FileCount);
        Assert.Equal(["3840×2160", "1920×1080"], work.QualityOverview?.Resolutions);
        Assert.Equal(["h264", "av1"], work.QualityOverview?.VideoCodecs);
        Assert.Equal(2, work.Images.Count);

        // The first image is what the review case has always compared against, so it stays where
        // the rest of the code already looks for it.
        Assert.Equal(work.Images[0].Url, work.ArtworkUrl);
    }

    [Fact]
    public async Task A_file_prdb_does_not_hold_comes_back_answered_rather_than_missing()
    {
        var prdb = new FakePrdb().Recognises("known.mp4", "A Known Work");

        var result = await IdentifyAsync(
            prdb,
            (FirstFile, "known.mp4"),
            (SecondFile, "nobody-has-catalogued-this.mp4"));

        Assert.Equal(IdentificationBatchStatus.Identified, result.Status);
        Assert.Equal(2, result.Results.Count);

        // An unrecognised file is a result at no confidence, not an absent one. A client that
        // treated silence as the answer would ask about it again for ever.
        var unknown = result.Results.Single(one => one.VideoFileId == SecondFile);
        Assert.Equal(RemoteMatchConfidence.None, unknown.Confidence);
        Assert.Null(unknown.Work);
        Assert.Null(unknown.Site);
    }

    [Fact]
    public async Task The_request_carries_the_file_name_and_size_and_nothing_of_the_content()
    {
        var prdb = new FakePrdb();

        await IdentifyAsync(prdb, (FirstFile, "known.mp4"));

        var sent = Assert.Single(prdb.IdentifyRequests);
        var file = sent["files"]!.AsArray().Single()!;

        // The product's claim about itself: only the name, the size and the hashes are offered.
        Assert.Equal("known.mp4", file["filename"]!.GetValue<string>());
        Assert.Equal(4_096, file["filesize"]!.GetValue<long>());
        Assert.Equal(FirstFile.ToString("n"), file["ref"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refused_credential_is_reported_as_refused_rather_than_as_an_outage(
        HttpStatusCode status)
    {
        // The distinction is the whole point: a refusal needs an Administrator to correct the
        // credential, while an outage resolves itself and must not send anyone looking.
        var result = await IdentifyAsync(new FakePrdb { Failure = status }, (FirstFile, "a.mp4"));

        Assert.Equal(IdentificationBatchStatus.Rejected, result.Status);
        Assert.Empty(result.Results);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_outage_or_a_rate_limit_leaves_the_work_to_be_retried(HttpStatusCode status)
    {
        var result = await IdentifyAsync(new FakePrdb { Failure = status }, (FirstFile, "a.mp4"));

        Assert.Equal(IdentificationBatchStatus.Unavailable, result.Status);
        Assert.Empty(result.Results);
    }

    [Fact]
    public async Task A_reply_that_is_not_the_documented_shape_is_an_outage_and_not_a_crash()
    {
        // A proxy that answers with its own error page, or a service half way through a
        // deployment. It reaches the same code path as an outage rather than ending the lane.
        var result = await IdentifyAsync(
            new FakePrdb { RawBody = "<html>Gateway</html>" },
            (FirstFile, "a.mp4"));

        Assert.Equal(IdentificationBatchStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task A_result_for_a_file_that_was_never_asked_about_is_discarded()
    {
        // `ref` is what ties an answer to a file. One that is not a reference this client issued
        // cannot be filed against anything, and guessing would attach a stranger's identity.
        var result = await IdentifyAsync(
            new FakePrdb { RawBody = """{"results":[{"ref":"not-a-guid","confidence":4,"candidates":[]}]}""" },
            (FirstFile, "a.mp4"));

        Assert.Equal(IdentificationBatchStatus.Identified, result.Status);
        Assert.Empty(result.Results);
    }

    private static Task<IdentificationBatchResult> IdentifyAsync(
        FakePrdb prdb,
        params (Guid Id, string Name)[] files) =>
        new PrdbIdentificationClient(new FakePrdbTransport(prdb)).IdentifyAsync(
            "fixture-credential",
            files
                .Select(file => new RemoteIdentificationRequest(
                    file.Id,
                    file.Name,
                    4_096,
                    "0123456789abcdef",
                    "fedcba9876543210"))
                .ToArray(),
            TestContext.Current.CancellationToken);
}
