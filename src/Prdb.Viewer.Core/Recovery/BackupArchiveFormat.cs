namespace Prdb.Viewer.Core.Recovery;

/// <summary>
/// Why a Backup Archive could not be accepted. Each value names a condition an operator can act on
/// without the product ever emitting decrypted state to explain itself.
/// </summary>
public enum BackupValidationFailure
{
    NotAnArchive,
    UnsupportedFormat,
    Damaged,
    WrongPassphraseOrAltered,
    MissingRequiredState,
    NoActiveAdministrator,
    BrokenReference,
    UnknownContent,
}

/// <summary>
/// The versioned, non-secret envelope every Backup Archive carries and the rules that decide which
/// archives a product version may open. The header exposes no personal or library content, and its
/// integrity is authenticated together with the archive body.
/// </summary>
public static class BackupArchiveFormat
{
    /// <summary>The first eight bytes of every archive: `PRDBVBAK`.</summary>
    public static ReadOnlySpan<byte> Magic => "PRDBVBAK"u8;

    /// <summary>The Backup Archive format this product version writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The earlier formats this product version restores directly. An archive older than this range
    /// names the next intermediate version an operator must use; a newer one is refused before any
    /// mutation rather than guessed at.
    /// </summary>
    public static readonly int[] DirectlySupportedVersions = [1];

    public const int SaltBytes = 16;
    public const int NonceBytes = 12;
    public const int TagBytes = 16;
    public const int KeyBytes = 32;

    /// <summary>
    /// Argon2id cost. The parameters travel in the authenticated header so an archive stays
    /// openable after the defaults change, and so a future version can refuse costs below its own
    /// floor rather than silently accepting a weakened archive.
    /// </summary>
    public const int MemoryKibiBytes = 64 * 1024;

    public const int Iterations = 3;

    public const int Parallelism = 2;

    /// <summary>The lowest cost this product version will accept from an archive header.</summary>
    public const int MinimumMemoryKibiBytes = 16 * 1024;

    public const int MinimumIterations = 2;

    public static bool CanRestoreDirectly(int formatVersion) =>
        DirectlySupportedVersions.Contains(formatVersion);

    public static bool IsAcceptableCost(int memoryKibiBytes, int iterations) =>
        memoryKibiBytes >= MinimumMemoryKibiBytes && iterations >= MinimumIterations;

    /// <summary>
    /// What an operator must do with a format this version cannot open directly. A newer archive is
    /// never guessed at, and an older one names the exact next product version to use rather than
    /// reporting a generic incompatibility.
    /// </summary>
    public static string ExplainUnsupported(int formatVersion) => formatVersion > CurrentVersion
        ? $"The archive uses Backup Archive format {formatVersion}, which this version does not " +
          $"know. Restore it with the product version that wrote it, or a newer one."
        : $"The archive uses Backup Archive format {formatVersion}. Restore it with product " +
          $"version 0.1, then back that installation up again with this version.";
}
