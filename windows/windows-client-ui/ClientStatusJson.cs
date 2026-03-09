using System.Text.Json;

namespace OWLProtect.WindowsClientUi;

internal static class ClientStatusJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public static ClientStatus LoadFromFile(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<ClientStatus>(stream, SerializerOptions)
            ?? throw new InvalidOperationException($"Preview status payload at '{path}' was invalid.");
    }

    public static ClientStatus CreateUnavailableStatus() =>
        new(
            Connected: false,
            DeviceName: "unregistered",
            Username: "not signed in",
            DeviceId: "pending",
            CurrentGateway: "unassigned",
            State: "Disconnected",
            DiagnosticScope: "Authentication",
            UserMessage: "The Windows service is not reachable over the named pipe.",
            DiagnosticDetail: "Start the Windows client service to retrieve enrollment, gateway placement, and diagnostics.",
            AuthMode: "NotConnected",
            RecoveryState: "Idle",
            RegistrationState: "Pending",
            EnrollmentKind: "Bootstrap",
            PolicyBundleVersion: "unassigned",
            Routes: Array.Empty<string>(),
            DnsZones: Array.Empty<string>(),
            Ports: Array.Empty<int>(),
            FailoverGateways: Array.Empty<string>(),
            Timeline: ["Waiting for the service host to become available."],
            LatencyMs: 0,
            JitterMs: 0,
            SignalStrengthPercent: 0,
            ThroughputMbps: 0,
            AccessTokenExpiresAtUtc: null,
            RevalidateAfterUtc: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            LastErrorCode: "pipe_unreachable",
            LastSupportBundlePath: null,
            Posture: new ClientPostureStatus(
                Managed: false,
                Compliant: false,
                PostureScore: 0,
                BitLockerEnabled: false,
                DefenderHealthy: false,
                FirewallEnabled: false,
                SecureBootEnabled: false,
                TamperProtectionEnabled: false,
                ComplianceReasons: ["service_unreachable"],
                OperatingSystem: Environment.OSVersion.VersionString,
                CollectedAtUtc: null));
}
