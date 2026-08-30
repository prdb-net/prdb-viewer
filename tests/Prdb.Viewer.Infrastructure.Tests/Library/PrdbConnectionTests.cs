using System.Net;

using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;

using Xunit;

namespace Prdb.Viewer.Infrastructure.Tests.Library;

/// <summary>
/// The two clients that ask prdb something other than an identity, over the transport they use in
/// production.
///
/// Both turn an HTTP failure into one of two very different answers, and the difference is what an
/// Administrator is asked to do about it: a refused credential is theirs to correct, an outage is
/// nobody's and resolves itself. That decision is taken from a status code, and until now nothing
/// checked which code produced which answer — the suites replaced these clients rather than
/// exercising them.
/// </summary>
public sealed class PrdbConnectionTests
{
    public sealed class Verifying
    {
        [Fact]
        public async Task An_answered_rate_limit_verifies_the_credential()
        {
            var outcome = await VerifyAsync(new FakePrdb());

            Assert.Equal(PrdbVerificationOutcome.Verified, outcome);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task A_refusal_is_the_credential_and_not_the_service(HttpStatusCode status)
        {
            // This is the outcome that puts a Work Issue in front of an Administrator. Reported as
            // an outage instead, a wrong key would look like something that would fix itself.
            var outcome = await VerifyAsync(new FakePrdb { Failure = status });

            Assert.Equal(PrdbVerificationOutcome.Rejected, outcome);
        }

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        [InlineData(HttpStatusCode.InternalServerError)]
        public async Task An_outage_is_the_service_and_not_the_credential(HttpStatusCode status)
        {
            // And the other way round: an outage reported as a refusal would send someone looking
            // for a new key over something that recovers on its own.
            var outcome = await VerifyAsync(new FakePrdb { Failure = status });

            Assert.Equal(PrdbVerificationOutcome.Unavailable, outcome);
        }

        [Fact]
        public async Task A_reply_that_is_not_JSON_at_all_leaves_the_credential_unjudged()
        {
            // What a captive portal or a gateway answers with. It fails to parse, which is loud
            // enough to be handled already.
            var outcome = await VerifyAsync(new FakePrdb { RawBody = "<html>Gateway</html>" });

            Assert.Equal(PrdbVerificationOutcome.Unavailable, outcome);
        }

        [Fact]
        public async Task A_reply_carrying_none_of_the_documented_fields_verifies_nothing()
        {
            // The quiet one. This parses, so nothing is thrown and an object comes back — it just
            // holds none of the answer. Checked only for being non-null, it read as proof that a
            // credential worked, when what actually happened is that something answered 200 and
            // said nothing. An endpoint that has moved, or anything at all between here and prdb,
            // looks exactly like this.
            var outcome = await VerifyAsync(new FakePrdb { RawBody = "{}" });

            Assert.Equal(PrdbVerificationOutcome.Unavailable, outcome);
        }

        [Fact]
        public async Task A_configured_endpoint_is_where_the_client_goes()
        {
            // The switch exists so an installation can be pointed at a catalogue that answers on
            // demand. Untested, it would be a setting that reads well and changes nothing.
            var prdb = new FakePrdb();

            await new PrdbConnectionVerifier(
                    new FakePrdbTransport(prdb),
                    new PrdbEndpoint("https://prdb.example.invalid"))
                .VerifyAsync("fixture-credential", TestContext.Current.CancellationToken);

            Assert.Equal("prdb.example.invalid", Assert.Single(prdb.Requested).Host);
        }

        [Fact]
        public async Task The_published_service_is_where_it_goes_without_one()
        {
            var prdb = new FakePrdb();

            await VerifyAsync(prdb);

            Assert.Equal("api.prdb.net", Assert.Single(prdb.Requested).Host);
        }

        private static Task<PrdbVerificationOutcome> VerifyAsync(FakePrdb prdb) =>
            new PrdbConnectionVerifier(new FakePrdbTransport(prdb))
                .VerifyAsync("fixture-credential", TestContext.Current.CancellationToken);
    }

    public sealed class FetchingTheSiteDirectory
    {
        /// <summary>What the client asks for, and therefore what a full page looks like to it.</summary>
        private const int PageSize = 1_000;

        [Fact]
        public async Task Each_Site_arrives_with_the_identity_the_library_files_against()
        {
            var result = await FetchAsync(new FakePrdb());

            Assert.Equal(SiteDirectoryFetchStatus.Fetched, result.Status);
            var site = Assert.Single(result.Sites);
            Assert.Equal("Site 1-0", site.Title);
            Assert.False(string.IsNullOrWhiteSpace(site.Id));
        }

        [Fact]
        public async Task A_Site_without_a_title_is_dropped_rather_than_kept_as_a_blank()
        {
            // It satisfies the schema and is still unusable. Kept, it would show up as an
            // unnamed row that nothing can be filed against.
            var result = await FetchAsync(new FakePrdb { IncludeUnusableSite = true });

            Assert.Equal(SiteDirectoryFetchStatus.Fetched, result.Status);
            Assert.Single(result.Sites);
        }

        [Fact]
        public async Task A_full_page_is_followed_by_the_next_one()
        {
            var prdb = new FakePrdb();
            prdb.SitePages[1] = PageSize;
            prdb.SitePages[2] = 3;

            var result = await FetchAsync(prdb);

            // A page that is full is the only sign there is more to come, so a client that stopped
            // at the first one would silently hold an incomplete directory.
            Assert.Equal([1, 2], prdb.SitePagesRequested);
            Assert.Equal(PageSize + 3, result.Sites.Count);
        }

        [Fact]
        public async Task A_catalogue_that_never_ends_is_left_after_a_fixed_number_of_pages()
        {
            // Every page comes back full, which is what a miscounted or unbounded catalogue looks
            // like from here. The ceiling exists so a daily refresh cannot become a loop, and
            // nothing has ever checked that it holds.
            var prdb = new FakePrdb();

            for (var page = 1; page <= 40; page++)
            {
                prdb.SitePages[page] = PageSize;
            }

            var result = await FetchAsync(prdb);

            Assert.Equal(SiteDirectoryFetchStatus.Fetched, result.Status);
            Assert.Equal(20, prdb.SitePagesRequested.Count);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task A_refusal_names_the_credential(HttpStatusCode status)
        {
            var result = await FetchAsync(new FakePrdb { Failure = status });

            Assert.Equal(SiteDirectoryFetchStatus.Rejected, result.Status);
            Assert.Empty(result.Sites);
        }

        [Theory]
        [InlineData(HttpStatusCode.ServiceUnavailable)]
        [InlineData(HttpStatusCode.TooManyRequests)]
        public async Task An_outage_carries_the_status_it_failed_with(HttpStatusCode status)
        {
            var result = await FetchAsync(new FakePrdb { Failure = status });

            Assert.Equal(SiteDirectoryFetchStatus.Unavailable, result.Status);

            // The detail is written into an Operator Handoff, so the number has to survive.
            Assert.Contains(((int)status).ToString(), result.Detail);
        }

        private static Task<SiteDirectoryFetchResult> FetchAsync(FakePrdb prdb) =>
            new PrdbSiteDirectoryClient(new FakePrdbTransport(prdb))
                .FetchAsync("fixture-credential", TestContext.Current.CancellationToken);
    }
}
