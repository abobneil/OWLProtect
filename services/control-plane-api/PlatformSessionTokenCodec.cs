using System.Security.Cryptography;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal static class PlatformSessionTokenCodec
{
    private const int SecretSizeBytes = 32;

    public static string CreateSessionId() => Guid.NewGuid().ToString("n");

    public static string CreateToken(string sessionId)
    {
        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(SecretSizeBytes));
        return $"{sessionId}.{secret}";
    }

    public static string HashToken(string token) => PasswordProtector.Hash(token);

    public static bool VerifyToken(string token, string storedHash) => PasswordProtector.Verify(token, storedHash);

    public static string? TryGetSessionId(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var separatorIndex = token.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return null;
        }

        return token[..separatorIndex];
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
