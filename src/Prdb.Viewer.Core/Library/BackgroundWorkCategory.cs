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
    /// Comparing this installation's own Video Files with each other, so that two files carrying
    /// the same work can be found without asking anybody. It is the one use of the Perceptual Hash
    /// that needs nothing from the network, and it works for exactly the files prdb had no answer
    /// about.
    /// </summary>
    PerceptualNeighbourhood,

    /// <summary>
    /// Asking prdb again about works this library has already established, so that what one
    /// identification paid for is not thrown away: the identity of every Actor it credited, and
    /// the facts about the work that the identification answer carried and nothing kept.
    /// </summary>
    Enrichment,
}
