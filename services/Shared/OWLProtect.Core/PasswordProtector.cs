using System.Security.Cryptography;

namespace OWLProtect.Core;

public static class PasswordProtector
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int IterationCount = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, IterationCount, HashAlgorithmName.SHA256, HashSize);
        return $"pbkdf2${IterationCount}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedValue)
    {
        if (!storedValue.StartsWith("pbkdf2$", StringComparison.Ordinal))
        {
            return password == storedValue;
        }

        var parts = storedValue.Split('$', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expectedHash = Convert.FromBase64String(parts[3]);
        var actualHash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}
