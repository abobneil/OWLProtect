using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed record UserLoginRequest(string Username);
public sealed record ProviderLoginRequest(string ProviderId, string Token);
public sealed record RefreshSessionRequest(string RefreshToken);
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
public sealed record ClientSessionIssueRequest(string DeviceId);
public sealed record ClientSessionRevalidationRequest(string DeviceId);
public sealed record ClientHealthReport(
    ConnectionState State,
    int LatencyMs,
    int JitterMs,
    decimal PacketLossPercent,
    int ThroughputMbps,
    int SignalStrengthPercent,
    bool DnsReachable,
    bool RouteHealthy,
    string Message,
    DateTimeOffset? SampledAtUtc);

public sealed record ControlPlaneSessionTokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

public sealed record ControlPlaneAuthSessionResponse(
    PlatformSession Session,
    ControlPlaneSessionTokenPair Tokens,
    AdminAccount? Admin,
    User? User);

public sealed record ControlPlaneClientAuthSessionResponse(
    PlatformSession Session,
    ControlPlaneSessionTokenPair Tokens,
    User User,
    Device Device,
    PolicyResolutionResult Resolution,
    ResolvedPolicyBundle Bundle,
    GatewayPlacement Placement);

public sealed record ControlPlaneClientSessionRevalidationResponse(
    PlatformSession Session,
    User User,
    Device Device,
    PolicyResolutionResult Resolution,
    ResolvedPolicyBundle Bundle,
    GatewayPlacement Placement,
    DateTimeOffset? RevalidateAfterUtc);

public sealed record ApiErrorResponse(
    string Error,
    string ErrorCode,
    string? Policy = null);
