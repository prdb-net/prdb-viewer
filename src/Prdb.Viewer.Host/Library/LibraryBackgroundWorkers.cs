using Prdb.Viewer.Infrastructure.Library;

namespace Prdb.Viewer.Host.Library;

public sealed class LibraryScanWorker(
    IServiceScopeFactory scopes,
    ILogger<LibraryScanWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        WorkerLoop.RunAsync<LibraryScanRunner>(
            scopes,
            logger,
            (runner, cancellationToken) => runner.RunNextSliceAsync(cancellationToken),
            stoppingToken);
}

public sealed class TechnicalInspectionWorker(
    IServiceScopeFactory scopes,
    ILogger<TechnicalInspectionWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        WorkerLoop.RunAsync<TechnicalInspectionRunner>(
            scopes,
            logger,
            (runner, cancellationToken) => runner.RunNextSliceAsync(cancellationToken),
            stoppingToken);
}

public sealed class HashingWorker(
    IServiceScopeFactory scopes,
    ILogger<HashingWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        WorkerLoop.RunAsync<HashingRunner>(
            scopes,
            logger,
            (runner, cancellationToken) => runner.RunNextSliceAsync(cancellationToken),
            stoppingToken);
}

public sealed class PreviewGenerationWorker(
    IServiceScopeFactory scopes,
    ILogger<PreviewGenerationWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        WorkerLoop.RunAsync<PreviewGenerationRunner>(
            scopes,
            logger,
            (runner, cancellationToken) => runner.RunNextSliceAsync(cancellationToken),
            stoppingToken);
}

public sealed class IdentificationWorker(
    IServiceScopeFactory scopes,
    ILogger<IdentificationWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        WorkerLoop.RunAsync<IdentificationRunner>(
            scopes,
            logger,
            (runner, cancellationToken) => runner.RunNextSliceAsync(cancellationToken),
            stoppingToken);
}

public sealed class SiteRecognitionWorker(
    IServiceScopeFactory scopes,
    ILogger<SiteRecognitionWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        WorkerLoop.RunAsync<SiteRecognitionRunner>(
            scopes,
            logger,
            (runner, cancellationToken) => runner.RunNextSliceAsync(cancellationToken),
            stoppingToken);
}

public sealed class EnrichmentWorker(
    IServiceScopeFactory scopes,
    ILogger<EnrichmentWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        WorkerLoop.RunAsync<EnrichmentRunner>(
            scopes,
            logger,
            (runner, cancellationToken) => runner.RunNextSliceAsync(cancellationToken),
            stoppingToken);
}

internal static class WorkerLoop
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The pause a lane takes between slices while a Video is playing. Throughput drops but no
    /// committed result is lost, which is the promise interactive use is given.
    /// </summary>
    private static readonly TimeSpan PlaybackDelay = TimeSpan.FromSeconds(2);

    public static async Task RunAsync<TRunner>(
        IServiceScopeFactory scopes,
        ILogger logger,
        Func<TRunner, CancellationToken, Task<bool>> run,
        CancellationToken stoppingToken)
        where TRunner : notnull
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var handled = await run(
                    scope.ServiceProvider.GetRequiredService<TRunner>(),
                    stoppingToken);

                if (!handled)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                }
                else if (scope.ServiceProvider
                    .GetRequiredService<PlaybackPressureMonitor>()
                    .PlaybackIsActive)
                {
                    await Task.Delay(PlaybackDelay, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "A background-work lane could not advance its next slice.");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }
}
