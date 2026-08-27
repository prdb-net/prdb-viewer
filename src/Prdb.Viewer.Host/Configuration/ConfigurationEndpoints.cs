using Prdb.Viewer.Core.Access;
using Prdb.Viewer.Host.Access;
using Prdb.Viewer.Infrastructure.Configuration;

namespace Prdb.Viewer.Host.Configuration;

public static class ConfigurationEndpoints
{
    public static void MapConfiguration(this IEndpointRouteBuilder routes)
    {
        var configuration = routes.MapGroup("/api/admin/configuration")
            .WithTags("Configuration")
            .RequireAuthorization(policy =>
                policy.RequireRole(AccountAuthority.Administrator.ToString()));

        configuration.MapGet("/", async (
            InstallationConfigurationService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.GetAsync(cancellationToken)));

        configuration.MapPost("/prdb-connection", async (
            PrdbCredentialRequest request,
            InstallationConfigurationService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.VerifyCredentialAsync(
                request.Credential ?? string.Empty,
                cancellationToken)))
            .RequireCsrf();

        configuration.MapPost("/prdb-connection/retry", async (
            InstallationConfigurationService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.RetryCredentialAsync(cancellationToken)))
            .RequireCsrf();

        configuration.MapGet("/library-directory-candidates", (
            InstallationConfigurationService service) =>
            TypedResults.Ok(new LibraryDirectoryCandidatesResponse(
                service.DiscoverLibraryDirectories())));

        configuration.MapPost("/library-directories/stages", async (
            LibraryDirectoryStageRequest request,
            InstallationConfigurationService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.StageLibraryDirectoryAsync(
                request.Name ?? string.Empty,
                request.ContainerPath ?? string.Empty,
                cancellationToken)))
            .RequireCsrf();

        configuration.MapPost("/library-directories/stages/{stageId:guid}/activate", async (
            Guid stageId,
            InstallationConfigurationService service,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await service.ActivateLibraryDirectoryAsync(
                stageId,
                cancellationToken)))
            .RequireCsrf();
    }
}

public sealed record PrdbCredentialRequest(string? Credential);

public sealed record LibraryDirectoryStageRequest(string? Name, string? ContainerPath);

public sealed record LibraryDirectoryCandidatesResponse(IReadOnlyList<string> ContainerPaths);
