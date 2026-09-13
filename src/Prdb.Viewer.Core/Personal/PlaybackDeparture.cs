namespace Prdb.Viewer.Core.Personal;

/// <summary>
/// How a Viewing Session ended, where anything was actually observed about it.
/// </summary>
/// <remarks>
/// Only <see cref="AnotherVideo"/> is evidence about the Video, and only in combination with a very
/// short watch: somebody who looked at a Video for a few seconds and then went to a different one
/// has said something, quietly. None of the others is. A decode failure, a closed tab and a session
/// nothing accounts for are all ways of not knowing, and the whole point of naming them separately
/// is that none of them may be read as a dislike.
/// </remarks>
public enum PlaybackDeparture
{
    /// <summary>Nothing was observed. The default, and never negative evidence.</summary>
    Unknown,

    /// <summary>The User went from this Video to a different one, positively observed.</summary>
    AnotherVideo,

    /// <summary>Playback failed. Evidence about a Video File, and about nothing else.</summary>
    TechnicalFailure,

    /// <summary>The player or the page was closed. An ordinary end to an ordinary session.</summary>
    Closed,

    /// <summary>The session was ended by the inactivity timeout rather than by anybody.</summary>
    Inactivity,
}
