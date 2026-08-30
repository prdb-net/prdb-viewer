using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Host.Configuration;
using Prdb.Viewer.Host.Development;
using Prdb.Viewer.Host.Library;
using Prdb.Viewer.Host.Personal;
using Prdb.Viewer.Infrastructure.Persistence;

var operatorCommand = OperatorCommands.Matches(args);
var seedCommand = SeedCommand.Matches(args);

// Every command line that is a command rather than a server start: the web host is built without
// the arguments, its logging is quietened, and the result of the command is the process output.
var command = operatorCommand || seedCommand;

// The OpenAPI document generator loads this application only to read its endpoints. It never
// prepares the database, so the background-work lanes must not start and report an unopenable one.
var readingEndpoints = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
var builder = WebApplication.CreateBuilder(command ? [] : args);

// An operator command's result is its output. Routine startup and query logging would bury it, so
// only warnings and worse reach the console.
if (command)
{
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
}

var dataDirectory = builder.Configuration["VIEWER_DATA_DIRECTORY"]
    ?? Path.Combine(AppContext.BaseDirectory, "data");

// The container mounts the library tree at the default, and nothing about a deployed installation
// needs to say so. A developer running the application from a working copy has no /libraries and
// cannot create one, which left the Library Directory screens unreachable outside a container.
var libraryMountRoot = builder.Configuration["VIEWER_LIBRARY_MOUNT_ROOT"]
    ?? Prdb.Viewer.Infrastructure.Configuration.LibraryMountRoot.DefaultPath;

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddViewerPersistence(dataDirectory, libraryMountRoot);
if (!readingEndpoints && builder.Configuration.GetValue("VIEWER_BACKGROUND_WORK_ENABLED", true))
{
    builder.Services.AddHostedService<LibraryScanWorker>();
    builder.Services.AddHostedService<TechnicalInspectionWorker>();
    builder.Services.AddHostedService<HashingWorker>();
    builder.Services.AddHostedService<PreviewGenerationWorker>();
    builder.Services.AddHostedService<IdentificationWorker>();
    builder.Services.AddHostedService<SiteRecognitionWorker>();
}
builder.Services
    .AddAuthentication(SessionAuthentication.Scheme)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthenticationHandler>(
        SessionAuthentication.Scheme,
        configureOptions: null);
builder.Services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());

// A TLS-terminating reverse proxy hides the client's scheme and address, which would otherwise
// drop the Secure flag from the session cookie and collapse anonymous rate limiting onto the
// proxy. Trusting those headers is only safe when nothing but the proxy can reach the container,
// so it stays off until an operator says so.
var behindReverseProxy = builder.Configuration.GetValue("VIEWER_BEHIND_REVERSE_PROXY", false);

if (behindReverseProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}
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
    app.Logger.LogInformation(
        "prdb-viewer {Version} ({Commit}) starting with data in {DataDirectory}.",
        Prdb.Viewer.Infrastructure.ProductBuild.Version,
        Prdb.Viewer.Infrastructure.ProductBuild.Commit,
        dataDirectory);

    try
    {
        await app.Services.PrepareViewerDatabaseAsync();
    }
    catch (DatabaseMigrationException)
    {
        return 1;
    }

    if (seedCommand)
    {
        return await SeedCommand.RunAsync(app.Services, Console.Out, Console.Error);
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

if (behindReverseProxy)
{
    app.UseForwardedHeaders();
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
