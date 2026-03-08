namespace OWLProtect.ControlPlane.Api;

public sealed record AdminLoginRequest(string Username, string Password);
public sealed record UserLoginRequest(string Username);
public sealed record PasswordChangeRequest(string CurrentPassword, string NewPassword);
public sealed record ProviderLoginRequest(string ProviderId, string Token);
public sealed record PrivilegedOperationRequest(string OperationName);

