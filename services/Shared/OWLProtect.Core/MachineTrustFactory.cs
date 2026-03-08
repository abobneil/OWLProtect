using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace OWLProtect.Core;

public static class MachineTrustFactory
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(90);
    private static readonly TimeSpan RotationLead = TimeSpan.FromDays(14);

    public static IssuedMachineTrustMaterial Create(MachineTrustSubjectKind kind, string subjectId, string subjectName)
    {
        using var rsa = RSA.Create(3072);
        var distinguishedName = new X500DistinguishedName($"CN={BuildCommonName(kind, subjectName)}, O=OWLProtect");
        var request = new CertificateRequest(distinguishedName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.2", "Client Authentication")],
            critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddUri(new Uri($"urn:owlprotect:{kind.ToString().ToLowerInvariant()}:{Uri.EscapeDataString(subjectId)}"));
        request.CertificateExtensions.Add(san.Build());

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        var expiresAt = notBefore.Add(Lifetime);
        using var certificate = request.CreateSelfSigned(notBefore, expiresAt);
        var material = new MachineTrustMaterial(
            Guid.NewGuid().ToString("n"),
            kind,
            subjectId,
            subjectName,
            certificate.Thumbprint,
            certificate.ExportCertificatePem(),
            DateTimeOffset.UtcNow,
            notBefore,
            expiresAt,
            expiresAt.Subtract(RotationLead),
            RevokedAtUtc: null,
            ReplacedById: null);

        return new IssuedMachineTrustMaterial(material, rsa.ExportPkcs8PrivateKeyPem());
    }

    private static string BuildCommonName(MachineTrustSubjectKind kind, string subjectName)
    {
        var prefix = kind == MachineTrustSubjectKind.Gateway ? "gateway" : "device";
        var sanitized = string.Concat(subjectName.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' or '_'));
        return string.IsNullOrWhiteSpace(sanitized) ? prefix : $"{prefix}-{sanitized}";
    }
}
