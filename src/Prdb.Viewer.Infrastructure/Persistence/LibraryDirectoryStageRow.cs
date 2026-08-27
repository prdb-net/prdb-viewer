namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class LibraryDirectoryStageRow
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    public required string ContainerPath { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }
}
