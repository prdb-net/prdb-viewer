using PrdbOsHash = Prdb.Hashing.OsHash;
using PrdbVideoPerceptualHasher = Prdb.Hashing.VideoPerceptualHasher;

namespace Prdb.Viewer.Infrastructure.Library;

/// <summary>
/// The content hashes prdb identifies a file by. Either value may be absent: a short file and a
/// container the hashing library does not index have no OS hash, and a container ffmpeg cannot
/// sample produces no perceptual hash. One usable hash is enough to be identified by content.
/// </summary>
public sealed record VideoFileHashes(
    string? OsHash,
    string? PerceptualHash,
    string? FailureReason);

public interface IVideoFileHasher
{
    Task<VideoFileHashes> ComputeAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Computes both hashes with the official <c>Prdb.Hashing</c> package so their values match what
/// the prdb Public API stores. Failures are reported rather than thrown, because an unreadable or
/// unsampleable file is routine on a real library and must not abandon the run.
/// </summary>
public sealed class PrdbVideoFileHasher : IVideoFileHasher
{
    private readonly PrdbVideoPerceptualHasher perceptual = new();

    public async Task<VideoFileHashes> ComputeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string? osHash;

        try
        {
            osHash = PrdbOsHash.TryCompute(path, out var computed) ? computed : null;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return new VideoFileHashes(null, null, "The file could not be read for hashing.");
        }

        var result = await perceptual.ComputeAsync(path, cancellationToken);

        return new VideoFileHashes(
            osHash,
            result.Hash,
            result.IsComputed ? null : $"No perceptual hash: {result.Outcome}.");
    }
}
