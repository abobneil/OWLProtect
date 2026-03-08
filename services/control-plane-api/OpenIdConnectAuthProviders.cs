using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class AuthProviderValidationException(string safeMessage, string diagnosticCode, string auditDetail, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string SafeMessage { get; } = safeMessage;
    public string DiagnosticCode { get; } = diagnosticCode;
    public string AuditDetail { get; } = auditDetail;
}

internal sealed class EntraAuthProvider(OpenIdConnectTokenValidator tokenValidator) : IAuthProvider
{
    public string Type => "entra";

    public Task<IdpUserContext> ValidateAsync(AuthProviderConfig config, string token, CancellationToken cancellationToken) =>
        tokenValidator.ValidateAsync(config, token, cancellationToken);
}

internal sealed class GenericOidcAuthProvider(OpenIdConnectTokenValidator tokenValidator) : IAuthProvider
{
    public string Type => "oidc";

    public Task<IdpUserContext> ValidateAsync(AuthProviderConfig config, string token, CancellationToken cancellationToken) =>
        tokenValidator.ValidateAsync(config, token, cancellationToken);
}

internal sealed class OpenIdConnectTokenValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _configurationManagers = new(StringComparer.OrdinalIgnoreCase);
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    public async Task<IdpUserContext> ValidateAsync(AuthProviderConfig config, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AuthProviderValidationException(
                "A bearer token is required.",
                "token-missing",
                $"Provider '{config.Id}' validation was attempted without a token.");
        }

        var principal = await ValidateTokenWithRefreshAsync(config, token, cancellationToken);
        return MapIdentityContext(config, principal.Claims);
    }

    private async Task<ClaimsPrincipal> ValidateTokenWithRefreshAsync(AuthProviderConfig config, string token, CancellationToken cancellationToken)
    {
        try
        {
            return await ValidateTokenAsync(config, token, cancellationToken);
        }
        catch (SecurityTokenSignatureKeyNotFoundException)
        {
            GetConfigurationManager(config).RequestRefresh();
            return await ValidateTokenAsync(config, token, cancellationToken);
        }
    }

    private async Task<ClaimsPrincipal> ValidateTokenAsync(AuthProviderConfig config, string token, CancellationToken cancellationToken)
    {
        OpenIdConnectConfiguration discoveryDocument;
        try
        {
            discoveryDocument = await GetConfigurationManager(config).GetConfigurationAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            throw new AuthProviderValidationException(
                "Unable to load OpenID Connect discovery metadata for the configured provider.",
                "oidc-discovery-failed",
                $"Provider '{config.Id}' metadata retrieval failed for issuer '{config.Issuer}'. {exception.Message}",
                exception);
        }

        if (discoveryDocument.SigningKeys.Count == 0)
        {
            throw new AuthProviderValidationException(
                "The configured provider did not publish signing keys.",
                "jwks-missing",
                $"Provider '{config.Id}' discovery document did not include any JWKS signing keys.");
        }

        try
        {
            return _tokenHandler.ValidateToken(token, BuildTokenValidationParameters(config, discoveryDocument), out _);
        }
        catch (SecurityTokenException exception)
        {
            throw new AuthProviderValidationException(
                "Token validation failed for the configured provider.",
                "token-validation-failed",
                $"Provider '{config.Id}' rejected the presented token. {exception.GetType().Name}: {exception.Message}",
                exception);
        }
        catch (ArgumentException exception)
        {
            throw new AuthProviderValidationException(
                "Token validation failed for the configured provider.",
                "token-validation-failed",
                $"Provider '{config.Id}' could not parse the presented token. {exception.Message}",
                exception);
        }
    }

    private ConfigurationManager<OpenIdConnectConfiguration> GetConfigurationManager(AuthProviderConfig config) =>
        _configurationManagers.GetOrAdd(config.Id, _ =>
        {
            var metadataAddress = BuildMetadataAddress(config.Issuer);
            var requireHttps = Uri.TryCreate(metadataAddress, UriKind.Absolute, out var metadataUri) &&
                               string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

            return new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataAddress,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = requireHttps })
            {
                AutomaticRefreshInterval = TimeSpan.FromHours(12),
                RefreshInterval = TimeSpan.FromMinutes(5)
            };
        });

    private static TokenValidationParameters BuildTokenValidationParameters(AuthProviderConfig config, OpenIdConnectConfiguration discoveryDocument)
    {
        var normalizedIssuer = NormalizeIssuer(config.Issuer);
        return new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = discoveryDocument.SigningKeys,
            ValidateIssuer = true,
            IssuerValidator = (issuer, _, _) =>
            {
                if (!string.Equals(NormalizeIssuer(issuer), normalizedIssuer, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SecurityTokenInvalidIssuerException($"Issuer '{issuer}' does not match configured issuer '{config.Issuer}'.");
                }

                return issuer;
            },
            ValidateAudience = true,
            ValidAudience = config.ClientId,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew
        };
    }

    private static IdpUserContext MapIdentityContext(AuthProviderConfig config, IEnumerable<Claim> claims)
    {
        var claimLookup = claims
            .GroupBy(claim => claim.Type, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.SelectMany(claim => ExpandClaimValues(claim.Value)).ToArray(), StringComparer.Ordinal);

        var subject = GetFirstClaimValue(claimLookup, JwtRegisteredClaimNames.Sub);
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new AuthProviderValidationException(
                "Validated token is missing the required subject claim.",
                "required-claim-missing",
                $"Provider '{config.Id}' token did not include the required '{JwtRegisteredClaimNames.Sub}' claim.");
        }

        var displayName =
            GetFirstClaimValue(claimLookup, "name") ??
            GetFirstClaimValue(claimLookup, "preferred_username") ??
            GetFirstClaimValue(claimLookup, "upn") ??
            GetFirstClaimValue(claimLookup, "email") ??
            subject;

        var username = ResolveFirstConfiguredClaimValue(claimLookup, config.UsernameClaimPaths) ?? displayName;
        var groups = ResolveAllConfiguredClaimValues(claimLookup, config.GroupClaimPaths);
        var mfaSatisfied = ResolveMfaSatisfied(claimLookup, config.MfaClaimPaths);
        if (config.RequireMfa && !mfaSatisfied)
        {
            throw new AuthProviderValidationException(
                "The configured provider token did not satisfy upstream MFA requirements.",
                "mfa-required",
                $"Provider '{config.Id}' token was valid but did not satisfy configured MFA claim requirements.");
        }

        return new IdpUserContext(
            config.Id,
            subject,
            username,
            displayName,
            groups,
            mfaSatisfied,
            config.SilentSsoEnabled);
    }

    private static string BuildMetadataAddress(string issuer)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var issuerUri))
        {
            throw new AuthProviderValidationException(
                "The configured issuer URI is invalid.",
                "issuer-invalid",
                $"Issuer '{issuer}' is not a valid absolute URI.");
        }

        return issuerUri.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration";
    }

    private static string NormalizeIssuer(string issuer) => issuer.TrimEnd('/');

    private static string? GetFirstClaimValue(IReadOnlyDictionary<string, string[]> claimLookup, string claimType) =>
        claimLookup.TryGetValue(claimType, out var values) ? values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) : null;

    private static IReadOnlyList<string> GetAllClaimValues(IReadOnlyDictionary<string, string[]> claimLookup, string claimType) =>
        claimLookup.TryGetValue(claimType, out var values)
            ? values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray()
            : [];

    private static string? ResolveFirstConfiguredClaimValue(IReadOnlyDictionary<string, string[]> claimLookup, IReadOnlyList<string> claimPaths)
    {
        foreach (var claimPath in claimPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var value = GetFirstClaimValue(claimLookup, claimPath);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> ResolveAllConfiguredClaimValues(IReadOnlyDictionary<string, string[]> claimLookup, IReadOnlyList<string> claimPaths)
    {
        var values = new List<string>();
        foreach (var claimPath in claimPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            values.AddRange(GetAllClaimValues(claimLookup, claimPath));
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool ResolveMfaSatisfied(IReadOnlyDictionary<string, string[]> claimLookup, IReadOnlyList<string> claimPaths)
    {
        foreach (var claimPath in claimPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            var values = GetAllClaimValues(claimLookup, claimPath);
            if (values.Count == 0)
            {
                continue;
            }

            if (string.Equals(claimPath, "amr", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Any(value => value.Contains("mfa", StringComparison.OrdinalIgnoreCase) ||
                                        value.Contains("otp", StringComparison.OrdinalIgnoreCase) ||
                                        value.Contains("fido", StringComparison.OrdinalIgnoreCase) ||
                                        value.Contains("rsa", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            if (string.Equals(claimPath, "acr", StringComparison.OrdinalIgnoreCase))
            {
                if (values.Any(value => !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            if (values.Any(value => bool.TryParse(value, out var parsed) && parsed))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> ExpandClaimValues(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            yield break;
        }

        var trimmed = rawValue.Trim();
        if (!trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            yield return trimmed;
            yield break;
        }

        List<string> values = [];
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                values.Add(trimmed);
            }
            else
            {
                foreach (var element in document.RootElement.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var value = element.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            values.Add(value);
                        }
                    }
                    else if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        values.Add(element.GetBoolean().ToString());
                    }
                    else if (element.ValueKind is JsonValueKind.Number or JsonValueKind.Object)
                    {
                        values.Add(element.GetRawText());
                    }
                }
            }
        }
        catch (JsonException)
        {
            values.Add(trimmed);
        }

        foreach (var value in values)
        {
            yield return value;
        }
    }
}
