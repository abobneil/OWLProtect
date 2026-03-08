using System.Diagnostics;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class TestUserDisableWorker(
    IServiceProvider serviceProvider,
    TestUserDisableWorkerState state,
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
        var startedAtUtc = DateTimeOffset.UtcNow;
        var start = Stopwatch.GetTimestamp();
        state.RecordStart(startedAtUtc);

        try
        {
            using var scope = serviceProvider.CreateScope();
            var bootstrapService = scope.ServiceProvider.GetRequiredService<IBootstrapService>();
            var disabled = bootstrapService.DisableExpiredTestUser();
            if (disabled)
            {
                logger.LogInformation("Auto-disabled the seeded test user after the one-hour window expired.");
            }
            state.RecordSuccess(
                DateTimeOffset.UtcNow,
                Stopwatch.GetElapsedTime(start),
                disabled ? "The seeded test user was auto-disabled." : "No seeded test-user action was required.",
                disabled ? 1 : 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            state.RecordFailure(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start), exception.Message);
            logger.LogWarning(exception, "Seeded test-user disable cycle failed.");
        }

        await Task.CompletedTask;
    }
}
