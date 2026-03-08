using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed record ClientStatus(
    bool Connected,
    string DeviceName,
    string CurrentGateway,
    ConnectionState State,
    string UserMessage,
    int LatencyMs,
    int JitterMs,
    int SignalStrengthPercent,
    int ThroughputMbps,
    DateTimeOffset UpdatedAtUtc);

public sealed record ConnectCommand(bool SilentSsoPreferred);
public sealed record PipeCommand(string Command, bool SilentSsoPreferred);

