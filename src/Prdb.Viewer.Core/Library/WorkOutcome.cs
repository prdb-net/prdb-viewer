namespace Prdb.Viewer.Core.Library;

/// <summary>
/// What one run made of one thing it asked about.
///
/// A lane's item count says how far it got. This says what it came to, which is the only half that
/// can answer why a run established less than the one before it: three files identified, three
/// files prdb had never heard of, and three files left for a person to settle are the same three
/// files done, and nothing about them is the same.
/// </summary>
public enum WorkOutcome
{
    /// <summary>It established Shared Library Knowledge.</summary>
    Established,

    /// <summary>It asked, and got back nothing that settles anything.</summary>
    Unanswered,

    /// <summary>It produced a reviewable candidate, which a person settles.</summary>
    Reviewable,
}
