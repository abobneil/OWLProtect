namespace OWLProtect.Core;

public static class SeedData
{
    public const string DefaultTenantId = "tenant-default";
    public static readonly PlatformBootstrapSettings DefaultBootstrapSettings = new(
        DefaultTenantId,
        "OWLProtect Default",
        "global",
        SeedTestUserEnabled: false,
        SessionRevalidationSeconds: 300);

    public static AdminAccount CreateDefaultAdmin(BootstrapAdminCredentials credentials) =>
        new(
            "admin-1",
            credentials.Username,
            credentials.PasswordHash,
            AdminRole.SuperAdmin,
            MustChangePassword: true,
            MfaEnrolled: false);

    public static SeedDataset CreateSeedDataset(PlatformBootstrapSettings? settings = null)
    {
        var bootstrap = settings ?? DefaultBootstrapSettings;
        var tenant = new Tenant(bootstrap.DefaultTenantId, bootstrap.DefaultTenantName, bootstrap.DefaultTenantRegion, IsDefault: true);
        var users = new[]
        {
            new User(
                "user-1",
                "user",
                "Default Test User",
                Enabled: bootstrap.SeedTestUserEnabled,
                TestAccount: true,
                Provider: "local",
                GroupIds: ["group-test"],
                PolicyIds: ["policy-test"],
                TenantId: tenant.Id),
            new User(
                "user-2",
                "maria.diaz",
                "Maria Diaz",
                Enabled: true,
                TestAccount: false,
                Provider: "entra",
                GroupIds: ["group-engineering", "entra:eng"],
                PolicyIds: ["policy-core"],
                TenantId: tenant.Id)
        };

        var devices = new[]
        {
            new Device(
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
                DateTimeOffset.Parse("2026-03-07T23:45:00Z"),
                TenantId: tenant.Id,
                RegistrationState: DeviceRegistrationState.Enrolled,
                EnrollmentKind: DeviceEnrollmentKind.ReEnrollment,
                HardwareKey: "hw-mariad-lt-14",
                SerialNumber: "MDLT14-2394",
                OperatingSystem: "Windows 11 24H2",
                RegisteredAtUtc: DateTimeOffset.Parse("2026-03-01T15:00:00Z"),
                LastEnrollmentAtUtc: DateTimeOffset.Parse("2026-03-07T22:55:00Z"),
                DisabledAtUtc: null,
                ComplianceReasons: []),
            new Device(
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
                DateTimeOffset.Parse("2026-03-07T23:41:00Z"),
                TenantId: tenant.Id,
                RegistrationState: DeviceRegistrationState.Pending,
                EnrollmentKind: DeviceEnrollmentKind.Bootstrap,
                HardwareKey: "hw-qa-lab-device",
                SerialNumber: "QALAB-9911",
                OperatingSystem: "Windows 11 24H2",
                RegisteredAtUtc: DateTimeOffset.Parse("2026-03-07T22:40:00Z"),
                LastEnrollmentAtUtc: DateTimeOffset.Parse("2026-03-07T22:40:00Z"),
                DisabledAtUtc: null,
                ComplianceReasons: ["firewall_disabled", "device_pending_approval"])
        };

        var gateways = new[]
        {
            new Gateway("gw-1", "us-east-core-1", "us-east", HealthSeverity.Green, 31, 124, 38, 54, 18, tenant.Id, DateTimeOffset.Parse("2026-03-07T23:45:00Z")),
            new Gateway("gw-2", "us-east-core-2", "us-east", HealthSeverity.Yellow, 72, 140, 70, 68, 42, tenant.Id, DateTimeOffset.Parse("2026-03-07T23:44:42Z"))
        };

        var gatewayPools = new[]
        {
            new GatewayPool("pool-1", "East Coast Pool", ["us-east"], ["gw-1", "gw-2"], tenant.Id)
        };

        var policies = new[]
        {
            new PolicyRule(
                "policy-test",
                "Default Test Policy",
                ["10.10.20.0/24"],
                ["test.owlprotect.local"],
                [443, 8443],
                "split-tunnel",
                tenant.Id,
                Priority: 50,
                TargetGroupIds: ["group-test"],
                RequireManaged: true,
                RequireCompliant: false,
                MinimumPostureScore: 40,
                AllowedDeviceStates: [DeviceRegistrationState.Pending, DeviceRegistrationState.Enrolled]),
            new PolicyRule(
                "policy-core",
                "Core Enterprise Access",
                ["10.0.0.0/8", "172.16.20.0/24"],
                ["corp.owlprotect.local", "eng.owlprotect.local"],
                [53, 80, 443, 3389],
                "split-tunnel",
                tenant.Id,
                Priority: 100,
                TargetGroupIds: ["group-engineering", "entra:eng"],
                RequireManaged: true,
                RequireCompliant: true,
                MinimumPostureScore: 80,
                AllowedDeviceStates: [DeviceRegistrationState.Enrolled])
        };

        var sessions = new[]
        {
            new TunnelSession(
                "session-1",
                "user-2",
                "device-1",
                "gw-1",
                DateTimeOffset.Parse("2026-03-07T22:59:00Z"),
                21,
                188,
                tenant.Id,
                PolicyBundleVersion: "seed-policy-core-v1",
                AuthorizedAtUtc: DateTimeOffset.Parse("2026-03-07T22:59:00Z"),
                RevalidateAfterUtc: DateTimeOffset.Parse("2026-03-07T23:04:00Z"))
        };

        var healthSamples = new[]
        {
            new HealthSample("health-1", "device-1", ConnectionState.Healthy, HealthSeverity.Green, 18, 4, 0.1m, 188, 91, true, true, DateTimeOffset.Parse("2026-03-07T23:45:00Z"), "Tunnel healthy with low jitter and strong signal.", tenant.Id),
            new HealthSample("health-2", "device-2", ConnectionState.PolicyBlocked, HealthSeverity.Red, 0, 0, 0m, 0, 72, false, false, DateTimeOffset.Parse("2026-03-07T23:41:00Z"), "Device posture is noncompliant, so enterprise routes remain blocked.", tenant.Id)
        };

        var alerts = new[]
        {
            new Alert("alert-1", HealthSeverity.Red, "Test account disabled", "The seeded test user remains disabled until an admin explicitly enables it.", "device", "device-2", DateTimeOffset.Parse("2026-03-07T23:00:00Z"), tenant.Id),
            new Alert("alert-2", HealthSeverity.Yellow, "Gateway load rising", "Gateway us-east-core-2 is above the yellow load threshold.", "gateway", "gw-2", DateTimeOffset.Parse("2026-03-07T23:39:00Z"), tenant.Id)
        };

        var authProviders = new[]
        {
            new AuthProviderConfig(
                "auth-1",
                "Microsoft Entra ID",
                "entra",
                "https://login.microsoftonline.com/example/v2.0",
                "entra-client-id",
                ["preferred_username", "upn", "email", "sub"],
                ["groups"],
                ["amr", "acr"],
                RequireMfa: true,
                SilentSsoEnabled: true,
                TenantId: tenant.Id),
            new AuthProviderConfig(
                "auth-2",
                "Generic OIDC",
                "oidc",
                "https://identity.example.com",
                "oidc-client-id",
                ["preferred_username", "email", "sub"],
                ["groups"],
                ["amr"],
                RequireMfa: true,
                SilentSsoEnabled: false,
                TenantId: tenant.Id)
        };

        var auditEvents = AuditChain.CreateSeedChain(
        [
            new AuditEventDraft("audit-1", "system", "seed-default-admin", "admin", "admin-1", DateTimeOffset.Parse("2026-03-07T22:00:00Z"), "success", "Seeded default admin with forced password reset.", tenant.Id),
            new AuditEventDraft("audit-2", "system", "seed-test-user", "user", "user-1", DateTimeOffset.Parse("2026-03-07T22:00:00Z"), "success", "Seeded disabled test user with restricted default policy.", tenant.Id)
        ]);

        return new SeedDataset(
            tenant,
            users,
            devices,
            gateways,
            gatewayPools,
            policies,
            sessions,
            healthSamples,
            alerts,
            authProviders,
            auditEvents);
    }

