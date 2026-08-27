using System.Reflection;

using Microsoft.OpenApi;

using Prdb.Viewer.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var dataDirectory = builder.Configuration["VIEWER_DATA_DIRECTORY"]
    ?? Path.Combine(AppContext.BaseDirectory, "data");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddViewerPersistence(dataDirectory);
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

var readingEndpoints = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

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
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .WithTags("Health");

app.MapFallback("/api/{*rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();

return 0;

internal sealed record HealthResponse(string Status);

public partial class Program;
