using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Identity;

using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Configuration;

namespace Prdb.Viewer.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddViewerPersistence(
        this IServiceCollection services,
        string dataDirectory,
        string libraryMountRoot = LibraryMountRoot.DefaultPath)
    {
        var location = new ViewerDatabaseLocation(dataDirectory);

        services.AddSingleton(location);
        services.AddSingleton<SqlitePragmaInterceptor>();
        services.AddDbContext<ViewerDbContext>((provider, options) => options
            .UseSqlite(location.ConnectionString)
            .AddInterceptors(provider.GetRequiredService<SqlitePragmaInterceptor>())
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddScoped<DatabaseMigrator>();
        services.AddSingleton<OperatorCredentialFiles>();
        services.AddSingleton<IPasswordHasher<AccountRow>, PasswordHasher<AccountRow>>();
        services.AddScoped<AccessService>();
        services.AddSingleton(new LibraryMountRoot(libraryMountRoot));
        services.AddScoped<LibraryDirectoryInspector>();
        services.AddScoped<IPrdbConnectionVerifier, PrdbConnectionVerifier>();
        services.AddScoped<InstallationConfigurationService>();
        services.AddTransient<ProductUserAgentHandler>();
        services.AddHttpClient(PrdbConnectionVerifier.TransportName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
            })
            .AddHttpMessageHandler<ProductUserAgentHandler>()
            .RedactLoggedHeaders(["X-Api-Key"]);

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