    public static readonly SeedDataset DefaultSeedDataset = CreateSeedDataset();
    public static readonly Tenant DefaultTenant = DefaultSeedDataset.DefaultTenant;
    public static readonly User DefaultUser =
        DefaultSeedDataset.Users[0];

    public static readonly IReadOnlyList<User> Users = DefaultSeedDataset.Users;
    public static readonly IReadOnlyList<Device> Devices = DefaultSeedDataset.Devices;
    public static readonly IReadOnlyList<Gateway> Gateways = DefaultSeedDataset.Gateways;
    public static readonly IReadOnlyList<GatewayPool> GatewayPools = DefaultSeedDataset.GatewayPools;
    public static readonly IReadOnlyList<PolicyRule> Policies = DefaultSeedDataset.Policies;
    public static readonly IReadOnlyList<TunnelSession> Sessions = DefaultSeedDataset.Sessions;
    public static readonly IReadOnlyList<HealthSample> HealthSamples = DefaultSeedDataset.HealthSamples;
    public static readonly IReadOnlyList<Alert> Alerts = DefaultSeedDataset.Alerts;
    public static readonly IReadOnlyList<AuthProviderConfig> AuthProviders = DefaultSeedDataset.AuthProviders;
    public static readonly IReadOnlyList<AuditEvent> AuditEvents = DefaultSeedDataset.AuditEvents;
}
