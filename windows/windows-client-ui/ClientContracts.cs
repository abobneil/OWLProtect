namespace OWLProtect.WindowsClientUi;

public sealed record PipeCommand(string Command, bool SilentSsoPreferred);

public sealed record ClientStatus(
    bool Connected,
    string DeviceName,
    string CurrentGateway,
    string State,
    string DiagnosticScope,
    string UserMessage,
    string DiagnosticDetail,
    string[] FailoverGateways,
    string[] Timeline,
    int LatencyMs,
    int JitterMs,
    int SignalStrengthPercent,
    int ThroughputMbps,
    DateTimeOffset UpdatedAtUtc);
