using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class DiagnosticsSamplerWorker(
    ILogger<DiagnosticsSamplerWorker> logger,
    ClientSessionState state) : BackgroundService
{
    private readonly Random _random = new();
    private int _tick;
    private static readonly GatewayPool LocalPool = new("pool-local", "Local Client Pool", ["us-east"], ["gw-1", "gw-2"]);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = state.GetStatus();
            if (current.Connected)
            {
                _tick++;

                var primaryDegraded = _tick % 4 == 0;
                var localNetworkPoor = _tick % 6 == 0;
                var gateways = CreateGatewayFleet(primaryDegraded);
                var gatewayLookup = gateways.ToDictionary(gateway => gateway.Id, gateway => gateway.Name, StringComparer.Ordinal);
                var placement = GatewayDiagnostics.SelectGatewayPlacement(SeedData.DefaultTenantId, gateways, [LocalPool])!;
                var failoverGateways = placement.FailoverGatewayIds.Select(id => gatewayLookup.GetValueOrDefault(id, id)).ToArray();

                var sample = BuildSample(localNetworkPoor, primaryDegraded);
                var simulatedState = localNetworkPoor
                    ? ConnectionState.LocalNetworkPoor
                    : primaryDegraded
                        ? ConnectionState.GatewayDegraded
                        : ConnectionState.Healthy;
                var activeGateway = gateways.Single(gateway => string.Equals(gateway.Id, placement.GatewayId, StringComparison.Ordinal));
                var diagnostics = GatewayDiagnostics.ClassifyDevice(
                    new Device(
                        current.DeviceId,
                        current.DeviceName,
                        "user-local",
                        "Unknown",
                        "Unknown",
                        "127.0.0.1",
                        current.Posture.Managed,
                        current.Posture.Compliant,
                        current.Posture.PostureScore,
                        simulatedState,
                        sample.SampledAtUtc,
                        RegistrationState: Enum.Parse<DeviceRegistrationState>(current.RegistrationState, ignoreCase: true),
                        EnrollmentKind: Enum.Parse<DeviceEnrollmentKind>(current.EnrollmentKind, ignoreCase: true)),
                    sample,
                    new TunnelSession(
                        "session-local",
                        "user-local",
                        current.DeviceId,
                        placement.GatewayId,
                        DateTimeOffset.UtcNow,
                        8,
                        sample.ThroughputMbps),
                    activeGateway);

                var nextTimeline = current.Timeline;
                if (!string.Equals(current.CurrentGateway, placement.GatewayName, StringComparison.Ordinal))
                {
                    nextTimeline = AppendTimeline(nextTimeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Failed over to {placement.GatewayName} after {current.CurrentGateway} crossed gateway thresholds.");
                }
                else if (localNetworkPoor)
                {
                    nextTimeline = AppendTimeline(nextTimeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Local network warning detected before traffic reached the gateway.");
                }

                state.UpdateDiagnostics(current with
                {
                    CurrentGateway = placement.GatewayName,
                    State = diagnostics.State.ToString(),
                    DiagnosticScope = diagnostics.Scope.ToString(),
                    UserMessage = diagnostics.Summary,
                    DiagnosticDetail = diagnostics.Detail,
                    FailoverGateways = failoverGateways,
                    Timeline = nextTimeline,
                    LatencyMs = sample.LatencyMs,
                    JitterMs = sample.JitterMs,
                    SignalStrengthPercent = sample.SignalStrengthPercent,
                    ThroughputMbps = sample.ThroughputMbps
                });
            }

            logger.LogDebug("Client diagnostics sampled at {Time}.", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private HealthSample BuildSample(bool localNetworkPoor, bool primaryDegraded)
    {
        if (localNetworkPoor)
        {
            return new HealthSample(
                Guid.NewGuid().ToString("n"),
                "device-local",
                ConnectionState.LocalNetworkPoor,
                HealthSeverity.Yellow,
                54,
                22,
                2.4m,
                62,
                49,
                false,
                false,
                DateTimeOffset.UtcNow,
                "Packet loss and weak signal indicate a local uplink issue.");
        }

        if (primaryDegraded)
        {
            return new HealthSample(
                Guid.NewGuid().ToString("n"),
                "device-local",
                ConnectionState.GatewayDegraded,
                HealthSeverity.Yellow,
                81,
                18,
                0.4m,
                118,
                88,
                true,
                true,
                DateTimeOffset.UtcNow,
                "Gateway latency breached the failover threshold.");
        }

        return new HealthSample(
            Guid.NewGuid().ToString("n"),
            "device-local",
            ConnectionState.Healthy,
            HealthSeverity.Green,
            18 + _random.Next(0, 10),
            3 + _random.Next(0, 4),
            0.1m,
            145 + _random.Next(0, 45),
            82 + _random.Next(0, 12),
            true,
            true,
            DateTimeOffset.UtcNow,
            "Tunnel healthy with stable link metrics.");
    }

    private static Gateway[] CreateGatewayFleet(bool primaryDegraded)
    {
        var now = DateTimeOffset.UtcNow;
        var primary = primaryDegraded
            ? new Gateway("gw-1", "us-east-core-1", "us-east", HealthSeverity.Red, 88, 152, 91, 86, 92, LastHeartbeatUtc: now)
            : new Gateway("gw-1", "us-east-core-1", "us-east", HealthSeverity.Green, 34, 128, 41, 49, 20, LastHeartbeatUtc: now);
        var secondary = primaryDegraded
            ? new Gateway("gw-2", "us-east-core-2", "us-east", HealthSeverity.Green, 39, 111, 37, 46, 24, LastHeartbeatUtc: now)
            : new Gateway("gw-2", "us-east-core-2", "us-east", HealthSeverity.Yellow, 62, 118, 58, 64, 39, LastHeartbeatUtc: now);
        return [primary, secondary];
    }

    private static IReadOnlyList<string> AppendTimeline(IReadOnlyList<string> existing, string entry) =>
        existing
            .Concat([entry])
            .TakeLast(6)
            .ToArray();
}
