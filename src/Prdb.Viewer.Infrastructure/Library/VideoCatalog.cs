using Microsoft.EntityFrameworkCore;

using Prdb.Viewer.Core.Library;
using Prdb.Viewer.Infrastructure.Persistence;

namespace Prdb.Viewer.Infrastructure.Library;

public sealed record VideoFileSummary(
    Guid Id,
    string RelativePath,
    long Size,
    long DurationMilliseconds,
    string ContainerFormat,
    string VideoCodec,
    string? AudioCodec,
    int? Width,
    int? Height,
    VideoFileAvailability Availability,
    DirectPlayClassification DirectPlayClassification,
    string DeliveryUrl);

public sealed record VideoSummary(
    Guid Id,
    string DisplayTitle,
    DateTimeOffset DiscoveryDate,
    IReadOnlyList<VideoFileSummary> VideoFiles);

public sealed class VideoCatalog(ViewerDbContext database)
{
    public async Task<IReadOnlyList<VideoSummary>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var videos = await database.Videos
            .AsNoTracking()
            .Include(video => video.VideoFiles)
            .Where(video => video.VideoFiles.Any(file =>
                file.Availability == VideoFileAvailability.Available))
            .OrderByDescending(video => video.DiscoveryDate)
            .ToListAsync(cancellationToken);

        return videos.Select(video =>
        {
            var files = video.VideoFiles
                .Where(file => file.Availability == VideoFileAvailability.Available)
                .OrderBy(file => file.RelativePath)
                .Select(file => new VideoFileSummary(
                    file.Id,
                    file.RelativePath,
                    file.Size,
                    file.DurationMilliseconds,
                    file.ContainerFormat,
                    file.VideoCodec,
                    file.AudioCodec,
                    file.Width,
                    file.Height,
                    file.Availability,
                    file.DirectPlayClassification,
                    $"/media/videos/{file.PublicDeliveryId}"))
                .ToArray();
            var title = Path.GetFileNameWithoutExtension(files[0].RelativePath);
            return new VideoSummary(
                video.Id,
                string.IsNullOrWhiteSpace(title) ? "Unknown Video" : title,
                AsOffset(video.DiscoveryDate),
                files);
        }).ToArray();
    }

    private static DateTimeOffset AsOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
