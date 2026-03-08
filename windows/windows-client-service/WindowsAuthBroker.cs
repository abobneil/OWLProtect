using System.Security.Principal;
using Microsoft.Extensions.Options;

namespace OWLProtect.WindowsClientService;

public sealed class WindowsAuthBroker(
    ControlPlaneClient controlPlaneClient,
    IOptions<WindowsClientOptions> options)
{
    public async Task<AuthSessionContext> AuthenticateAsync(bool silentPreferred, CancellationToken cancellationToken)
    {
        var attempts = new List<string>();
        if (silentPreferred)
        {
            var silentResult = await TrySilentAsync(attempts, cancellationToken);
            if (silentResult is not null)
            {
                return silentResult;
            }
        }

        var interactiveResult = await TryInteractiveAsync(attempts, cancellationToken);
        if (interactiveResult is not null)
        {
            return interactiveResult;
        }

        throw new InvalidOperationException(
            attempts.Count == 0
                ? "No Windows auth path is configured. Set WindowsClient__InteractiveUsername or provider token settings."
                : string.Join(" ", attempts));
    }

    private async Task<AuthSessionContext?> TrySilentAsync(List<string> attempts, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (!string.IsNullOrWhiteSpace(configured.SilentProviderId) &&
            !string.IsNullOrWhiteSpace(configured.SilentProviderToken))
        {
            try
            {
                var response = await controlPlaneClient.LoginWithProviderAsync(
                    configured.SilentProviderId,
                    configured.SilentProviderToken,
                    cancellationToken);
                return new AuthSessionContext("Silent SSO", $"Validated silent provider '{configured.SilentProviderId}'.", response);
            }
            catch (Exception exception)
            {
                attempts.Add($"Silent provider login failed: {exception.Message}");
            }
        }

        var username = configured.SilentUsername ?? ResolveWindowsUsername();
        if (!string.IsNullOrWhiteSpace(username))
        {
            try
            {
                var response = await controlPlaneClient.LoginWithLocalUserAsync(username, cancellationToken);
                return new AuthSessionContext("Silent SSO", $"Bound the current Windows identity to '{username}'.", response);
            }
            catch (Exception exception)
            {
                attempts.Add($"Silent local login failed for '{username}': {exception.Message}");
            }
        }

        return null;
    }

    private async Task<AuthSessionContext?> TryInteractiveAsync(List<string> attempts, CancellationToken cancellationToken)
    {
        var configured = options.Value;
        if (!string.IsNullOrWhiteSpace(configured.InteractiveProviderId) &&
            !string.IsNullOrWhiteSpace(configured.InteractiveProviderToken))
        {
            try
            {
                var response = await controlPlaneClient.LoginWithProviderAsync(
                    configured.InteractiveProviderId,
                    configured.InteractiveProviderToken,
                    cancellationToken);
                return new AuthSessionContext("Interactive Fallback", $"Validated interactive provider '{configured.InteractiveProviderId}'.", response);
            }
            catch (Exception exception)
            {
                attempts.Add($"Interactive provider login failed: {exception.Message}");
            }
        }

        var username = configured.InteractiveUsername ?? ResolveWindowsUsername();
        if (!string.IsNullOrWhiteSpace(username))
        {
            try
            {
                var response = await controlPlaneClient.LoginWithLocalUserAsync(username, cancellationToken);
                return new AuthSessionContext("Interactive Fallback", $"Issued a local interactive session for '{username}'.", response);
            }
            catch (Exception exception)
            {
                attempts.Add($"Interactive local login failed for '{username}': {exception.Message}");
            }
        }

        return null;
    }

    private static string? ResolveWindowsUsername()
    {
        var identityName = WindowsIdentity.GetCurrent()?.Name;
        if (string.IsNullOrWhiteSpace(identityName))
        {
            return string.IsNullOrWhiteSpace(Environment.UserName) ? null : Environment.UserName;
        }

        var segments = identityName.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? identityName : segments[^1];
    }
}

public sealed record AuthSessionContext(
    string Mode,
    string Summary,
    ControlPlaneAuthSessionResponse Response);
