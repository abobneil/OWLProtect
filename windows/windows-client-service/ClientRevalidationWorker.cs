namespace OWLProtect.WindowsClientService;

public sealed class ClientRevalidationWorker(
    ClientSessionState state,
    ILogger<ClientRevalidationWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RevalidationInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RevalidationInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var current = state.GetStatus();
                if (current.Connected || string.Equals(current.RecoveryState, "OfflineGrace", StringComparison.Ordinal))
                {
                    await state.RefreshAuthorizationAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Client authorization revalidation failed.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }
}
