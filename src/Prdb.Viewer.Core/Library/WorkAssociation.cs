namespace Prdb.Viewer.Core.Library;

/// <summary>
/// Where a Work Association stands.
///
/// An association asserts that two Videos carry the same content without naming what that content
/// is, so it is not an Identification Claim and has no target to be right or wrong about. What it
/// has instead is whether it was acted on: Proposed while it waits for an Administrator,
/// Established once the two Videos are one, Rejected when a person said they are not the same, and
/// Separated when a Split took them apart again.
/// </summary>
public enum WorkAssociationStatus
{
    Proposed,
    Established,
    Rejected,
    Separated,
}
