using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class ClientSessionState
{
    private readonly Lock _gate = new();
    private static readonly GatewayPool LocalPool = new("pool-local", "Local Client Pool", ["us-east"], ["gw-1", "gw-2"]);
    private ClientStatus _status = new(
        Connected: false,
        DeviceName: Environment.MachineName,
        CurrentGateway: "unassigned",
        State: ConnectionState.AuthExpired.ToString(),
        DiagnosticScope: DiagnosticScope.Authentication.ToString(),
        UserMessage: "Sign in with silent SSO or interactive login to establish the tunnel.",
        DiagnosticDetail: "A client session has not been issued yet, so the tunnel cannot select a gateway.",
        FailoverGateways: [],
        Timeline:
        [
            $"{DateTimeOffset.UtcNow:HH:mm:ss} Waiting for user authentication before selecting a gateway."
        ],
        LatencyMs: 0,
        JitterMs: 0,
        SignalStrengthPercent: 0,
        ThroughputMbps: 0,
        UpdatedAtUtc: DateTimeOffset.UtcNow);

    public ClientStatus GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public ClientStatus Connect(bool silentSsoPreferred)
    {
        lock (_gate)
        {
            var gateways = CreateGatewayFleet(primaryDegraded: false);
            var placement = GatewayDiagnostics.SelectGatewayPlacement(SeedData.DefaultTenantId, gateways, [LocalPool]);
            var gatewayLookup = gateways.ToDictionary(gateway => gateway.Id, gateway => gateway.Name, StringComparer.Ordinal);
            _status = _status with
            {
                Connected = true,
                CurrentGateway = placement?.GatewayName ?? "unassigned",
                State = ConnectionState.Healthy.ToString(),
                DiagnosticScope = DiagnosticScope.Healthy.ToString(),
                UserMessage = silentSsoPreferred
                    ? "Connected using managed-device silent SSO."
                    : "Connected after interactive sign-in.",
                DiagnosticDetail = placement?.Summary ?? "No healthy gateway placement was returned for the client.",
                FailoverGateways = placement?.FailoverGatewayIds.Select(id => gatewayLookup.GetValueOrDefault(id, id)).ToArray() ?? [],
                Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Connected through {placement?.GatewayName ?? "no gateway"} ({(silentSsoPreferred ? "silent SSO" : "interactive sign-in")})."),
                LatencyMs = 22,
                JitterMs = 4,
                SignalStrengthPercent = 94,
                ThroughputMbps = 180,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            return _status;
        }
    }

    public ClientStatus Disconnect()
    {
        lock (_gate)
        {
            _status = _status with
            {
                Connected = false,
                CurrentGateway = "unassigned",
                State = ConnectionState.ServerUnavailable.ToString(),
                DiagnosticScope = DiagnosticScope.Authentication.ToString(),
                UserMessage = "Tunnel disconnected.",
                DiagnosticDetail = "Client routing is idle until the next session is established.",
                FailoverGateways = [],
                Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Tunnel disconnected by the user."),
                ThroughputMbps = 0,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            return _status;
        }
    }

    public void UpdateDiagnostics(ClientStatus nextStatus)
    {
        lock (_gate)
        {
            _status = nextStatus with { UpdatedAtUtc = DateTimeOffset.UtcNow };
        }
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

    private static string[] AppendTimeline(IReadOnlyList<string> existing, string entry) =>
        existing
            .Concat([entry])
            .TakeLast(5)
            .ToArray();
}
