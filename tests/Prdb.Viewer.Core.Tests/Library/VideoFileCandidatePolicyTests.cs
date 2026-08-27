using Prdb.Viewer.Core.Library;

using Xunit;

namespace Prdb.Viewer.Core.Tests.Library;

public sealed class VideoFileCandidatePolicyTests
{
    [Theory]
    [InlineData(".mp4")]
    [InlineData(".MKV")]
    [InlineData(".webm")]
    public void Recognised_video_extensions_are_admitted(string extension) =>
        Assert.True(VideoFileCandidatePolicy.Recognizes(extension));

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".txt")]
    [InlineData("")]
    public void Other_extensions_are_not_candidates(string extension) =>
        Assert.False(VideoFileCandidatePolicy.Recognizes(extension));
}
