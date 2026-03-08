namespace OWLProtect.WindowsClientUi;

public sealed record PipeCommand(string Command, bool SilentSsoPreferred);

public sealed record ClientStatus(
    bool Connected,
    string DeviceName,
    string CurrentGateway,
    string State,
    string UserMessage,
    int LatencyMs,
    int JitterMs,
    int SignalStrengthPercent,
    int ThroughputMbps,
    DateTimeOffset UpdatedAtUtc);
