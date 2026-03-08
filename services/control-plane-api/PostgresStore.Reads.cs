using Npgsql;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed partial class PostgresStore
{
    public DashboardSnapshot Snapshot() =>
        new(
            ListAdmins(),
            ListUsers(),
            ListDevices(),
            ListGateways(),
            ListGatewayPools(),
            ListPolicies(),
            ListSessions(),
            ListHealthSamples(),
            ListAlerts(),
            ListAuthProviders(),
            ListAuditEvents());

    public BootstrapStatus GetBootstrapStatus()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT a.must_change_password, a.mfa_enrolled, u.enabled, u.enabled_at_utc
            FROM admins a
            CROSS JOIN LATERAL (
                SELECT enabled, enabled_at_utc
                FROM users
                WHERE username = 'user'
                LIMIT 1
            ) u
            ORDER BY a.username
            LIMIT 1
            """,
            connection);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Bootstrap state is unavailable.");
        }

        var enabledAt = reader.IsDBNull(3) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(3);
        return new BootstrapStatus(
            reader.GetBoolean(0),
            !reader.GetBoolean(1),
            reader.GetBoolean(2),
            enabledAt?.AddHours(1));
    }

    public IReadOnlyList<AdminAccount> ListAdmins()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, username, password_hash, role, must_change_password, mfa_enrolled
            FROM admins
            ORDER BY username
            """,
            connection);
        using var reader = command.ExecuteReader();

        var admins = new List<AdminAccount>();
        while (reader.Read())
        {
            admins.Add(MapAdmin(reader));
        }

        return admins;
    }

    public IReadOnlyList<User> ListUsers()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, enabled_at_utc
            FROM users
            ORDER BY username
            """,
            connection);
        using var reader = command.ExecuteReader();

        var users = new List<User>();
        while (reader.Read())
        {
            users.Add(MapUser(reader));
        }

        return users;
    }

    public IReadOnlyList<Device> ListDevices()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, name, user_id, city, country, public_ip, managed, compliant, posture_score, connection_state, last_seen_utc
            FROM devices
            ORDER BY name
            """,
            connection);
        using var reader = command.ExecuteReader();

        var devices = new List<Device>();
        while (reader.Read())
        {
            devices.Add(MapDevice(reader));
        }

        return devices;
    }

    public IReadOnlyList<ConnectionMapPoint> GetConnectionMap()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, name, city, country, public_ip, connection_state
            FROM devices
            ORDER BY name
            """,
            connection);
        using var reader = command.ExecuteReader();

        var points = new List<ConnectionMapPoint>();
        while (reader.Read())
        {
            points.Add(new ConnectionMapPoint(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ParseConnectionState(reader.GetString(5))));
        }

        return points;
    }

    public IReadOnlyList<Gateway> ListGateways()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, name, region, health, load_percent, peer_count, cpu_percent, memory_percent, latency_ms
            FROM gateways
            ORDER BY name
            """,
            connection);
        using var reader = command.ExecuteReader();

        var gateways = new List<Gateway>();
        while (reader.Read())
        {
            gateways.Add(MapGateway(reader));
        }

        return gateways;
    }

    public IReadOnlyList<GatewayPool> ListGatewayPools()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, name, regions, gateway_ids
            FROM gateway_pools
            ORDER BY name
            """,
            connection);
        using var reader = command.ExecuteReader();

        var pools = new List<GatewayPool>();
        while (reader.Read())
        {
            pools.Add(new GatewayPool(
                reader.GetString(0),
                reader.GetString(1),
                ReadStringArray(reader, 2),
                ReadStringArray(reader, 3)));
        }

        return pools;
    }

    public IReadOnlyList<PolicyRule> ListPolicies()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, name, cidrs, dns_zones, ports, mode
            FROM policies
            ORDER BY name
            """,
            connection);
        using var reader = command.ExecuteReader();

        var policies = new List<PolicyRule>();
        while (reader.Read())
        {
            policies.Add(new PolicyRule(
                reader.GetString(0),
                reader.GetString(1),
                ReadStringArray(reader, 2),
                ReadStringArray(reader, 3),
                ReadIntArray(reader, 4),
                reader.GetString(5)));
        }

        return policies;
    }

    public IReadOnlyList<TunnelSession> ListSessions()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, user_id, device_id, gateway_id, connected_at_utc, handshake_age_seconds, throughput_mbps
            FROM user_sessions
            WHERE revoked_at_utc IS NULL
            ORDER BY connected_at_utc DESC
            """,
            connection);
        using var reader = command.ExecuteReader();

        var sessions = new List<TunnelSession>();
        while (reader.Read())
        {
            sessions.Add(new TunnelSession(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetInt32(5),
                reader.GetInt32(6)));
        }

        return sessions;
    }

    public IReadOnlyList<HealthSample> ListHealthSamples()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, device_id, state, severity, latency_ms, jitter_ms, packet_loss_percent, throughput_mbps, signal_strength_percent, dns_reachable, route_healthy, sampled_at_utc, message
            FROM health_samples
            ORDER BY sampled_at_utc DESC
            """,
            connection);
        using var reader = command.ExecuteReader();

        var samples = new List<HealthSample>();
        while (reader.Read())
        {
            samples.Add(new HealthSample(
                reader.GetString(0),
                reader.GetString(1),
                ParseConnectionState(reader.GetString(2)),
                ParseHealthSeverity(reader.GetString(3)),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetDecimal(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetBoolean(9),
                reader.GetBoolean(10),
                reader.GetFieldValue<DateTimeOffset>(11),
                reader.GetString(12)));
        }

        return samples;
    }

    public IReadOnlyList<Alert> ListAlerts()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, severity, title, description, target_type, target_id, created_at_utc
            FROM alerts
            ORDER BY created_at_utc DESC
            """,
            connection);
        using var reader = command.ExecuteReader();

        var alerts = new List<Alert>();
        while (reader.Read())
        {
            alerts.Add(new Alert(
                reader.GetString(0),
                ParseHealthSeverity(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetFieldValue<DateTimeOffset>(6)));
        }

        return alerts;
    }

    public IReadOnlyList<AuthProviderConfig> ListAuthProviders()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, name, type, issuer, client_id, mfa_claim_paths, silent_sso_enabled
            FROM auth_providers
            ORDER BY name
            """,
            connection);
        using var reader = command.ExecuteReader();

        var providers = new List<AuthProviderConfig>();
        while (reader.Read())
        {
            providers.Add(new AuthProviderConfig(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                ReadStringArray(reader, 5),
                reader.GetBoolean(6)));
        }

        return providers;
    }

    public IReadOnlyList<AuditEvent> ListAuditEvents()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, actor, action, target_type, target_id, created_at_utc, outcome, detail
            FROM audit_events
            ORDER BY created_at_utc DESC
            """,
            connection);
        using var reader = command.ExecuteReader();

        var events = new List<AuditEvent>();
        while (reader.Read())
        {
            events.Add(new AuditEvent(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return events;
    }
}
