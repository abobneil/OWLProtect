using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class TestUserDisableWorker(
    IServiceProvider serviceProvider,
    ILogger<TestUserDisableWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCycleAsync(stoppingToken);

        using var timer = new PeriodicTimer(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var bootstrapService = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            if (bootstrapService.DisableExpiredTestUser())
            {
                logger.LogInformation("Auto-disabled the seeded test user after the one-hour window expired.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Seeded test-user disable cycle failed.");
        }

        await Task.CompletedTask;
    }
}
