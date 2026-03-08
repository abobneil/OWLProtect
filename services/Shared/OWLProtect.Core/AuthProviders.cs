namespace OWLProtect.Core;

public sealed record IdpUserContext(
    string ProviderId,
    string Subject,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Groups,
    bool MfaSatisfied,
    bool SilentSsoEligible);

public interface IAuthProvider
{
    string Type { get; }
    Task<IdpUserContext> ValidateAsync(AuthProviderConfig config, string token, CancellationToken cancellationToken);
}

public sealed class AuthProviderResolver(IEnumerable<IAuthProvider> providers, IAuthProviderConfigRepository repository)
{
    private readonly IReadOnlyDictionary<string, IAuthProvider> _providersByType = providers.ToDictionary(provider => provider.Type, StringComparer.OrdinalIgnoreCase);

    public async Task<IdpUserContext> ValidateAsync(string providerId, string token, CancellationToken cancellationToken)
    {
        var config = repository.ListAuthProviders().SingleOrDefault(provider => string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase));
        if (config is null)
        {
            throw new InvalidOperationException($"Unknown auth provider '{providerId}'.");
        }

        if (!_providersByType.TryGetValue(config.Type, out var provider))
        {
            throw new InvalidOperationException($"Unsupported auth provider type '{config.Type}' for provider '{providerId}'.");
        }

        return await provider.ValidateAsync(config, token, cancellationToken);
    }
}
