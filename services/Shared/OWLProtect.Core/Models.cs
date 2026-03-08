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

public enum DeviceRegistrationState
{
    Pending,
    Enrolled,
    Disabled,
    Revoked
}

public enum DeviceEnrollmentKind
{
    Bootstrap,
    ReEnrollment,
    Recovery,
    Reconciliation
}

public enum MachineTrustSubjectKind
{
    Gateway,
    Device
}

public sealed record Tenant(
    string Id,
    string Name,
    string Region,
    bool IsDefault);

public sealed record User(
    string Id,
    string Username,
    string DisplayName,
    bool Enabled,
    bool TestAccount,
    string Provider,
    IReadOnlyList<string> GroupIds,
    IReadOnlyList<string> PolicyIds,
    string TenantId = SeedData.DefaultTenantId);

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
    DateTimeOffset LastSeenUtc,
    string TenantId = SeedData.DefaultTenantId,
    DeviceRegistrationState RegistrationState = DeviceRegistrationState.Pending,
    DeviceEnrollmentKind EnrollmentKind = DeviceEnrollmentKind.Bootstrap,
    string HardwareKey = "",
    string SerialNumber = "",
    string OperatingSystem = "",
    DateTimeOffset? RegisteredAtUtc = null,
    DateTimeOffset? LastEnrollmentAtUtc = null,
    DateTimeOffset? DisabledAtUtc = null,
    IReadOnlyList<string>? ComplianceReasons = null);

public sealed record Gateway(
    string Id,
    string Name,
    string Region,
    HealthSeverity Health,
    int LoadPercent,
    int PeerCount,
    int CpuPercent,
    int MemoryPercent,
    int LatencyMs,
    string TenantId = SeedData.DefaultTenantId);

public sealed record GatewayPool(
    string Id,
    string Name,
    IReadOnlyList<string> Regions,
    IReadOnlyList<string> GatewayIds,
    string TenantId = SeedData.DefaultTenantId);

public sealed record PolicyRule(
    string Id,
    string Name,
    IReadOnlyList<string> Cidrs,
    IReadOnlyList<string> DnsZones,
    IReadOnlyList<int> Ports,
    string Mode,
    string TenantId = SeedData.DefaultTenantId,
    int Priority = 100,
    IReadOnlyList<string>? TargetGroupIds = null,
    bool RequireManaged = true,
    bool RequireCompliant = true,
    int MinimumPostureScore = 80,
    IReadOnlyList<DeviceRegistrationState>? AllowedDeviceStates = null);

public sealed record TunnelSession(
    string Id,
    string UserId,
    string DeviceId,
    string GatewayId,
    DateTimeOffset ConnectedAtUtc,
    int HandshakeAgeSeconds,
    int ThroughputMbps,
    string TenantId = SeedData.DefaultTenantId,
    string PolicyBundleVersion = "",
    DateTimeOffset? AuthorizedAtUtc = null,
    DateTimeOffset? RevalidateAfterUtc = null);

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
    string Message,
    string TenantId = SeedData.DefaultTenantId);

public sealed record Alert(
    string Id,
    HealthSeverity Severity,
    string Title,
    string Description,
    string TargetType,
    string TargetId,
    DateTimeOffset CreatedAtUtc,
    string TenantId = SeedData.DefaultTenantId);

public sealed record PostureReport(
    string DeviceId,
    bool Managed,
    bool Compliant,
    bool BitLockerEnabled,
    bool DefenderHealthy,
    bool FirewallEnabled,
    bool SecureBootEnabled,
    bool TamperProtectionEnabled,
    string OsVersion,
    string TenantId = SeedData.DefaultTenantId,
    int SchemaVersion = 1,
    DateTimeOffset? CollectedAtUtc = null);

public sealed record AuthProviderConfig(
    string Id,
    string Name,
    string Type,
    string Issuer,
    string ClientId,
    IReadOnlyList<string> UsernameClaimPaths,
    IReadOnlyList<string> GroupClaimPaths,
    IReadOnlyList<string> MfaClaimPaths,
    bool RequireMfa,
    bool SilentSsoEnabled,
    string TenantId = SeedData.DefaultTenantId);

public sealed record AuditEvent(
    string Id,
    long Sequence,
    string Actor,
    string Action,
    string TargetType,
    string TargetId,
    DateTimeOffset CreatedAtUtc,
    string Outcome,
    string Detail,
    string? PreviousEventHash,
    string EventHash,
    string TenantId = SeedData.DefaultTenantId);

public sealed record PolicyResolutionResult(
    string TenantId,
    string UserId,
    string DeviceId,
    IReadOnlyList<string> EffectiveGroups,
    IReadOnlyList<string> PolicyIds,
    IReadOnlyList<string> DecisionLog);

public sealed record ResolvedPolicyBundle(
    string TenantId,
    string UserId,
    string DeviceId,
    string Version,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<string> PolicyIds,
    IReadOnlyList<string> Cidrs,
    IReadOnlyList<string> DnsZones,
    IReadOnlyList<int> Ports);

public sealed record SessionAuthorizationDecision(
    bool Authorized,
    string ErrorCode,
    string Message,
    PolicyResolutionResult? Resolution,
    ResolvedPolicyBundle? Bundle,
    DateTimeOffset? RevalidateAfterUtc);

public sealed record DeviceEnrollmentResult(
    Device Device,
    bool RequiresApproval,
    bool ReconciledExistingRecord,
    string Action);

public sealed record AuditRetentionOperation(
    DateTimeOffset CutoffUtc,
    DateTimeOffset ExportedAtUtc,
    string ExportPath,
    long RemovedThroughSequence,
    DateTimeOffset RemovedThroughCreatedAtUtc,
    string RemovedThroughEventHash,
    int ExportedEventCount);

public sealed record AuditRetentionCheckpoint(
    string Id,
    DateTimeOffset CutoffUtc,
    DateTimeOffset ExportedAtUtc,
    string ExportPath,
    long RemovedThroughSequence,
    DateTimeOffset RemovedThroughCreatedAtUtc,
    string RemovedThroughEventHash,
    int ExportedEventCount);

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

public sealed record MachineTrustMaterial(
    string Id,
    MachineTrustSubjectKind Kind,
    string SubjectId,
    string SubjectName,
    string Thumbprint,
    string CertificatePem,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset NotBeforeUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset RotateAfterUtc,
    DateTimeOffset? RevokedAtUtc,
    string? ReplacedById);

public sealed record IssuedMachineTrustMaterial(
    MachineTrustMaterial Material,
    string PrivateKeyPem);
