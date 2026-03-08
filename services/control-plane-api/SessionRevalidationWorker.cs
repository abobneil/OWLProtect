using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class SessionRevalidationWorker(IServiceProvider serviceProvider, ILogger<SessionRevalidationWorker> logger, IPlatformBootstrapSettingsProvider bootstrapSettingsProvider) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(bootstrapSettingsProvider.GetSettings().SessionRevalidationSeconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = serviceProvider.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SessionRevalidationService>();
                var revalidated = service.RevalidateActiveSessions("scheduler");
                if (revalidated > 0)
                {
                    logger.LogInformation("Revalidated {SessionCount} active tunnel session(s).", revalidated);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to revalidate active sessions.");
            }
        }
    }
}
