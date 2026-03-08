using System.Text.Json.Serialization;

namespace OWLProtect.Core;

public enum AdminRole
{
    SuperAdmin,
    Operator,
    ReadOnly
}

public enum HealthSeverity
{
    Green,
    Yellow,
    Red
}

public enum ConnectionState
{
    Healthy,
    LocalNetworkPoor,
    LowBandwidth,
    HighJitter,
    GatewayDegraded,
    ServerUnavailable,
    AuthExpired,
    PolicyBlocked
}

public enum PlatformSessionKind
{
    Admin,
    User,
    Client
}

public sealed record User(
    string Id,
    string Username,
    string DisplayName,
    bool Enabled,
    bool TestAccount,
    string Provider,
    IReadOnlyList<string> GroupIds,
    IReadOnlyList<string> PolicyIds);

public sealed record AdminAccount(
    string Id,
    string Username,
    [property: JsonIgnore]
    string Password,
    AdminRole Role,
    bool MustChangePassword,
    bool MfaEnrolled);

public sealed record Device(
    string Id,
    string Name,
    string UserId,
    string City,
    string Country,
    string PublicIp,
    bool Managed,
    bool Compliant,
    int PostureScore,
    ConnectionState ConnectionState,
    DateTimeOffset LastSeenUtc);

public sealed record Gateway(
    string Id,
    string Name,
    string Region,
    HealthSeverity Health,
    int LoadPercent,
    int PeerCount,
    int CpuPercent,
    int MemoryPercent,
    int LatencyMs);

public sealed record GatewayPool(
    string Id,
    string Name,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> GatewayIds);

public sealed record PolicyRule(
    string Id,
    string Name,
    IReadOnlyList<string> Cidrs,
    IReadOnlyList<string> DnsZones,
    IReadOnlyList<int> Ports,
    string Mode);

public sealed record TunnelSession(
    string Id,
    string UserId,
    string DeviceId,
    string GatewayId,
    DateTimeOffset ConnectedAtUtc,
    int HandshakeAgeSeconds,
    int ThroughputMbps);

public sealed record HealthSample(
    string Id,
    string DeviceId,
    ConnectionState State,
    HealthSeverity Severity,
    int LatencyMs,
    int JitterMs,
    decimal PacketLossPercent,
    int ThroughputMbps,
    int SignalStrengthPercent,
    bool DnsReachable,
    bool RouteHealthy,
    DateTimeOffset SampledAtUtc,
    string Message);

public sealed record Alert(
    string Id,
    HealthSeverity Severity,
    string Title,
    string Description,
    string TargetType,
    string TargetId,
    DateTimeOffset CreatedAtUtc);

public sealed record PostureReport(
    string DeviceId,
    bool Managed,
    bool Compliant,
    bool BitLockerEnabled,
    bool DefenderHealthy,
    bool FirewallEnabled,
    bool SecureBootEnabled,
    bool TamperProtectionEnabled,
    string OsVersion);

public sealed record AuthProviderConfig(
    string Id,
    string Name,
    string Type,
    string Issuer,
    string ClientId,
    IReadOnlyList<string> MfaClaimPaths,
    bool SilentSsoEnabled);

public sealed record AuditEvent(
    string Id,
    string Actor,
    string Action,
    string TargetType,
    string TargetId,
    DateTimeOffset CreatedAtUtc,
    string Outcome,
    string Detail);

public sealed record DashboardSnapshot(
    IReadOnlyList<AdminAccount> Admins,
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

public sealed record ConnectionMapPoint(
    string DeviceId,
    string DeviceName,
    string City,
    string Country,
    string PublicIp,
    ConnectionState ConnectionState);

public sealed record BootstrapStatus(
    bool RequiresPasswordChange,
    bool RequiresMfaEnrollment,
    bool TestUserEnabled,
    DateTimeOffset? TestUserAutoDisableAtUtc);

public sealed record PlatformSession(
    string Id,
    PlatformSessionKind Kind,
    string SubjectId,
    string SubjectName,
    string? Role,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    DateTimeOffset LastAuthenticatedAtUtc,
    DateTimeOffset? StepUpExpiresAtUtc,
    DateTimeOffset? RevokedAtUtc);

public sealed record IssuedPlatformSession(
    PlatformSession Session,
    string AccessToken,
    string RefreshToken);
