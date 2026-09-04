namespace Prdb.Viewer.Core.Library;

public enum BackgroundWorkCategory
{
    LibraryScan,
    TechnicalInspection,
    Hashing,
    PreviewGeneration,
    Identification,
    SiteRecognition,

    /// <summary>
    /// Asking prdb again about works this library has already established, so that what one
    /// identification paid for is not thrown away: the identity of every Actor it credited, and
    /// the facts about the work that the identification answer carried and nothing kept.
    /// </summary>
    Enrichment,
}
