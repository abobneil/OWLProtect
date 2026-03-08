using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed record AdminLoginRequest(string Username, string Password);
public sealed record UserLoginRequest(string Username);
public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);
public sealed record ProviderLoginRequest(string ProviderId, string Token);
public sealed record PrivilegedOperationRequest(string OperationName);
public sealed record RefreshSessionRequest(string RefreshToken);
public sealed record StepUpRequest(string Password);
public sealed record ClientSessionIssueRequest(string DeviceId);
public sealed record SessionTokenPair(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, string RefreshToken, DateTimeOffset RefreshTokenExpiresAtUtc);
public sealed record AuthSessionResponse(PlatformSession Session, SessionTokenPair Tokens, AdminAccount? Admin, User? User);
public sealed record ClientAuthSessionResponse(PlatformSession Session, SessionTokenPair Tokens, User User, Device Device);
