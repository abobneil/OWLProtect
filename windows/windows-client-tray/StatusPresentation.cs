namespace OWLProtect.WindowsClientTray;

internal static class StatusPresentation
{
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

    public static string ToTooltip(ClientStatus status)
    {
        var summary = status.State switch
        {
            "Healthy" => "Connected",
            "ApprovalPending" => "Pending approval",
            "AdminDisconnected" => "Disconnected by admin",
            "LocalNetworkPoor" or "LowBandwidth" or "HighJitter" or "GatewayDegraded" => "Degraded",
            "AuthExpired" => "Reauthentication required",
            _ => "Disconnected"
        };

        var tooltip = $"OWLProtect Client - {summary}";
        return tooltip.Length > 63 ? tooltip[..63] : tooltip;
    }
}
