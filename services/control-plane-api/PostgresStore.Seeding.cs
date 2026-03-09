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
        var seed = SeedData.CreateSeedDataset(_bootstrapSettingsProvider.GetSettings());

        await InsertAdminAsync(connection, transaction, SeedData.CreateDefaultAdmin(_bootstrapAdminCredentialsProvider.GetBootstrapAdminCredentials()), cancellationToken);
        foreach (var user in seed.Users)
        {
            await InsertUserAsync(connection, transaction, user, cancellationToken);
        }

        foreach (var device in seed.Devices)
        {
            await InsertDeviceAsync(connection, transaction, device, cancellationToken);
        }

        foreach (var pool in seed.GatewayPools)
        {
            await InsertGatewayPoolAsync(connection, transaction, pool, cancellationToken);
        }

        foreach (var gateway in seed.Gateways)
        {
            await InsertGatewayAsync(connection, transaction, gateway, cancellationToken);
        }

        foreach (var policy in seed.Policies)
        {
            await InsertPolicyAsync(connection, transaction, policy, cancellationToken);
        }

        foreach (var session in seed.Sessions)
        {
            await InsertSessionAsync(connection, transaction, session, cancellationToken);
        }

        foreach (var sample in seed.HealthSamples)
        {
            await InsertHealthSampleAsync(connection, transaction, sample, cancellationToken);
        }

        foreach (var alert in seed.Alerts)
        {
            await InsertAlertAsync(connection, transaction, alert, cancellationToken);
        }

        foreach (var provider in seed.AuthProviders)
        {
            await InsertAuthProviderAsync(connection, transaction, provider, cancellationToken);
        }

        foreach (var auditEvent in seed.AuditEvents)
        {
            await InsertAuditEventAsync(connection, transaction, auditEvent, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Seeded PostgreSQL persistence store with bootstrap data.");
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
            INSERT INTO users (id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, tenant_id, enabled_at_utc)
            VALUES (@id, @username, @display_name, @enabled, @test_account, @provider_type, @group_ids, @policy_ids, @tenant_id, @enabled_at_utc)
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
        command.Parameters.AddWithValue("tenant_id", user.TenantId);
        command.Parameters.AddWithValue("enabled_at_utc", user.Enabled ? DateTimeOffset.UtcNow : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDeviceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Device device, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO devices (id, name, user_id, city, country, public_ip, managed, compliant, posture_score, connection_state, last_seen_utc, tenant_id, registration_state, enrollment_kind, hardware_key, serial_number, operating_system, registered_at_utc, last_enrollment_at_utc, disabled_at_utc, compliance_reasons)
            VALUES (@id, @name, @user_id, @city, @country, @public_ip, @managed, @compliant, @posture_score, @connection_state, @last_seen_utc, @tenant_id, @registration_state, @enrollment_kind, @hardware_key, @serial_number, @operating_system, @registered_at_utc, @last_enrollment_at_utc, @disabled_at_utc, @compliance_reasons)
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
        command.Parameters.AddWithValue("tenant_id", device.TenantId);
        command.Parameters.AddWithValue("registration_state", device.RegistrationState.ToString());
        command.Parameters.AddWithValue("enrollment_kind", device.EnrollmentKind.ToString());
        command.Parameters.AddWithValue("hardware_key", device.HardwareKey);
        command.Parameters.AddWithValue("serial_number", device.SerialNumber);
        command.Parameters.AddWithValue("operating_system", device.OperatingSystem);
        command.Parameters.AddWithValue("registered_at_utc", (object?)device.RegisteredAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("last_enrollment_at_utc", (object?)device.LastEnrollmentAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("disabled_at_utc", (object?)device.DisabledAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("compliance_reasons", (device.ComplianceReasons ?? []).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGatewayPoolAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, GatewayPool pool, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO gateway_pools (id, name, regions, gateway_ids, tenant_id)
            VALUES (@id, @name, @regions, @gateway_ids, @tenant_id)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", pool.Id);
        command.Parameters.AddWithValue("name", pool.Name);
        command.Parameters.AddWithValue("regions", pool.Regions.ToArray());
        command.Parameters.AddWithValue("gateway_ids", pool.GatewayIds.ToArray());
        command.Parameters.AddWithValue("tenant_id", pool.TenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGatewayAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Gateway gateway, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO gateways (id, name, region, health, load_percent, peer_count, cpu_percent, memory_percent, latency_ms, tenant_id)
            VALUES (@id, @name, @region, @health, @load_percent, @peer_count, @cpu_percent, @memory_percent, @latency_ms, @tenant_id)
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
            INSERT INTO policies (id, name, cidrs, dns_zones, ports, mode, tenant_id, priority, target_group_ids, require_managed, require_compliant, minimum_posture_score, allowed_registration_states)
            VALUES (@id, @name, @cidrs, @dns_zones, @ports, @mode, @tenant_id, @priority, @target_group_ids, @require_managed, @require_compliant, @minimum_posture_score, @allowed_registration_states)
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", policy.Id);
        command.Parameters.AddWithValue("name", policy.Name);
        command.Parameters.AddWithValue("cidrs", policy.Cidrs.ToArray());
        command.Parameters.AddWithValue("dns_zones", policy.DnsZones.ToArray());
        command.Parameters.AddWithValue("ports", policy.Ports.ToArray());
        command.Parameters.AddWithValue("mode", policy.Mode);
        command.Parameters.AddWithValue("tenant_id", policy.TenantId);
        command.Parameters.AddWithValue("priority", policy.Priority);
        command.Parameters.AddWithValue("target_group_ids", (policy.TargetGroupIds ?? []).ToArray());
        command.Parameters.AddWithValue("require_managed", policy.RequireManaged);
        command.Parameters.AddWithValue("require_compliant", policy.RequireCompliant);
        command.Parameters.AddWithValue("minimum_posture_score", policy.MinimumPostureScore);
        command.Parameters.AddWithValue("allowed_registration_states", (policy.AllowedDeviceStates ?? []).Select(value => value.ToString()).ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, TunnelSession session, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO user_sessions (id, user_id, device_id, gateway_id, connected_at_utc, handshake_age_seconds, throughput_mbps, tenant_id, policy_bundle_version, authorized_at_utc, revalidate_after_utc)
            VALUES (@id, @user_id, @device_id, @gateway_id, @connected_at_utc, @handshake_age_seconds, @throughput_mbps, @tenant_id, @policy_bundle_version, @authorized_at_utc, @revalidate_after_utc)
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
        command.Parameters.AddWithValue("tenant_id", session.TenantId);
        command.Parameters.AddWithValue("policy_bundle_version", string.IsNullOrWhiteSpace(session.PolicyBundleVersion) ? DBNull.Value : session.PolicyBundleVersion);
        command.Parameters.AddWithValue("authorized_at_utc", (object?)session.AuthorizedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("revalidate_after_utc", (object?)session.RevalidateAfterUtc ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertHealthSampleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, HealthSample sample, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO health_samples (id, device_id, state, severity, latency_ms, jitter_ms, packet_loss_percent, throughput_mbps, signal_strength_percent, dns_reachable, route_healthy, sampled_at_utc, message, tenant_id)
            VALUES (@id, @device_id, @state, @severity, @latency_ms, @jitter_ms, @packet_loss_percent, @throughput_mbps, @signal_strength_percent, @dns_reachable, @route_healthy, @sampled_at_utc, @message, @tenant_id)
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
        command.Parameters.AddWithValue("tenant_id", sample.TenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAlertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Alert alert, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO alerts (id, severity, title, description, target_type, target_id, created_at_utc, tenant_id)
            VALUES (@id, @severity, @title, @description, @target_type, @target_id, @created_at_utc, @tenant_id)
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
        command.Parameters.AddWithValue("tenant_id", alert.TenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuthProviderAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AuthProviderConfig provider, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO auth_providers (id, name, type, issuer, client_id, username_claim_paths, group_claim_paths, mfa_claim_paths, require_mfa, silent_sso_enabled, tenant_id)
            VALUES (@id, @name, @type, @issuer, @client_id, @username_claim_paths, @group_claim_paths, @mfa_claim_paths, @require_mfa, @silent_sso_enabled, @tenant_id)
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
        command.Parameters.AddWithValue("tenant_id", provider.TenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertAuditEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO audit_events (id, sequence_number, actor, action, target_type, target_id, created_at_utc, outcome, detail, previous_event_hash, event_hash, tenant_id)
            VALUES (@id, @sequence_number, @actor, @action, @target_type, @target_id, @created_at_utc, @outcome, @detail, @previous_event_hash, @event_hash, @tenant_id)
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
        command.Parameters.AddWithValue("tenant_id", auditEvent.TenantId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
