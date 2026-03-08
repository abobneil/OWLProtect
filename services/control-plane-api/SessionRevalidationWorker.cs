using System.Diagnostics;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class SessionRevalidationWorker(IServiceProvider serviceProvider, ILogger<SessionRevalidationWorker> logger, IPlatformBootstrapSettingsProvider bootstrapSettingsProvider, SessionRevalidationWorkerState state) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(bootstrapSettingsProvider.GetSettings().SessionRevalidationSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken stoppingToken)
    {
        var startedAtUtc = DateTimeOffset.UtcNow;
        var start = Stopwatch.GetTimestamp();
        state.RecordStart(startedAtUtc);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<SessionRevalidationService>();
            var revalidated = service.RevalidateActiveSessions("scheduler");
            state.RecordSuccess(
                DateTimeOffset.UtcNow,
                Stopwatch.GetElapsedTime(start),
                $"Revalidated {revalidated} active tunnel session(s).",
                revalidated);
            if (revalidated > 0)
            {
                logger.LogInformation("Revalidated {SessionCount} active tunnel session(s).", revalidated);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            state.RecordFailure(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start), exception.Message);
            logger.LogError(exception, "Failed to revalidate active sessions.");
        }

        await Task.CompletedTask;
    }
}
