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

    /// <summary>
    /// When this Library Directory is next due a Library Scan nobody asked for, or null while none
    /// is scheduled. Durable state is the only authority for what is due, so a restart neither
    /// forgets a period that elapsed while the application was down nor starts a fresh one.
    /// </summary>
    public DateTime? NextScanDueAt { get; set; }
}
