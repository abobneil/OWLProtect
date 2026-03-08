using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace OWLProtect.Core;

public sealed record MachineTrustProofHeaders(
    string TrustId,
    string Timestamp,
    string Nonce,
    string Signature);

public static class MachineTrustProofCodec
{
    public const string TrustIdHeaderName = "X-OWL-Trust-Id";
    public const string TimestampHeaderName = "X-OWL-Trust-Timestamp";
    public const string NonceHeaderName = "X-OWL-Trust-Nonce";
    public const string SignatureHeaderName = "X-OWL-Trust-Signature";

    public static MachineTrustProofHeaders Sign(
        string trustId,
        string privateKeyPem,
        string method,
        string pathAndQuery,
        string timestamp,
        string nonce,
        ReadOnlySpan<byte> body)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        var payload = BuildPayload(method, pathAndQuery, timestamp, nonce, body);
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return new MachineTrustProofHeaders(trustId, timestamp, nonce, Convert.ToBase64String(signature));
    }

    public static bool Verify(
        MachineTrustMaterial trustMaterial,
        string method,
        string pathAndQuery,
        string timestamp,
        string nonce,
        ReadOnlySpan<byte> body,
        string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        using var certificate = X509Certificate2.CreateFromPem(trustMaterial.CertificatePem);
        using var rsa = certificate.GetRSAPublicKey();
        if (rsa is null)
        {
            return false;
        }

        var payload = BuildPayload(method, pathAndQuery, timestamp, nonce, body);
        return rsa.VerifyData(payload, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public static string CreateNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static byte[] BuildPayload(
        string method,
        string pathAndQuery,
        string timestamp,
        string nonce,
        ReadOnlySpan<byte> body)
    {
        var bodyHash = SHA256.HashData(body);
        var canonical = string.Create(
            method.Length + pathAndQuery.Length + timestamp.Length + nonce.Length + 4 + (bodyHash.Length * 2),
            (method, pathAndQuery, timestamp, nonce, bodyHash),
            static (buffer, state) =>
            {
                var writer = new SpanWriter(buffer);
                writer.Write(state.method.ToUpperInvariant());
                writer.Write('\n');
                writer.Write(state.pathAndQuery);
                writer.Write('\n');
                writer.Write(state.timestamp);
                writer.Write('\n');
                writer.Write(state.nonce);
                writer.Write('\n');
                writer.Write(Convert.ToHexString(state.bodyHash));
            });
        return Encoding.UTF8.GetBytes(canonical);
    }

    private ref struct SpanWriter(Span<char> buffer)
    {
        private Span<char> _buffer = buffer;
        private int _index;

        public void Write(char value)
        {
            _buffer[_index++] = value;
        }

        public void Write(string value)
        {
            value.AsSpan().CopyTo(_buffer[_index..]);
            _index += value.Length;
        }
    }
}
