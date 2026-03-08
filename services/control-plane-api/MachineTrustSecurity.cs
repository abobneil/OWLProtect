using System.Collections.Concurrent;
using System.Text.Json;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed record MachineRequestContext(MachineTrustMaterial TrustMaterial)
{
    public string Actor => $"{TrustMaterial.Kind.ToString().ToLowerInvariant()}:{TrustMaterial.SubjectName}";
}

internal sealed class MachineTrustReplayProtector
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);

    public bool TryRegister(string trustMaterialId, string nonce, DateTimeOffset now, TimeSpan ttl)
    {
        Cleanup(now);
        var key = $"{trustMaterialId}:{nonce}";
        return _entries.TryAdd(key, now.Add(ttl));
    }

    private void Cleanup(DateTimeOffset now)
    {
        foreach (var entry in _entries)
        {
            if (entry.Value <= now)
            {
                _entries.TryRemove(entry.Key, out _);
            }
        }
    }
}

internal static class MachineTrustSecurity
{
    private static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    public static async Task<byte[]> ReadRequestBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        request.Body.Position = 0;
        return buffer.ToArray();
    }

    public static IResult BuildDeniedResult(string error, string errorCode) =>
        Results.Json(new ApiErrorResponse(error, errorCode), statusCode: StatusCodes.Status401Unauthorized);

    public static bool TryAuthenticate(
        HttpContext httpContext,
        ReadOnlySpan<byte> body,
        MachineTrustSubjectKind expectedKind,
        IMachineTrustRepository trustRepository,
        MachineTrustReplayProtector replayProtector,
        out MachineRequestContext? machineContext,
        out IResult? deniedResult)
    {
        machineContext = null;
        deniedResult = null;

        var trustId = httpContext.Request.Headers[MachineTrustProofCodec.TrustIdHeaderName].ToString();
        var timestamp = httpContext.Request.Headers[MachineTrustProofCodec.TimestampHeaderName].ToString();
        var nonce = httpContext.Request.Headers[MachineTrustProofCodec.NonceHeaderName].ToString();
        var signature = httpContext.Request.Headers[MachineTrustProofCodec.SignatureHeaderName].ToString();

        if (string.IsNullOrWhiteSpace(trustId) ||
            string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(nonce) ||
            string.IsNullOrWhiteSpace(signature))
        {
            AuditFailure(httpContext, "machine-auth-missing", expectedKind, trustId, "Machine authentication headers were missing.");
            deniedResult = BuildDeniedResult("Machine trust authentication is required.", "machine_auth_required");
            return false;
        }

        var material = trustRepository.GetTrustMaterial(trustId);
        if (material is null || material.Kind != expectedKind)
        {
            AuditFailure(httpContext, "machine-auth-invalid", expectedKind, trustId, "Trust material was not found for the expected subject kind.");
            deniedResult = BuildDeniedResult("Machine trust authentication failed.", "machine_auth_invalid");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (material.RevokedAtUtc is not null || material.NotBeforeUtc > now || material.ExpiresAtUtc <= now)
        {
            AuditFailure(httpContext, "machine-auth-inactive", expectedKind, material.SubjectId, "Trust material is revoked or expired.");
            deniedResult = BuildDeniedResult("Machine trust authentication failed.", "machine_auth_inactive");
            return false;
        }

        if (!DateTimeOffset.TryParse(timestamp, out var parsedTimestamp) ||
            parsedTimestamp < now.Subtract(AllowedClockSkew) ||
            parsedTimestamp > now.Add(AllowedClockSkew))
        {
            AuditFailure(httpContext, "machine-auth-stale", expectedKind, material.SubjectId, "Machine request timestamp was outside the allowed skew.");
            deniedResult = BuildDeniedResult("Machine trust authentication failed.", "machine_auth_stale");
            return false;
        }

        if (!replayProtector.TryRegister(material.Id, nonce, now, AllowedClockSkew.Add(TimeSpan.FromMinutes(1))))
        {
            AuditFailure(httpContext, "machine-auth-replay", expectedKind, material.SubjectId, "Machine request nonce was replayed.");
            deniedResult = BuildDeniedResult("Machine trust authentication failed.", "machine_auth_replay");
            return false;
        }

        var pathAndQuery = httpContext.Request.Path + httpContext.Request.QueryString.Value;
        if (!MachineTrustProofCodec.Verify(material, httpContext.Request.Method, pathAndQuery, timestamp, nonce, body, signature))
        {
            AuditFailure(httpContext, "machine-auth-signature", expectedKind, material.SubjectId, "Machine request signature validation failed.");
            deniedResult = BuildDeniedResult("Machine trust authentication failed.", "machine_auth_signature_invalid");
            return false;
        }

        machineContext = new MachineRequestContext(material);
        return true;
    }

    public static IResult DeserializeFailure() =>
        Results.Json(new ApiErrorResponse("Request body was invalid.", "invalid_request_body"), statusCode: StatusCodes.Status400BadRequest);

    public static T? DeserializeBody<T>(byte[] body)
    {
        if (body.Length == 0)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(body);
    }

    private static void AuditFailure(HttpContext httpContext, string action, MachineTrustSubjectKind kind, string targetId, string detail)
    {
        var auditWriter = httpContext.RequestServices.GetRequiredService<IAuditWriter>();
        auditWriter.WriteAudit(
            "machine",
            action,
            kind.ToString().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(targetId) ? "unknown" : targetId,
            "failure",
            detail);
    }
}
