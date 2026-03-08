using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using OWLProtect.Core;
using StackExchange.Redis;

namespace OWLProtect.ControlPlane.Api;

internal sealed class PostgresPlatformSessionStore : IPlatformSessionStore, IDisposable
{
    private static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);
    private readonly NpgsqlDataSource _dataSource;
    private readonly ConnectionMultiplexer? _redisConnection;
    private readonly IDatabase? _redisDatabase;
    private readonly IAuditWriter _auditWriter;

    public PostgresPlatformSessionStore(IOptions<PersistenceOptions> options, IAuditWriter auditWriter)
    {
        var persistenceOptions = options.Value;
        _auditWriter = auditWriter;
        _dataSource = CreateDataSource(persistenceOptions.ConnectionString);
        if (!string.IsNullOrWhiteSpace(persistenceOptions.RedisConnectionString))
        {
            _redisConnection = ConnectionMultiplexer.Connect(persistenceOptions.RedisConnectionString);
            _redisDatabase = _redisConnection.GetDatabase();
        }
    }

    public IssuedPlatformSession CreateSession(PlatformSessionKind kind, string subjectId, string subjectName, string? role)
    {
        var now = DateTimeOffset.UtcNow;
        var sessionId = PlatformSessionTokenCodec.CreateSessionId();
        var accessToken = PlatformSessionTokenCodec.CreateToken(sessionId);
        var refreshToken = PlatformSessionTokenCodec.CreateToken(sessionId);
        var accessTokenHash = PlatformSessionTokenCodec.HashToken(accessToken);
        var refreshTokenHash = PlatformSessionTokenCodec.HashToken(refreshToken);
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

        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO platform_sessions (
                id,
                kind,
                subject_id,
                subject_name,
                role,
                access_token_hash,
                refresh_token_hash,
                created_at_utc,
                access_token_expires_at_utc,
                refresh_token_expires_at_utc,
                last_authenticated_at_utc,
                step_up_expires_at_utc,
                revoked_at_utc)
            VALUES (
                @id,
                @kind,
                @subject_id,
                @subject_name,
                @role,
                @access_token_hash,
                @refresh_token_hash,
                @created_at_utc,
                @access_token_expires_at_utc,
                @refresh_token_expires_at_utc,
                @last_authenticated_at_utc,
                @step_up_expires_at_utc,
                @revoked_at_utc)
            """,
            connection);
        BindSession(command, session, accessTokenHash, refreshTokenHash);
        command.ExecuteNonQuery();

        CacheStoredSession(new StoredPlatformSession(accessTokenHash, refreshTokenHash, session));
        OwlProtectTelemetry.SessionsIssued.Add(1, new TagList
        {
            { "kind", kind.ToString() },
            { "store", "postgres" }
        });
        _auditWriter.WriteAudit(subjectName, "platform-session-issued", "platform-session", sessionId, "success", $"Issued {kind} session.");
        return new IssuedPlatformSession(session, accessToken, refreshToken);
    }

    public PlatformSession? Authenticate(string accessToken)
    {
        var sessionId = PlatformSessionTokenCodec.TryGetSessionId(accessToken);
        if (sessionId is null)
        {
            return null;
        }

        using var connection = _dataSource.OpenConnection();
        var storedSession = LoadStoredSessionFromCache(sessionId) ?? LoadStoredSession(connection, sessionId);
        if (storedSession is null ||
            storedSession.Session.RevokedAtUtc is not null ||
            storedSession.Session.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow ||
            !PlatformSessionTokenCodec.VerifyToken(accessToken, storedSession.AccessTokenHash))
        {
            RemoveCachedSession(sessionId);
            return null;
        }

        var updated = storedSession.Session with { LastAuthenticatedAtUtc = DateTimeOffset.UtcNow };
        using var command = new NpgsqlCommand(
            """
            UPDATE platform_sessions
            SET last_authenticated_at_utc = @last_authenticated_at_utc
            WHERE id = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", updated.Id);
        command.Parameters.AddWithValue("last_authenticated_at_utc", updated.LastAuthenticatedAtUtc);
        command.ExecuteNonQuery();

        CacheStoredSession(storedSession with { Session = updated });
        return updated;
    }

    public PlatformSession? GetSession(string sessionId)
    {
        using var connection = _dataSource.OpenConnection();
        var storedSession = LoadStoredSessionFromCache(sessionId) ?? LoadStoredSession(connection, sessionId);
        if (storedSession is null ||
            storedSession.Session.RevokedAtUtc is not null ||
            storedSession.Session.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            RemoveCachedSession(sessionId);
            return null;
        }

        CacheStoredSession(storedSession);
        return storedSession.Session;
    }

    public IssuedPlatformSession Refresh(string refreshToken)
    {
        var sessionId = PlatformSessionTokenCodec.TryGetSessionId(refreshToken);
        if (sessionId is null)
        {
            throw new InvalidOperationException("Refresh token is invalid or expired.");
        }

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var storedSession = LoadStoredSessionFromCache(sessionId) ?? LoadStoredSession(connection, sessionId, transaction);
        if (storedSession is null ||
            storedSession.Session.RevokedAtUtc is not null ||
            storedSession.Session.RefreshTokenExpiresAtUtc <= DateTimeOffset.UtcNow ||
            !PlatformSessionTokenCodec.VerifyToken(refreshToken, storedSession.RefreshTokenHash))
        {
            RemoveCachedSession(sessionId);
            throw new InvalidOperationException("Refresh token is invalid or expired.");
        }

        var now = DateTimeOffset.UtcNow;
        var nextAccessToken = PlatformSessionTokenCodec.CreateToken(sessionId);
        var nextRefreshToken = PlatformSessionTokenCodec.CreateToken(sessionId);
        var nextAccessTokenHash = PlatformSessionTokenCodec.HashToken(nextAccessToken);
        var nextRefreshTokenHash = PlatformSessionTokenCodec.HashToken(nextRefreshToken);
        var refreshedSession = storedSession.Session with
        {
            AccessTokenExpiresAtUtc = now.Add(AccessTokenLifetime),
            RefreshTokenExpiresAtUtc = now.Add(RefreshTokenLifetime),
            LastAuthenticatedAtUtc = now,
            StepUpExpiresAtUtc = null,
            RevokedAtUtc = null
        };

        using var update = new NpgsqlCommand(
            """
            UPDATE platform_sessions
            SET access_token_hash = @access_token_hash,
                refresh_token_hash = @refresh_token_hash,
                access_token_expires_at_utc = @access_token_expires_at_utc,
                refresh_token_expires_at_utc = @refresh_token_expires_at_utc,
                last_authenticated_at_utc = @last_authenticated_at_utc,
                step_up_expires_at_utc = NULL
            WHERE id = @id
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("id", refreshedSession.Id);
        update.Parameters.AddWithValue("access_token_hash", nextAccessTokenHash);
        update.Parameters.AddWithValue("refresh_token_hash", nextRefreshTokenHash);
        update.Parameters.AddWithValue("access_token_expires_at_utc", refreshedSession.AccessTokenExpiresAtUtc);
        update.Parameters.AddWithValue("refresh_token_expires_at_utc", refreshedSession.RefreshTokenExpiresAtUtc);
        update.Parameters.AddWithValue("last_authenticated_at_utc", refreshedSession.LastAuthenticatedAtUtc);
        update.ExecuteNonQuery();

        transaction.Commit();
        CacheStoredSession(new StoredPlatformSession(nextAccessTokenHash, nextRefreshTokenHash, refreshedSession));
        _auditWriter.WriteAudit(refreshedSession.SubjectName, "platform-session-refreshed", "platform-session", refreshedSession.Id, "success", "Rotated access and refresh tokens.");
        return new IssuedPlatformSession(refreshedSession, nextAccessToken, nextRefreshToken);
    }

    public PlatformSession MarkStepUp(string sessionId, DateTimeOffset expiresAtUtc, string actor)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var storedSession = LoadStoredSession(connection, sessionId, transaction);
        if (storedSession is null ||
            storedSession.Session.RevokedAtUtc is not null ||
            storedSession.Session.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Session was not found or is no longer active.");
        }

        var updated = storedSession.Session with { StepUpExpiresAtUtc = expiresAtUtc };
        using var command = new NpgsqlCommand(
            """
            UPDATE platform_sessions
            SET step_up_expires_at_utc = @step_up_expires_at_utc
            WHERE id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", sessionId);
        command.Parameters.AddWithValue("step_up_expires_at_utc", expiresAtUtc);
        command.ExecuteNonQuery();

        transaction.Commit();
        CacheStoredSession(storedSession with { Session = updated });
        _auditWriter.WriteAudit(actor, "platform-session-step-up", "platform-session", sessionId, "success", $"Privileged step-up granted until {expiresAtUtc:O}.");
        return updated;
    }

    public bool RevokeSession(string sessionId, string actor, string reason)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            UPDATE platform_sessions
            SET revoked_at_utc = NOW(),
                step_up_expires_at_utc = NULL
            WHERE id = @id
              AND revoked_at_utc IS NULL
            """,
            connection);
        command.Parameters.AddWithValue("id", sessionId);
        var affected = command.ExecuteNonQuery() > 0;
        if (affected)
        {
            RemoveCachedSession(sessionId);
            _auditWriter.WriteAudit(actor, "platform-session-revoked", "platform-session", sessionId, "success", reason);
        }

        return affected;
    }

    public int RevokeSubjectSessions(PlatformSessionKind kind, string subjectId, string actor, string reason)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            UPDATE platform_sessions
            SET revoked_at_utc = NOW(),
                step_up_expires_at_utc = NULL
            WHERE kind = @kind
              AND subject_id = @subject_id
              AND revoked_at_utc IS NULL
            RETURNING id
            """,
            connection);
        command.Parameters.AddWithValue("kind", kind.ToString());
        command.Parameters.AddWithValue("subject_id", subjectId);

        var revokedSessionIds = new List<string>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                revokedSessionIds.Add(reader.GetString(0));
            }
        }

        if (revokedSessionIds.Count > 0)
        {
            RemoveCachedSessions(revokedSessionIds);
            _auditWriter.WriteAudit(actor, "platform-session-subject-revoked", "subject", subjectId, "success", $"{revokedSessionIds.Count} {kind} session(s) revoked. {reason}");
        }

        return revokedSessionIds.Count;
    }

    public void Dispose()
    {
        _redisConnection?.Dispose();
        _dataSource.Dispose();
    }

    private static NpgsqlDataSource CreateDataSource(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Persistence:ConnectionString is required when using the postgres provider.");
        }

        return NpgsqlDataSource.Create(connectionString);
    }

    private void CacheStoredSession(StoredPlatformSession storedSession)
    {
        if (_redisDatabase is null)
        {
            return;
        }

        var ttl = storedSession.Session.RefreshTokenExpiresAtUtc - DateTimeOffset.UtcNow;
        if (ttl <= TimeSpan.Zero)
        {
            RemoveCachedSession(storedSession.Session.Id);
            return;
        }

        var cacheValue = JsonSerializer.Serialize(new CachedPlatformSession(storedSession.AccessTokenHash, storedSession.RefreshTokenHash, storedSession.Session));
        _redisDatabase.StringSet(GetCacheKey(storedSession.Session.Id), cacheValue, ttl);
    }

    private StoredPlatformSession? LoadStoredSessionFromCache(string sessionId)
    {
        if (_redisDatabase is null)
        {
            return null;
        }

        var cacheValue = _redisDatabase.StringGet(GetCacheKey(sessionId));
        if (!cacheValue.HasValue)
        {
            return null;
        }

        var cached = JsonSerializer.Deserialize<CachedPlatformSession>(cacheValue.ToString());
        if (cached is null)
        {
            RemoveCachedSession(sessionId);
            return null;
        }

        return new StoredPlatformSession(cached.AccessTokenHash, cached.RefreshTokenHash, cached.Session);
    }

    private void RemoveCachedSession(string sessionId)
    {
        _redisDatabase?.KeyDelete(GetCacheKey(sessionId));
    }

    private void RemoveCachedSessions(IEnumerable<string> sessionIds)
    {
        if (_redisDatabase is null)
        {
            return;
        }

        var keys = sessionIds.Select(id => (RedisKey)GetCacheKey(id)).ToArray();
        if (keys.Length > 0)
        {
            _redisDatabase.KeyDelete(keys);
        }
    }

    private static string GetCacheKey(string sessionId) => $"platform-session:{sessionId}";

    private static void BindSession(NpgsqlCommand command, PlatformSession session, string accessTokenHash, string refreshTokenHash)
    {
        command.Parameters.AddWithValue("id", session.Id);
        command.Parameters.AddWithValue("kind", session.Kind.ToString());
        command.Parameters.AddWithValue("subject_id", session.SubjectId);
        command.Parameters.AddWithValue("subject_name", session.SubjectName);
        command.Parameters.AddWithValue("role", (object?)session.Role ?? DBNull.Value);
        command.Parameters.AddWithValue("access_token_hash", accessTokenHash);
        command.Parameters.AddWithValue("refresh_token_hash", refreshTokenHash);
        command.Parameters.AddWithValue("created_at_utc", session.CreatedAtUtc);
        command.Parameters.AddWithValue("access_token_expires_at_utc", session.AccessTokenExpiresAtUtc);
        command.Parameters.AddWithValue("refresh_token_expires_at_utc", session.RefreshTokenExpiresAtUtc);
        command.Parameters.AddWithValue("last_authenticated_at_utc", session.LastAuthenticatedAtUtc);
        command.Parameters.AddWithValue("step_up_expires_at_utc", (object?)session.StepUpExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("revoked_at_utc", (object?)session.RevokedAtUtc ?? DBNull.Value);
    }

    private static StoredPlatformSession? LoadStoredSession(NpgsqlConnection connection, string sessionId, NpgsqlTransaction? transaction = null)
    {
        using var command = new NpgsqlCommand(
            """
            SELECT id,
                   kind,
                   subject_id,
                   subject_name,
                   role,
                   access_token_hash,
                   refresh_token_hash,
                   created_at_utc,
                   access_token_expires_at_utc,
                   refresh_token_expires_at_utc,
                   last_authenticated_at_utc,
                   step_up_expires_at_utc,
                   revoked_at_utc
            FROM platform_sessions
            WHERE id = @id
            LIMIT 1
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", sessionId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new StoredPlatformSession(
            reader.GetString(5),
            reader.GetString(6),
            new PlatformSession(
                reader.GetString(0),
                Enum.Parse<PlatformSessionKind>(reader.GetString(1), ignoreCase: true),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8),
                reader.GetFieldValue<DateTimeOffset>(9),
                reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
                reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12)));
    }

    private sealed record StoredPlatformSession(
        string AccessTokenHash,
        string RefreshTokenHash,
        PlatformSession Session);

    private sealed record CachedPlatformSession(
        string AccessTokenHash,
        string RefreshTokenHash,
        PlatformSession Session);
}
