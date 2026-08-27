using Prdb.Viewer.Core.Configuration;

namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class LibraryDirectoryRow
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string ContainerPath { get; set; }

    public LibraryDirectoryState State { get; set; }

    public LibraryDirectoryHealth Health { get; set; }

    public int ConfigurationGeneration { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ActivatedAt { get; set; }

    public DateTime? RemovedAt { get; set; }

    public DateTime? InitialProcessingStartedAt { get; set; }
}
