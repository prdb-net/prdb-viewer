namespace Prdb.Viewer.Core.Library;

/// <summary>prdb's current catalogue assertion, independent of local identity and storage availability.</summary>
public enum RemoteCatalogueState
{
    Unknown,
    Active,
    Deleted,
    Merged,
}
