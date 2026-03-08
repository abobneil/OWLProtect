namespace OWLProtect.WindowsClientUi;

public static class PipeProtocol
{
    public const int Version = 1;

    public const string StatusCommand = "status";
    public const string ConnectCommand = "connect";
    public const string DisconnectCommand = "disconnect";
    public const string SignOutCommand = "sign-out";
    public const string SupportBundleCommand = "support-bundle";
}

public sealed record PipeRequest(
    int ProtocolVersion,
    string RequestId,
    string Command,
    bool SilentSsoPreferred = true);

public sealed record PipeResponse(
    int ProtocolVersion,
    string RequestId,
    bool Success,
    ClientStatus Status,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? ExportPath = null);

public sealed record ClientPostureStatus(
    bool Managed,
    bool Compliant,
    int PostureScore,
    bool BitLockerEnabled,
    bool DefenderHealthy,
    bool FirewallEnabled,
    bool SecureBootEnabled,
    bool TamperProtectionEnabled,
    IReadOnlyList<string> ComplianceReasons,
    string OperatingSystem,
    DateTimeOffset? CollectedAtUtc);

public sealed record ClientStatus(
    bool Connected,
    string DeviceName,
    string Username,
    string DeviceId,
    string CurrentGateway,
    string State,
    string DiagnosticScope,
    string UserMessage,
    string DiagnosticDetail,
    string AuthMode,
    string RecoveryState,
    string RegistrationState,
    string EnrollmentKind,
    string PolicyBundleVersion,
    IReadOnlyList<string> Routes,
    IReadOnlyList<string> DnsZones,
    IReadOnlyList<int> Ports,
    IReadOnlyList<string> FailoverGateways,
    IReadOnlyList<string> Timeline,
    int LatencyMs,
    int JitterMs,
    int SignalStrengthPercent,
    int ThroughputMbps,
    DateTimeOffset? AccessTokenExpiresAtUtc,
    DateTimeOffset? RevalidateAfterUtc,
    DateTimeOffset UpdatedAtUtc,
    string? LastErrorCode,
    string? LastSupportBundlePath,
    ClientPostureStatus Posture);
