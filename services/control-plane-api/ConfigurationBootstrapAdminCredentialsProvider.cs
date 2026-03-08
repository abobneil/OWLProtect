using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class ConfigurationBootstrapAdminCredentialsProvider(
    IOptions<SecretManagementOptions> options,
    ILogger<ConfigurationBootstrapAdminCredentialsProvider> logger) : IBootstrapAdminCredentialsProvider
{
    private readonly Lazy<BootstrapAdminCredentials> _credentials = new(() => ResolveCredentials(options.Value, logger), isThreadSafe: true);

    public BootstrapAdminCredentials GetBootstrapAdminCredentials() => _credentials.Value;

    private static BootstrapAdminCredentials ResolveCredentials(SecretManagementOptions options, ILogger logger)
    {
        var username = string.IsNullOrWhiteSpace(options.BootstrapAdminUsername) ? "admin" : options.BootstrapAdminUsername.Trim();

        var passwordHash = ResolvePasswordHash(options);
        if (!string.IsNullOrWhiteSpace(passwordHash))
        {
            logger.LogInformation("Bootstrap admin credentials loaded from configured password hash.");
            return new BootstrapAdminCredentials(username, passwordHash);
        }

        var plaintextPassword = ResolvePlaintextPassword(options);
        if (!string.IsNullOrWhiteSpace(plaintextPassword))
        {
            logger.LogInformation("Bootstrap admin credentials loaded from configured secret source.");
            return new BootstrapAdminCredentials(username, PasswordProtector.Hash(plaintextPassword));
        }

        if (options.AllowGeneratedBootstrapAdminPassword)
        {
            var generatedPassword = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            logger.LogWarning(
                "Generated an ephemeral bootstrap admin password because no configured secret source was available. Username: {Username}. Password: {Password}",
                username,
                generatedPassword);
            return new BootstrapAdminCredentials(username, PasswordProtector.Hash(generatedPassword));
        }

        throw new InvalidOperationException(
            "Bootstrap admin credentials are not configured. Set SecretManagement:BootstrapAdminPassword, SecretManagement:BootstrapAdminPasswordFile, SecretManagement:BootstrapAdminPasswordHash, or SecretManagement:BootstrapAdminPasswordHashFile.");
    }

    private static string? ResolvePasswordHash(SecretManagementOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BootstrapAdminPasswordHashFile))
        {
            var path = options.BootstrapAdminPasswordHashFile.Trim();
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Configured bootstrap admin password hash file '{path}' was not found.");
            }

            return File.ReadAllText(path).Trim();
        }

        return string.IsNullOrWhiteSpace(options.BootstrapAdminPasswordHash)
            ? null
            : options.BootstrapAdminPasswordHash.Trim();
    }

    private static string? ResolvePlaintextPassword(SecretManagementOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.BootstrapAdminPasswordFile))
        {
            var path = options.BootstrapAdminPasswordFile.Trim();
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"Configured bootstrap admin password file '{path}' was not found.");
            }

            return File.ReadAllText(path).Trim();
        }

        return string.IsNullOrWhiteSpace(options.BootstrapAdminPassword)
            ? null
            : options.BootstrapAdminPassword.Trim();
    }
}
