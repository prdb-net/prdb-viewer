using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class VideoFileRow
{
    public Guid Id { get; set; }

    public Guid VideoId { get; set; }

    public VideoRow Video { get; set; } = null!;

    public Guid LibraryDirectoryId { get; set; }

    public LibraryDirectoryRow LibraryDirectory { get; set; } = null!;

    public required string RelativePath { get; set; }

    public long Size { get; set; }

    public DateTime LastWriteTimeUtc { get; set; }

    public required string Sha256 { get; set; }

    public Guid PublicDeliveryId { get; set; }

    public required string ContainerFormat { get; set; }

    public required string VideoCodec { get; set; }

    public string? AudioCodec { get; set; }

    public long DurationMilliseconds { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    /// <summary>The codec profile as the inspector names it, such as `High` or `Main 10`.</summary>
    public string? VideoProfile { get; set; }

    /// <summary>The codec level times ten, as `avc1` and `hvc1` encode it: 4.0 is 40.</summary>
    public int? VideoLevel { get; set; }

    public int? BitDepth { get; set; }

    public double? FrameRate { get; set; }

    public long? VideoBitrate { get; set; }

    public int? AudioChannels { get; set; }

    public int? AudioSampleRate { get; set; }

    public long? AudioBitrate { get; set; }

    /// <summary>
    /// The question this file puts to a client, derived from the facts above. Two files that share
    /// it are one question, so one Client Playback Assessment answers both.
    /// </summary>
    public string ProfileKey { get; set; } = string.Empty;

    public VideoFileAvailability Availability { get; set; }

    public DirectPlayClassification DirectPlayClassification { get; set; }

    public Guid LastObservedScanId { get; set; }

    public int ConsecutiveCompleteAbsences { get; set; }

    public DateTime InspectedAt { get; set; }

    /// <summary>
    /// The Video identity this occurrence carried before a merge moved it. It lets a later split
    /// reactivate the historical identity instead of inventing a new one.
    /// </summary>
    public Guid? PreviousVideoId { get; set; }

    public string? OsHash { get; set; }

    public string? PerceptualHash { get; set; }

    /// <summary>
    /// The observed content the hashes belong to. Content that no longer matches is hashed again
    /// rather than identified against values taken from different bytes.
    /// </summary>
    public string? HashedSha256 { get; set; }

    public DateTime? HashedAt { get; set; }

    public VideoFileHashState HashState { get; set; }

    public string? HashFailureReason { get; set; }

    public Guid? PublicPreviewId { get; set; }

    public string? PreviewRelativePath { get; set; }

    public string? PreviewSha256 { get; set; }

    public DateTime? PreviewGeneratedAt { get; set; }

    public VideoFilePreviewState PreviewState { get; set; }

    public string? IdentifiedSha256 { get; set; }

    public DateTime? IdentifiedAt { get; set; }

    /// <summary>
    /// The path local Site Recognition last read, and when. Site Recognition reads the path rather
    /// than the content, so a renamed file is read again while an unchanged one is not.
    /// </summary>
    public string? SiteRecognisedPath { get; set; }

    public DateTime? SiteRecognisedAt { get; set; }

    public ICollection<PlaybackAttemptVideoFileRow> PlaybackAttempts { get; set; } = [];

    /// <summary>
    /// The inspected media configuration this row carries, as the direct-play rules read it.
    /// </summary>
    public MediaConfiguration Media => new(ContainerFormat, VideoCodec, AudioCodec)
    {
        VideoProfile = VideoProfile,
        VideoLevel = VideoLevel,
        BitDepth = BitDepth,
        Width = Width,
        Height = Height,
        FrameRate = FrameRate,
        VideoBitrate = VideoBitrate,
        AudioChannels = AudioChannels,
        AudioSampleRate = AudioSampleRate,
        AudioBitrate = AudioBitrate,
    };

    /// <summary>
    /// Commits one inspection's facts, together with everything derived from them in the same
    /// breath: the Direct-Play Classification and the Profile Key a client is asked about. They are
    /// written here so no caller can store the facts and forget what they mean.
    /// </summary>
    public void ApplyInspectedMedia(MediaConfiguration media, long durationMilliseconds)
    {
        ContainerFormat = media.ContainerFormat;
        VideoCodec = media.VideoCodec;
        AudioCodec = media.AudioCodec;
        VideoProfile = media.VideoProfile;
        VideoLevel = media.VideoLevel;
        BitDepth = media.BitDepth;
        Width = media.Width;
        Height = media.Height;
        FrameRate = media.FrameRate;
        VideoBitrate = media.VideoBitrate;
        AudioChannels = media.AudioChannels;
        AudioSampleRate = media.AudioSampleRate;
        AudioBitrate = media.AudioBitrate;
        DurationMilliseconds = durationMilliseconds;
        DirectPlayClassification = DirectPlayClassificationRule.Classify(media);
        ProfileKey = media.ProfileKey;
    }
}
