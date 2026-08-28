using Prdb.Viewer.Core.Library;

namespace Prdb.Viewer.Infrastructure.Persistence;

/// <summary>
/// What one Account's client made of one media configuration. It is Personal State: it belongs to
/// the Account and the client context that produced it, influences nothing another Account sees,
/// and is never shown to an Administrator as activity.
///
/// Validity is structural rather than timed. The assessment is keyed by the Profile Key, so a
/// Video File whose inspected facts change asks a new question; and by the client context, so a
/// different browser, device, or platform asks its own.
/// </summary>
public sealed class ClientPlaybackAssessmentRow
{
    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public required string ClientContextKey { get; set; }

    public required string ProfileKey { get; set; }

    public ClientPlaybackAssessmentVerdict Verdict { get; set; }

    /// <summary>Whether the client expects playback to be smooth, where it can tell.</summary>
    public bool? Smooth { get; set; }

    public bool? PowerEfficient { get; set; }

    /// <summary>How the client answered: Media Capabilities, or the coarser type support test.</summary>
    public required string Method { get; set; }

    public DateTime AssessedAt { get; set; }
}

/// <summary>
/// What actually happened when one Account played one Video File on one client. It outranks every
/// prediction within that scope and nowhere else.
///
/// The observation is bound to the content it was made about, so a replaced or re-inspected file
/// is not judged by what its predecessor did.
/// </summary>
public sealed class ObservedPlaybackOutcomeRow
{
    public Guid AccountId { get; set; }

    public AccountRow Account { get; set; } = null!;

    public required string ClientContextKey { get; set; }

    public Guid VideoFileId { get; set; }

    public VideoFileRow VideoFile { get; set; } = null!;

    /// <summary>The content this outcome describes; a different one is a different question.</summary>
    public required string ContentSha256 { get; set; }

    public ObservedPlaybackOutcome Outcome { get; set; }

    /// <summary>
    /// Why an attempt failed. Only a Media failure says anything about the file: delivery and
    /// network failures are the installation's problem and never rule a variant out.
    /// </summary>
    public PlaybackFailureCategory? FailureCategory { get; set; }

    public DateTime ObservedAt { get; set; }
}
