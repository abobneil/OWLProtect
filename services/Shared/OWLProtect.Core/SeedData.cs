namespace OWLProtect.Core;

public static class SeedData
{
    public static AdminAccount CreateDefaultAdmin(BootstrapAdminCredentials credentials) =>
        new(
            "admin-1",
            credentials.Username,
            credentials.PasswordHash,
            AdminRole.SuperAdmin,
            MustChangePassword: true,
            MfaEnrolled: false);

    public static readonly User DefaultUser =
        new(
            "user-1",
            "user",
            "Default Test User",
            Enabled: false,
            TestAccount: true,
            Provider: "local",
            GroupIds: ["group-test"],
            PolicyIds: ["policy-test"]);

    public static readonly IReadOnlyList<User> Users =
    [
        DefaultUser,
        new(
            "user-2",
            "maria.diaz",
            "Maria Diaz",
            Enabled: true,
            TestAccount: false,
            Provider: "entra",
            GroupIds: ["group-engineering"],
            PolicyIds: ["policy-core"])
    ];

    public static readonly IReadOnlyList<Device> Devices =
    [
        new(
            "device-1",
            "MARIAD-LT-14",
            "user-2",
            "New York",
            "United States",
            "203.0.113.10",
            Managed: true,
            Compliant: true,
            PostureScore: 96,
            ConnectionState.Healthy,
            DateTimeOffset.Parse("2026-03-07T23:45:00Z")),
        new(
            "device-2",
            "QA-LAB-DEVICE",
            "user-1",
            "Austin",
            "United States",
            "203.0.113.22",
            Managed: true,
            Compliant: false,
            PostureScore: 58,
            ConnectionState.PolicyBlocked,
            DateTimeOffset.Parse("2026-03-07T23:41:00Z"))
    ];

    public static readonly IReadOnlyList<Gateway> Gateways =
    [
        new("gw-1", "us-east-core-1", "us-east", HealthSeverity.Green, 31, 124, 38, 54, 18),
        new("gw-2", "us-east-core-2", "us-east", HealthSeverity.Yellow, 72, 140, 70, 68, 42)
    ];

    public static readonly IReadOnlyList<GatewayPool> GatewayPools =
    [
        new("pool-1", "East Coast Pool", ["us-east"], ["gw-1", "gw-2"])
    ];

    public static readonly IReadOnlyList<PolicyRule> Policies =
    [
        new("policy-test", "Default Test Policy", ["10.10.20.0/24"], ["test.owlprotect.local"], [443, 8443], "split-tunnel"),
        new("policy-core", "Core Enterprise Access", ["10.0.0.0/8", "172.16.20.0/24"], ["corp.owlprotect.local", "eng.owlprotect.local"], [53, 80, 443, 3389], "split-tunnel")
    ];

    public static readonly IReadOnlyList<TunnelSession> Sessions =
    [
        new("session-1", "user-2", "device-1", "gw-1", DateTimeOffset.Parse("2026-03-07T22:59:00Z"), 21, 188)
    ];

    public static readonly IReadOnlyList<HealthSample> HealthSamples =
    [
        new("health-1", "device-1", ConnectionState.Healthy, HealthSeverity.Green, 18, 4, 0.1m, 188, 91, true, true, DateTimeOffset.Parse("2026-03-07T23:45:00Z"), "Tunnel healthy with low jitter and strong signal."),
        new("health-2", "device-2", ConnectionState.PolicyBlocked, HealthSeverity.Red, 0, 0, 0m, 0, 72, false, false, DateTimeOffset.Parse("2026-03-07T23:41:00Z"), "Device posture is noncompliant, so enterprise routes remain blocked.")
    ];

    public static readonly IReadOnlyList<Alert> Alerts =
    [
        new("alert-1", HealthSeverity.Red, "Test account disabled", "The seeded test user remains disabled until an admin explicitly enables it.", "device", "device-2", DateTimeOffset.Parse("2026-03-07T23:00:00Z")),
        new("alert-2", HealthSeverity.Yellow, "Gateway load rising", "Gateway us-east-core-2 is above the yellow load threshold.", "gateway", "gw-2", DateTimeOffset.Parse("2026-03-07T23:39:00Z"))
    ];

    public static readonly IReadOnlyList<AuthProviderConfig> AuthProviders =
    [
        new("auth-1", "Microsoft Entra ID", "entra", "https://login.microsoftonline.com/example/v2.0", "entra-client-id", ["amr", "acr"], true),
        new("auth-2", "Generic OIDC", "oidc", "https://identity.example.com", "oidc-client-id", ["amr"], false)
    ];

    public static readonly IReadOnlyList<AuditEvent> AuditEvents = AuditChain.CreateSeedChain(
    [
        new AuditEventDraft("audit-1", "system", "seed-default-admin", "admin", "admin-1", DateTimeOffset.Parse("2026-03-07T22:00:00Z"), "success", "Seeded default admin with forced password reset."),
        new AuditEventDraft("audit-2", "system", "seed-test-user", "user", "user-1", DateTimeOffset.Parse("2026-03-07T22:00:00Z"), "success", "Seeded disabled test user with restricted default policy.")
    ]);
}
