using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Host.Configuration;
using Prdb.Viewer.Host.Library;
using Prdb.Viewer.Host.Personal;
using Prdb.Viewer.Infrastructure.Persistence;

var operatorCommand = OperatorCommands.Matches(args);

// The OpenAPI document generator loads this application only to read its endpoints. It never
// prepares the database, so the background-work lanes must not start and report an unopenable one.
var readingEndpoints = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
var builder = WebApplication.CreateBuilder(operatorCommand ? [] : args);

var dataDirectory = builder.Configuration["VIEWER_DATA_DIRECTORY"]
    ?? Path.Combine(AppContext.BaseDirectory, "data");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddViewerPersistence(dataDirectory);
if (!readingEndpoints && builder.Configuration.GetValue("VIEWER_BACKGROUND_WORK_ENABLED", true))
{
    builder.Services.AddHostedService<LibraryScanWorker>();
    builder.Services.AddHostedService<TechnicalInspectionWorker>();
    builder.Services.AddHostedService<HashingWorker>();
    builder.Services.AddHostedService<PreviewGenerationWorker>();
    builder.Services.AddHostedService<IdentificationWorker>();
}
builder.Services
    .AddAuthentication(SessionAuthentication.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthentication.Scheme,
        configureOptions: null);
builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("anonymous-access", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
});
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info = new OpenApiInfo
    {
        Title = "prdb-viewer",
        Version = "v1",
        Description = "The versioned API used by the prdb-viewer browser application.",
    };

    return Task.CompletedTask;
}));

var app = builder.Build();

if (!readingEndpoints)
{
    var version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown";
    var commit = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .SingleOrDefault(attribute => attribute.Key == "Commit")
        ?.Value ?? "unknown";

    app.Logger.LogInformation(
        "prdb-viewer {Version} ({Commit}) starting with data in {DataDirectory}.",
        version,
        commit,
        dataDirectory);

    try
    {
        await app.Services.PrepareViewerDatabaseAsync();
    }
    catch (DatabaseMigrationException)
    {
        return 1;
    }

    if (operatorCommand)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await OperatorCommands.RunAsync(
            args,
            scope.ServiceProvider.GetRequiredService<Prdb.Viewer.Infrastructure.Access.AccessService>(),
            scope.ServiceProvider.GetRequiredService<Prdb.Viewer.Infrastructure.Recovery.BackupService>(),
            Console.Out,
            Console.Error);
    }
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .WithTags("Health")
    .AllowAnonymous();

app.MapAccess();
app.MapConfiguration();
app.MapBackgroundWork();
app.MapIdentification();
app.MapVideos();
app.MapPersonalState();

app.MapFallback("/api/{*rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

return 0;

internal sealed record HealthResponse(string Status);

public partial class Program;
