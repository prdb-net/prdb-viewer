using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class PerceptualNeighbourhoodRuleTests
{
    [Fact]
    public void Two_identical_hashes_are_no_distance_apart() =>
        Assert.Equal(0, PerceptualNeighbourhoodRule.Distance("a1b2c3d4e5f60718", "a1b2c3d4e5f60718"));

    [Theory]
    [InlineData("0000000000000000", "0000000000000001", 1)]
    [InlineData("0000000000000000", "000000000000000f", 4)]
    [InlineData("0000000000000000", "ffffffffffffffff", 64)]
    [InlineData("ffffffffffffffff", "fffffffffffffff0", 4)]
    public void The_distance_counts_the_bits_that_differ(string left, string right, int expected) =>
        Assert.Equal(expected, PerceptualNeighbourhoodRule.Distance(left, right));

    [Fact]
    public void The_distance_does_not_depend_on_which_file_is_asked_about_first() =>
        Assert.Equal(
            PerceptualNeighbourhoodRule.Distance("00ff00ff00ff00ff", "00ff00ff00ff0000"),
            PerceptualNeighbourhoodRule.Distance("00ff00ff00ff0000", "00ff00ff00ff00ff"));

    [Fact]
    public void A_hash_is_read_whatever_case_it_is_written_in() =>
        Assert.Equal(0, PerceptualNeighbourhoodRule.Distance("A1B2C3D4E5F60718", "a1b2c3d4e5f60718"));

    /// <summary>
    /// A Video File whose container ffmpeg cannot sample has no Perceptual Hash at all. That
    /// silence is the same absence the remote ladder already lives with, and it must not read as a
    /// distance of zero from every other silent file.
    /// </summary>
    [Theory]
    [InlineData(null, "a1b2c3d4e5f60718")]
    [InlineData("a1b2c3d4e5f60718", null)]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("a1b2", "a1b2c3d4e5f60718")]
    [InlineData("a1b2c3d4e5f6071800", "a1b2c3d4e5f60718")]
    [InlineData("zzzzzzzzzzzzzzzz", "a1b2c3d4e5f60718")]
    public void Nothing_to_compare_is_no_distance_rather_than_a_close_one(string? left, string? right) =>
        Assert.Null(PerceptualNeighbourhoodRule.Distance(left, right));

    [Theory]
    [InlineData("0000000000000000", "0000000000000000")]
    [InlineData("0000000000000000", "000000000000003f")]
    public void Files_within_the_measured_band_are_neighbours(string left, string right) =>
        Assert.True(PerceptualNeighbourhoodRule.AreNeighbours(left, right));

    /// <summary>
    /// Seven of 64 is already beyond where the same-work distances ended, and eight — the value
    /// Stash uses — is where an unrelated pair of near-empty files got in.
    /// </summary>
    [Theory]
    [InlineData("0000000000000000", "000000000000007f")]
    [InlineData("0000000000000000", "00000000000000ff")]
    [InlineData("0000000000000000", "ffffffffffffffff")]
    public void Files_beyond_it_are_not(string left, string right) =>
        Assert.False(PerceptualNeighbourhoodRule.AreNeighbours(left, right));

    [Fact]
    public void A_file_without_a_hash_is_nobodys_neighbour() =>
        Assert.False(PerceptualNeighbourhoodRule.AreNeighbours(null, "0000000000000000"));

    [Theory]
    [InlineData(1_800_000, 1_800_000)]
    [InlineData(1_800_000, 1_795_500)]
    [InlineData(1_795_500, 1_800_000)]
    [InlineData(60_000, 59_850)]
    public void Running_times_within_a_quarter_of_a_percent_agree(long left, long right) =>
        Assert.True(PerceptualNeighbourhoodRule.DurationsAgree(left, right));

    /// <summary>
    /// The common case the tolerance refuses on purpose: one encode a few seconds shorter than the
    /// other. It is not lost — it becomes something a person decides.
    /// </summary>
    [Theory]
    [InlineData(1_800_000, 1_790_000)]
    [InlineData(1_800_000, 1_791_000)]
    [InlineData(1_800_000, 1_200_000)]
    public void Running_times_further_apart_do_not(long left, long right) =>
        Assert.False(PerceptualNeighbourhoodRule.DurationsAgree(left, right));

    [Theory]
    [InlineData(0, 1_800_000)]
    [InlineData(1_800_000, 0)]
    [InlineData(-1, 1_800_000)]
    [InlineData(0, 0)]
    public void A_running_time_inspection_never_established_agrees_with_nothing(long left, long right) =>
        Assert.False(PerceptualNeighbourhoodRule.DurationsAgree(left, right));
}
