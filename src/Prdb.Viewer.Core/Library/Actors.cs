namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Whether this installation holds what prdb says about an Actor.
/// </summary>
/// <remarks>
/// An Actor Profile is a projection, not knowledge this installation owns (ADR 0020), so its
/// absence is an ordinary state rather than a fault: Pending is an Actor the lane has not reached,
/// Unavailable is one prdb had nothing to say about, and neither stops the Actor's page from
/// naming them and listing their Videos.
/// </remarks>
public enum ActorProfileState
{
    Pending,
    Retained,
    Unavailable,
}

/// <summary>
/// Whether a retained picture's bytes are held here — an Actor's, or a work's. A picture is served
/// from this installation's own origin or not at all; the browser is never sent to prdb for one.
/// </summary>
public enum ActorImageState
{
    Pending,
    Retained,
    Unavailable,
}

/// <summary>
/// The kinds of picture prdb holds for an Actor, in the order it orders them.
/// </summary>
/// <remarks>
/// The application derives one thing from the kind: which picture is the Actor Portrait, the one
/// that stands for them in a list. Everything else is gallery, in the order prdb gives.
/// </remarks>
public static class ActorImageKind
{
    public const int Thumbnail = 1;

    public const int Poster = 2;

    public const int Face = 3;
}
