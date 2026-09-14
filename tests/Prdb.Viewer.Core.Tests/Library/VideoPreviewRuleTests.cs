using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

/// <summary>
/// A preview still to be generated and one that could not be produced are the same neutral
/// placeholder on a screen and opposite facts underneath it.
/// </summary>
public sealed class VideoPreviewRuleTests
{
    private static VideoPreviewRule.Occurrence Available(VideoFilePreviewState preview) =>
        new(true, preview);

    private static VideoPreviewRule.Occurrence Unavailable(VideoFilePreviewState preview) =>
        new(false, preview);

    [Fact]
    public void A_Video_with_a_picture_has_nothing_to_explain() =>
        Assert.Equal(
            VideoPreviewState.Present,
            VideoPreviewRule.For(true, [Available(VideoFilePreviewState.Failed)]));

    [Fact]
    public void An_occurrence_still_to_be_attempted_is_one_the_picture_is_coming_for() =>
        Assert.Equal(
            VideoPreviewState.Pending,
            VideoPreviewRule.For(false, [Available(VideoFilePreviewState.Pending)]));

    /// <summary>
    /// The Video takes its picture from whichever occurrence produces one, so one attempt left is
    /// enough to be waiting on rather than done.
    /// </summary>
    [Fact]
    public void One_failed_occurrence_beside_one_still_to_try_is_still_waiting() =>
        Assert.Equal(
            VideoPreviewState.Pending,
            VideoPreviewRule.For(
                false,
                [Available(VideoFilePreviewState.Failed), Available(VideoFilePreviewState.Pending)]));

    [Fact]
    public void Every_available_occurrence_having_failed_is_as_good_as_the_picture_gets() =>
        Assert.Equal(
            VideoPreviewState.NoFrame,
            VideoPreviewRule.For(false, [Available(VideoFilePreviewState.Failed)]));

    /// <summary>
    /// Nothing can be sampled from a file the library cannot read, and that is a question about
    /// where the file is rather than about what is in it.
    /// </summary>
    [Fact]
    public void No_available_occurrence_leaves_nothing_to_sample_from() =>
        Assert.Equal(
            VideoPreviewState.Unreachable,
            VideoPreviewRule.For(
                false,
                [Unavailable(VideoFilePreviewState.Pending), Unavailable(VideoFilePreviewState.Failed)]));
}
