using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed record AdminLoginRequest(string Username, string Password);
public sealed record UserLoginRequest(string Username);
public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);
public sealed record ProviderLoginRequest(string ProviderId, string Token);
public sealed record RefreshSessionRequest(string RefreshToken);
public sealed record StepUpRequest(string Password);
public sealed record ClientSessionIssueRequest(string DeviceId);
public sealed record SessionTokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, string RefreshToken, DateTimeOffset RefreshTokenExpiresAtUtc);
public sealed record AuthSessionResponse(PlatformSession Session, SessionTokenPair Tokens, AdminAccount? Admin, User? User);
public sealed record ClientAuthSessionResponse(PlatformSession Session, SessionTokenPair Tokens, User User, Device Device, PolicyResolutionResult Resolution, ResolvedPolicyBundle Bundle, GatewayPlacement Placement);
public sealed record ApiErrorResponse(string Error, string ErrorCode, string? Policy = null);
public sealed record ValidationErrorResponse(string Error, string ErrorCode, IReadOnlyList<string> Details);
public sealed record AuditExportResponse(DateTimeOffset GeneratedAtUtc, DateTimeOffset? CutoffUtc, int EventCount, IReadOnlyList<AuditEvent> Events);
public sealed record AuditRetentionRunResponse(int ExportedEventCount, AuditRetentionCheckpoint? Checkpoint);
