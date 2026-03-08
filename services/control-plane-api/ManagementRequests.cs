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
    IReadOnlyList<string>? PolicyIds);

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
    DateTimeOffset? LastSeenUtc);

public sealed record GatewayUpsertRequest(
    string? Id,
    string Name,
    string Region,
    HealthSeverity Health,
    int LoadPercent,
    int PeerCount,
    int CpuPercent,
    int MemoryPercent,
    int LatencyMs);

public sealed record PolicyUpsertRequest(
    string? Id,
    string Name,
    IReadOnlyList<string>? Cidrs,
    IReadOnlyList<string>? DnsZones,
    IReadOnlyList<int>? Ports,
    string Mode);

public sealed record SessionUpsertRequest(
    string? Id,
    string UserId,
    string DeviceId,
    string GatewayId,
    DateTimeOffset? ConnectedAtUtc,
    int HandshakeAgeSeconds,
    int ThroughputMbps);
