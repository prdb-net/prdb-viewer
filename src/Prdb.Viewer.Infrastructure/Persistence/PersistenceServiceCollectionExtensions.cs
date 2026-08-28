using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Microsoft.AspNetCore.Identity;

using Prdb.Viewer.Infrastructure.Access;
using Prdb.Viewer.Infrastructure.Configuration;
using Prdb.Viewer.Infrastructure.Library;
using Prdb.Viewer.Infrastructure.Personal;

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
        services.AddScoped<WorkIssueRecorder>();
        services.AddScoped<VideoProjection>();
        services.AddSingleton<PlaybackPressureMonitor>();
        services.AddScoped<LibraryWorkScheduler>();
        services.AddScoped<BackgroundWorkOperations>();
        services.AddScoped<Recovery.BackupService>();
        services.AddScoped<LibraryScanRunner>();
        services.AddScoped<TechnicalInspectionRunner>();
        services.AddSingleton<DerivedArtifactStore>();
        services.AddScoped<HashingRunner>();
        services.AddScoped<PreviewGenerationRunner>();
        services.AddScoped<IdentificationRunner>();
        services.AddScoped<IdentificationService>();
        services.AddScoped<IdentificationReviewService>();
        services.AddScoped<PreviewDeliveryService>();
        services.AddScoped<BackgroundWorkQuery>();
        services.AddScoped<VideoCatalog>();
        services.AddScoped<LibraryDiscovery>();
        services.AddScoped<LibraryPreferences>();
        services.AddScoped<VideoDeliveryService>();
        services.AddScoped<PersonalStateService>();
        services.AddSingleton<IMediaProbe, FfprobeMediaProbe>();
        services.AddSingleton<IVideoFileHasher, PrdbVideoFileHasher>();
        services.AddSingleton<IPreviewImageGenerator, FfmpegPreviewImageGenerator>();
        services.AddScoped<IPrdbIdentificationClient, PrdbIdentificationClient>();
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
