using System.Security.Cryptography;
using System.Text;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal static class ExternalIdentityUserFactory
{
    public static string CreateUserId(string providerId, string subject)
    {
        var payload = Encoding.UTF8.GetBytes($"{providerId}:{subject}");
        var hash = SHA256.HashData(payload);
        return $"idp-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    public static User CreateOrUpdateUser(User? existingUser, AuthProviderConfig providerConfig, IdpUserContext identityContext)
    {
        var userId = existingUser?.Id ?? CreateUserId(providerConfig.Id, identityContext.Subject);
        return new User(
            userId,
            identityContext.Username,
            identityContext.DisplayName,
            Enabled: existingUser?.Enabled ?? true,
            TestAccount: false,
            Provider: providerConfig.Type,
            GroupIds: identityContext.Groups,
            PolicyIds: existingUser?.PolicyIds ?? []);
    }
}
