using System.Security.Cryptography;
using System.Text;

namespace OWLProtect.Core;

public static class SensitiveDataRedactor
{
    private static readonly string[] SensitiveFieldMarkers =
    [
        "access_token",
        "authorization",
        "cookie",
        "ip",
        "key",
        "nonce",
        "password",
        "private",
        "refresh_token",
        "secret",
        "serial",
        "signature",
        "token"
    ];

    public static bool IsSensitiveField(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return false;
        }

        return SensitiveFieldMarkers.Any(marker => fieldName.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static string Redact(string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "n/a";
        }

        return IsSensitiveField(fieldName)
            ? fieldName.Contains("ip", StringComparison.OrdinalIgnoreCase)
                ? "[redacted-ip]"
                : "[redacted]"
            : value;
    }

    public static string CreateSessionCorrelationId(string sessionId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
