namespace OWLProtect.Core;

public sealed record PlatformBootstrapSettings(
    string DefaultTenantId,
    string DefaultTenantName,
    string DefaultTenantRegion,
    bool SeedTestUserEnabled,
    int SessionRevalidationSeconds);

public interface IPlatformBootstrapSettingsProvider
{
    PlatformBootstrapSettings GetSettings();
}

public sealed record SeedDataset(
    Tenant DefaultTenant,
    IReadOnlyList<User> Users,
    IReadOnlyList<Device> Devices,
    IReadOnlyList<Gateway> Gateways,
    IReadOnlyList<GatewayPool> GatewayPools,
    IReadOnlyList<PolicyRule> Policies,
    IReadOnlyList<TunnelSession> Sessions,
    IReadOnlyList<HealthSample> HealthSamples,
    IReadOnlyList<Alert> Alerts,
    IReadOnlyList<AuthProviderConfig> AuthProviders,
    IReadOnlyList<AuditEvent> AuditEvents);
