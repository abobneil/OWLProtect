using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed record AdminUpsertRequest(
    string? Id,
    string Username,
    string? Password,
    AdminRole Role,
    bool MustChangePassword,
    bool MfaEnrolled);

public sealed record UserUpsertRequest(
    string? Id,
    string Username,
    string DisplayName,
    bool Enabled,
    bool TestAccount,
    string Provider,
    IReadOnlyList<string>? GroupIds,
    IReadOnlyList<string>? PolicyIds,
    string? TenantId);

public sealed record DeviceUpsertRequest(
    string? Id,
    string Name,
    string UserId,
    string City,
    string Country,
    string PublicIp,
    bool Managed,
    bool Compliant,
    int PostureScore,
    ConnectionState ConnectionState,
    DateTimeOffset? LastSeenUtc,
    string? TenantId,
    DeviceRegistrationState RegistrationState,
    DeviceEnrollmentKind EnrollmentKind,
    string? HardwareKey,
    string? SerialNumber,
    string? OperatingSystem,
    DateTimeOffset? RegisteredAtUtc,
    DateTimeOffset? LastEnrollmentAtUtc,
    DateTimeOffset? DisabledAtUtc,
    IReadOnlyList<string>? ComplianceReasons);

public sealed record GatewayUpsertRequest(
    string? Id,
    string Name,
    string Region,
    HealthSeverity Health,
    int LoadPercent,
    int PeerCount,
    int CpuPercent,
    int MemoryPercent,
    int LatencyMs,
    string? TenantId);

public sealed record PolicyUpsertRequest(
    string? Id,
    string Name,
    IReadOnlyList<string>? Cidrs,
    IReadOnlyList<string>? DnsZones,
    IReadOnlyList<int>? Ports,
    string Mode,
    string? TenantId,
    int Priority,
    IReadOnlyList<string>? TargetGroupIds,
    bool RequireManaged,
    bool RequireCompliant,
    int MinimumPostureScore,
    IReadOnlyList<DeviceRegistrationState>? AllowedDeviceStates);

public sealed record AuthProviderUpsertRequest(
    string? Id,
    string Name,
    string Type,
    string Issuer,
    string ClientId,
    IReadOnlyList<string>? UsernameClaimPaths,
    IReadOnlyList<string>? GroupClaimPaths,
    IReadOnlyList<string>? MfaClaimPaths,
    bool RequireMfa,
    bool SilentSsoEnabled,
    string? TenantId);

public sealed record SessionUpsertRequest(
    string? Id,
    string UserId,
    string DeviceId,
    string GatewayId,
    DateTimeOffset? ConnectedAtUtc,
    int HandshakeAgeSeconds,
    int ThroughputMbps,
    string? TenantId);

public sealed record DeviceEnrollmentRequest(
    string DeviceName,
    string City,
    string Country,
    string PublicIp,
    string HardwareKey,
    string SerialNumber,
    string OperatingSystem,
    DeviceEnrollmentKind EnrollmentKind,
    bool Managed);
