using System.Diagnostics;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class InMemoryPlatformSessionStore(IAuditWriter auditWriter) : IPlatformSessionStore
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, StoredPlatformSession> _sessions = new(StringComparer.Ordinal);

    public IssuedPlatformSession CreateSession(PlatformSessionKind kind, string subjectId, string subjectName, string? role)
    {
        lock (_gate)
        {
            var issuedSession = CreateIssuedSession(kind, subjectId, subjectName, role, DateTimeOffset.UtcNow);
            _sessions[issuedSession.Session.Id] = new StoredPlatformSession(
                PlatformSessionTokenCodec.HashToken(issuedSession.AccessToken),
                PlatformSessionTokenCodec.HashToken(issuedSession.RefreshToken),
                issuedSession.Session);

            OwlProtectTelemetry.SessionsIssued.Add(1, new TagList
            {
                { "kind", kind.ToString() },
                { "store", "in-memory" }
            });
            auditWriter.WriteAudit(subjectName, "platform-session-issued", "platform-session", issuedSession.Session.Id, "success", $"Issued {kind} session.");
            return issuedSession;
        }
    }

    public PlatformSession? Authenticate(string accessToken)
    {
        lock (_gate)
        {
            var storedSession = GetStoredSession(accessToken);
            if (storedSession is null || !IsAccessTokenActive(storedSession.Session))
            {
                return null;
            }

            if (!PlatformSessionTokenCodec.VerifyToken(accessToken, storedSession.AccessTokenHash))
            {
                return null;
            }

            var updated = storedSession.Session with { LastAuthenticatedAtUtc = DateTimeOffset.UtcNow };
            _sessions[updated.Id] = storedSession with { Session = updated };
            return updated;
        }
    }

    public PlatformSession? GetSession(string sessionId)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var storedSession))
            {
                return null;
            }

            return IsRefreshTokenActive(storedSession.Session) ? storedSession.Session : null;
        }
    }

    public IssuedPlatformSession Refresh(string refreshToken)
    {
        lock (_gate)
        {
            var storedSession = GetStoredSession(refreshToken);
            if (storedSession is null || !IsRefreshTokenActive(storedSession.Session))
            {
                throw new InvalidOperationException("Refresh token is invalid or expired.");
            }

            if (!PlatformSessionTokenCodec.VerifyToken(refreshToken, storedSession.RefreshTokenHash))
            {
                throw new InvalidOperationException("Refresh token is invalid or expired.");
            }

            var now = DateTimeOffset.UtcNow;
            var refreshedSession = storedSession.Session with
            {
                AccessTokenExpiresAtUtc = now.Add(AccessTokenLifetime),
                RefreshTokenExpiresAtUtc = now.Add(RefreshTokenLifetime),
                LastAuthenticatedAtUtc = now,
                StepUpExpiresAtUtc = null,
                RevokedAtUtc = null
            };

            var nextAccessToken = PlatformSessionTokenCodec.CreateToken(refreshedSession.Id);
            var nextRefreshToken = PlatformSessionTokenCodec.CreateToken(refreshedSession.Id);
            _sessions[refreshedSession.Id] = new StoredPlatformSession(
                PlatformSessionTokenCodec.HashToken(nextAccessToken),
                PlatformSessionTokenCodec.HashToken(nextRefreshToken),
                refreshedSession);

            auditWriter.WriteAudit(refreshedSession.SubjectName, "platform-session-refreshed", "platform-session", refreshedSession.Id, "success", "Rotated access and refresh tokens.");
            return new IssuedPlatformSession(refreshedSession, nextAccessToken, nextRefreshToken);
        }
    }

    public PlatformSession MarkStepUp(string sessionId, DateTimeOffset expiresAtUtc, string actor)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var storedSession) || !IsAccessTokenActive(storedSession.Session))
            {
                throw new InvalidOperationException("Session was not found or is no longer active.");
            }

            var updated = storedSession.Session with { StepUpExpiresAtUtc = expiresAtUtc };
            _sessions[sessionId] = storedSession with { Session = updated };
            auditWriter.WriteAudit(actor, "platform-session-step-up", "platform-session", sessionId, "success", $"Privileged step-up granted until {expiresAtUtc:O}.");
            return updated;
        }
    }

    public bool RevokeSession(string sessionId, string actor, string reason)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var storedSession) || storedSession.Session.RevokedAtUtc is not null)
            {
                return false;
            }

            var updated = storedSession.Session with { RevokedAtUtc = DateTimeOffset.UtcNow, StepUpExpiresAtUtc = null };
            _sessions[sessionId] = storedSession with { Session = updated };
            auditWriter.WriteAudit(actor, "platform-session-revoked", "platform-session", sessionId, "success", reason);
            return true;
        }
    }

    public int RevokeSubjectSessions(PlatformSessionKind kind, string subjectId, string actor, string reason)
    {
        lock (_gate)
        {
            var updatedCount = 0;
            foreach (var entry in _sessions.ToArray())
            {
                if (entry.Value.Session.Kind != kind ||
                    !string.Equals(entry.Value.Session.SubjectId, subjectId, StringComparison.Ordinal) ||
                    entry.Value.Session.RevokedAtUtc is not null)
                {
                    continue;
                }

                _sessions[entry.Key] = entry.Value with
                {
                    Session = entry.Value.Session with
                    {
                        RevokedAtUtc = DateTimeOffset.UtcNow,
                        StepUpExpiresAtUtc = null
                    }
                };
                updatedCount++;
            }

            if (updatedCount > 0)
            {
                auditWriter.WriteAudit(actor, "platform-session-subject-revoked", "subject", subjectId, "success", $"{updatedCount} {kind} session(s) revoked. {reason}");
            }

            return updatedCount;
        }
    }

    private static bool IsAccessTokenActive(PlatformSession session) =>
        session.RevokedAtUtc is null && session.AccessTokenExpiresAtUtc > DateTimeOffset.UtcNow;

    private static bool IsRefreshTokenActive(PlatformSession session) =>
        session.RevokedAtUtc is null && session.RefreshTokenExpiresAtUtc > DateTimeOffset.UtcNow;

    private StoredPlatformSession? GetStoredSession(string token)
    {
        var sessionId = PlatformSessionTokenCodec.TryGetSessionId(token);
        if (sessionId is null || !_sessions.TryGetValue(sessionId, out var storedSession))
        {
            return null;
        }

        return storedSession;
    }

    private static IssuedPlatformSession CreateIssuedSession(PlatformSessionKind kind, string subjectId, string subjectName, string? role, DateTimeOffset now)
    {
        var sessionId = PlatformSessionTokenCodec.CreateSessionId();
        var session = new PlatformSession(
            sessionId,
            kind,
            subjectId,
            subjectName,
            role,
            now,
            now.Add(AccessTokenLifetime),
            now.Add(RefreshTokenLifetime),
            now,
            StepUpExpiresAtUtc: null,
            RevokedAtUtc: null);

        return new IssuedPlatformSession(
            session,
            PlatformSessionTokenCodec.CreateToken(sessionId),
            PlatformSessionTokenCodec.CreateToken(sessionId));
    }

    private sealed record StoredPlatformSession(
        string AccessTokenHash,
        string RefreshTokenHash,
        PlatformSession Session);
}
