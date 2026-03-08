namespace OWLProtect.Core;

public sealed record IdpUserContext(
    string ProviderId,
    string Subject,
    string DisplayName,
    IReadOnlyList<string> Groups,
    bool MfaSatisfied,
    bool SilentSsoEligible);

public interface IAuthProvider
{
    string Id { get; }
    string Type { get; }
    Task<IdpUserContext> ValidateAsync(string token, CancellationToken cancellationToken);
}

public sealed class EntraAuthProvider : IAuthProvider
{
    public string Id => "auth-1";
    public string Type => "entra";

    public Task<IdpUserContext> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        return Task.FromResult(new IdpUserContext(
            Id,
            token,
            "Entra User",
            ["group-engineering"],
            MfaSatisfied: true,
            SilentSsoEligible: true));
    }
}

public sealed class GenericOidcAuthProvider : IAuthProvider
{
    public string Id => "auth-2";
    public string Type => "oidc";

    public Task<IdpUserContext> ValidateAsync(string token, CancellationToken cancellationToken)
    {
        return Task.FromResult(new IdpUserContext(
            Id,
            token,
            "OIDC User",
            ["group-engineering"],
            MfaSatisfied: true,
            SilentSsoEligible: false));
    }
}

public sealed class AuthProviderResolver(IEnumerable<IAuthProvider> providers)
{
    private readonly IReadOnlyDictionary<string, IAuthProvider> _providers = providers.ToDictionary(provider => provider.Id, StringComparer.OrdinalIgnoreCase);

    public IAuthProvider Resolve(string providerId)
    {
        if (_providers.TryGetValue(providerId, out var provider))
        {
            return provider;
        }

        throw new InvalidOperationException($"Unknown auth provider '{providerId}'.");
    }
}

