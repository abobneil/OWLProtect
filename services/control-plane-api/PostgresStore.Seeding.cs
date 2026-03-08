using Npgsql;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed partial class PostgresStore
{
    private async Task SeedIfEmptyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var check = new NpgsqlCommand("SELECT COUNT(1) FROM admins", connection);
        var existingAdmins = Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken));
        if (existingAdmins > 0)
        {
            return;
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await InsertAdminAsync(connection, transaction, SeedData.CreateDefaultAdmin(_bootstrapAdminCredentialsProvider.GetBootstrapAdminCredentials()), cancellationToken);
        foreach (var user in SeedData.Users)
        {
            await InsertUserAsync(connection, transaction, user, cancellationToken);
        }

        foreach (var device in SeedData.Devices)
        {
            await InsertDeviceAsync(connection, transaction, device, cancellationToken);
        }

        foreach (var pool in SeedData.GatewayPools)
        {
            await InsertGatewayPoolAsync(connection, transaction, pool, cancellationToken);
        }

        foreach (var gateway in SeedData.Gateways)
        {
            await InsertGatewayAsync(connection, transaction, gateway, cancellationToken);
        }

        foreach (var policy in SeedData.Policies)
        {
            await InsertPolicyAsync(connection, transaction, policy, cancellationToken);
        }

        foreach (var session in SeedData.Sessions)
        {
            await InsertSessionAsync(connection, transaction, session, cancellationToken);
        }

        foreach (var sample in SeedData.HealthSamples)
        {
            await InsertHealthSampleAsync(connection, transaction, sample, cancellationToken);
        }

        foreach (var alert in SeedData.Alerts)
        {
            await InsertAlertAsync(connection, transaction, alert, cancellationToken);
        }

        foreach (var provider in SeedData.AuthProviders)
        {
            await InsertAuthProviderAsync(connection, transaction, provider, cancellationToken);
        }

        foreach (var auditEvent in SeedData.AuditEvents)
        {
            await InsertAuditEventAsync(connection, transaction, auditEvent, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Seeded PostgreSQL persistence store with scaffold data.");
    }

    private static async Task InsertAdminAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AdminAccount admin, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO admins (id, username, password_hash, role, must_change_password, mfa_enrolled)
            VALUES (@id, @username, @password_hash, @role, @must_change_password, @mfa_enrolled)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", admin.Id);
        command.Parameters.AddWithValue("username", admin.Username);
        command.Parameters.AddWithValue("password_hash", admin.Password);
        command.Parameters.AddWithValue("role", admin.Role.ToString());
        command.Parameters.AddWithValue("must_change_password", admin.MustChangePassword);
        command.Parameters.AddWithValue("mfa_enrolled", admin.MfaEnrolled);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertUserAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, User user, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO users (id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, enabled_at_utc)
            VALUES (@id, @username, @display_name, @enabled, @test_account, @provider_type, @group_ids, @policy_ids, @enabled_at_utc)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", user.Id);
        command.Parameters.AddWithValue("username", user.Username);
        command.Parameters.AddWithValue("display_name", user.DisplayName);
        command.Parameters.AddWithValue("enabled", user.Enabled);
        command.Parameters.AddWithValue("test_account", user.TestAccount);
        command.Parameters.AddWithValue("provider_type", user.Provider);
        command.Parameters.AddWithValue("group_ids", user.GroupIds.ToArray());
        command.Parameters.AddWithValue("policy_ids", user.PolicyIds.ToArray());
        command.Parameters.AddWithValue("enabled_at_utc", user.Enabled ? DateTimeOffset.UtcNow : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDeviceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Device device, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO devices (id, name, user_id, city, country, public_ip, managed, compliant, posture_score, connection_state, last_seen_utc)
            VALUES (@id, @name, @user_id, @city, @country, @public_ip, @managed, @compliant, @posture_score, @connection_state, @last_seen_utc)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", device.Id);
        command.Parameters.AddWithValue("name", device.Name);
        command.Parameters.AddWithValue("user_id", device.UserId);
        command.Parameters.AddWithValue("city", device.City);
        command.Parameters.AddWithValue("country", device.Country);
        command.Parameters.AddWithValue("public_ip", device.PublicIp);
        command.Parameters.AddWithValue("managed", device.Managed);
        command.Parameters.AddWithValue("compliant", device.Compliant);
        command.Parameters.AddWithValue("posture_score", device.PostureScore);
        command.Parameters.AddWithValue("connection_state", device.ConnectionState.ToString());
        command.Parameters.AddWithValue("last_seen_utc", device.LastSeenUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGatewayPoolAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, GatewayPool pool, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO gateway_pools (id, name, regions, gateway_ids)
            VALUES (@id, @name, @regions, @gateway_ids)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", pool.Id);
        command.Parameters.AddWithValue("name", pool.Name);
        command.Parameters.AddWithValue("regions", pool.Regions.ToArray());
        command.Parameters.AddWithValue("gateway_ids", pool.GatewayIds.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGatewayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Gateway gateway, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO gateways (id, name, region, health, load_percent, peer_count, cpu_percent, memory_percent, latency_ms)
            VALUES (@id, @name, @region, @health, @load_percent, @peer_count, @cpu_percent, @memory_percent, @latency_ms)
            """,
            connection,
            transaction);
        BindGateway(command, gateway);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPolicyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PolicyRule policy, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO policies (id, name, cidrs, dns_zones, ports, mode)
            VALUES (@id, @name, @cidrs, @dns_zones, @ports, @mode)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", policy.Id);
        command.Parameters.AddWithValue("name", policy.Name);
        command.Parameters.AddWithValue("cidrs", policy.Cidrs.ToArray());
        command.Parameters.AddWithValue("dns_zones", policy.DnsZones.ToArray());
        command.Parameters.AddWithValue("ports", policy.Ports.ToArray());
        command.Parameters.AddWithValue("mode", policy.Mode);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TunnelSession session, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_sessions (id, user_id, device_id, gateway_id, connected_at_utc, handshake_age_seconds, throughput_mbps)
            VALUES (@id, @user_id, @device_id, @gateway_id, @connected_at_utc, @handshake_age_seconds, @throughput_mbps)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", session.Id);
        command.Parameters.AddWithValue("user_id", session.UserId);
        command.Parameters.AddWithValue("device_id", session.DeviceId);
        command.Parameters.AddWithValue("gateway_id", session.GatewayId);
        command.Parameters.AddWithValue("connected_at_utc", session.ConnectedAtUtc);
        command.Parameters.AddWithValue("handshake_age_seconds", session.HandshakeAgeSeconds);
        command.Parameters.AddWithValue("throughput_mbps", session.ThroughputMbps);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertHealthSampleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, HealthSample sample, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO health_samples (id, device_id, state, severity, latency_ms, jitter_ms, packet_loss_percent, throughput_mbps, signal_strength_percent, dns_reachable, route_healthy, sampled_at_utc, message)
            VALUES (@id, @device_id, @state, @severity, @latency_ms, @jitter_ms, @packet_loss_percent, @throughput_mbps, @signal_strength_percent, @dns_reachable, @route_healthy, @sampled_at_utc, @message)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", sample.Id);
        command.Parameters.AddWithValue("device_id", sample.DeviceId);
        command.Parameters.AddWithValue("state", sample.State.ToString());
        command.Parameters.AddWithValue("severity", sample.Severity.ToString());
        command.Parameters.AddWithValue("latency_ms", sample.LatencyMs);
        command.Parameters.AddWithValue("jitter_ms", sample.JitterMs);
        command.Parameters.AddWithValue("packet_loss_percent", sample.PacketLossPercent);
        command.Parameters.AddWithValue("throughput_mbps", sample.ThroughputMbps);
        command.Parameters.AddWithValue("signal_strength_percent", sample.SignalStrengthPercent);
        command.Parameters.AddWithValue("dns_reachable", sample.DnsReachable);
        command.Parameters.AddWithValue("route_healthy", sample.RouteHealthy);
        command.Parameters.AddWithValue("sampled_at_utc", sample.SampledAtUtc);
        command.Parameters.AddWithValue("message", sample.Message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAlertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Alert alert, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO alerts (id, severity, title, description, target_type, target_id, created_at_utc)
            VALUES (@id, @severity, @title, @description, @target_type, @target_id, @created_at_utc)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", alert.Id);
        command.Parameters.AddWithValue("severity", alert.Severity.ToString());
        command.Parameters.AddWithValue("title", alert.Title);
        command.Parameters.AddWithValue("description", alert.Description);
        command.Parameters.AddWithValue("target_type", alert.TargetType);
        command.Parameters.AddWithValue("target_id", alert.TargetId);
        command.Parameters.AddWithValue("created_at_utc", alert.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuthProviderAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AuthProviderConfig provider, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO auth_providers (id, name, type, issuer, client_id, username_claim_paths, group_claim_paths, mfa_claim_paths, require_mfa, silent_sso_enabled)
            VALUES (@id, @name, @type, @issuer, @client_id, @username_claim_paths, @group_claim_paths, @mfa_claim_paths, @require_mfa, @silent_sso_enabled)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", provider.Id);
        command.Parameters.AddWithValue("name", provider.Name);
        command.Parameters.AddWithValue("type", provider.Type);
        command.Parameters.AddWithValue("issuer", provider.Issuer);
        command.Parameters.AddWithValue("client_id", provider.ClientId);
        command.Parameters.AddWithValue("username_claim_paths", provider.UsernameClaimPaths.ToArray());
        command.Parameters.AddWithValue("group_claim_paths", provider.GroupClaimPaths.ToArray());
        command.Parameters.AddWithValue("mfa_claim_paths", provider.MfaClaimPaths.ToArray());
        command.Parameters.AddWithValue("require_mfa", provider.RequireMfa);
        command.Parameters.AddWithValue("silent_sso_enabled", provider.SilentSsoEnabled);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_events (id, sequence_number, actor, action, target_type, target_id, created_at_utc, outcome, detail, previous_event_hash, event_hash)
            VALUES (@id, @sequence_number, @actor, @action, @target_type, @target_id, @created_at_utc, @outcome, @detail, @previous_event_hash, @event_hash)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", auditEvent.Id);
        command.Parameters.AddWithValue("sequence_number", auditEvent.Sequence);
        command.Parameters.AddWithValue("actor", auditEvent.Actor);
        command.Parameters.AddWithValue("action", auditEvent.Action);
        command.Parameters.AddWithValue("target_type", auditEvent.TargetType);
        command.Parameters.AddWithValue("target_id", auditEvent.TargetId);
        command.Parameters.AddWithValue("created_at_utc", auditEvent.CreatedAtUtc);
        command.Parameters.AddWithValue("outcome", auditEvent.Outcome);
        command.Parameters.AddWithValue("detail", auditEvent.Detail);
        command.Parameters.AddWithValue("previous_event_hash", (object?)auditEvent.PreviousEventHash ?? DBNull.Value);
        command.Parameters.AddWithValue("event_hash", auditEvent.EventHash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
