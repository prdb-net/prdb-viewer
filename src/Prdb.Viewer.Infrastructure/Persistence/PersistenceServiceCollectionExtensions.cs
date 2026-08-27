using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Prdb.Viewer.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddViewerPersistence(
        this IServiceCollection services,
        string dataDirectory)
    {
        var location = new ViewerDatabaseLocation(dataDirectory);

        services.AddSingleton(location);
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContext<ViewerDbContext>((provider, options) => options
            .UseSqlite(location.ConnectionString)
            .AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<DatabaseMigrator>();

        return services;
    }

    public static async Task PrepareViewerDatabaseAsync(
        this IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<DatabaseMigrator>()
            .PrepareAsync(cancellationToken);
    }
}
