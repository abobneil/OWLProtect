namespace OWLProtect.WindowsClientService;

public sealed record ClientStatus(
    bool Connected,
    string DeviceName,
    string CurrentGateway,
    string State,
    string DiagnosticScope,
    string UserMessage,
    string DiagnosticDetail,
    IReadOnlyList<string> FailoverGateways,
    IReadOnlyList<string> Timeline,
    int LatencyMs,
    int JitterMs,
    int SignalStrengthPercent,
    int ThroughputMbps,
    DateTimeOffset UpdatedAtUtc);

public sealed record ConnectCommand(bool SilentSsoPreferred);
public sealed record PipeCommand(string Command, bool SilentSsoPreferred);
