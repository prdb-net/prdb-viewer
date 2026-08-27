namespace Prdb.Viewer.Infrastructure.Persistence;

public sealed class DatabaseMigrationException(string message, Exception innerException)
    : Exception(message, innerException);
