using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class ClientSessionState
{
    private readonly Lock _gate = new();
    private ClientStatus _status = new(
        Connected: false,
        DeviceName: Environment.MachineName,
        CurrentGateway: "unassigned",
        State: ConnectionState.AuthExpired,
        UserMessage: "Sign in with silent SSO or interactive login to establish the tunnel.",
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
            _status = _status with
            {
                Connected = true,
                CurrentGateway = "us-east-core-1",
                State = ConnectionState.Healthy,
                UserMessage = silentSsoPreferred
                    ? "Connected using managed-device silent SSO."
                    : "Connected after interactive sign-in.",
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
                State = ConnectionState.ServerUnavailable,
                UserMessage = "Tunnel disconnected.",
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
}

