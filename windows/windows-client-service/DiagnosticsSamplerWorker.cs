using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class DiagnosticsSamplerWorker(
    ILogger<DiagnosticsSamplerWorker> logger,
    ClientSessionState state) : BackgroundService
{
    private readonly Random _random = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = state.GetStatus();
            if (current.Connected)
            {
                var latency = 18 + _random.Next(0, 16);
                var jitter = 3 + _random.Next(0, 7);
                var signal = 78 + _random.Next(0, 18);
                var throughput = 145 + _random.Next(0, 60);
                var nextState = latency > 30 ? ConnectionState.HighJitter : ConnectionState.Healthy;

                state.UpdateDiagnostics(current with
                {
                    State = nextState,
                    UserMessage = nextState == ConnectionState.Healthy
                        ? "Tunnel healthy with stable link metrics."
                        : "Jitter is elevated. Calls and screen share may degrade.",
                    LatencyMs = latency,
                    JitterMs = jitter,
                    SignalStrengthPercent = signal,
                    ThroughputMbps = throughput
                });
            }

            logger.LogDebug("Client diagnostics sampled at {Time}.", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

