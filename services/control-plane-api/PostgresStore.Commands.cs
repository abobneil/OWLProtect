using Npgsql;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed partial class PostgresStore
{
    public AdminAccount LoginAdmin(string username, string password)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, username, password_hash, role, must_change_password, mfa_enrolled
            FROM admins
            WHERE lower(username) = lower(@username)
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("username", username);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Invalid admin credentials.");
        }

        var admin = MapAdmin(reader);
        if (!PasswordProtector.Verify(password, admin.Password))
        {
            throw new InvalidOperationException("Invalid admin credentials.");
        }

        reader.Close();
        AddAudit(connection, "admin", "admin-login", "admin", admin.Id, "success", "Admin authenticated with local bootstrap account.");
        return admin;
    }

    public User LoginUser(string username)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, enabled_at_utc
            FROM users
            WHERE lower(username) = lower(@username)
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("username", username);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("User not found.");
        }

        var user = MapUser(reader);
        reader.Close();

        if (!user.Enabled)
        {
            AddAudit(connection, username, "test-user-login", "user", user.Id, "failure", "Login rejected because the test user is disabled.");
            throw new InvalidOperationException("User is disabled.");
        }

        AddAudit(connection, username, "test-user-login", "user", user.Id, "success", "Passwordless local test-user login accepted.");
        return user;
    }

    public AdminAccount UpdateAdminPassword(string currentPassword, string newPassword)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = GetBootstrapAdmin(connection, transaction);
        if (!PasswordProtector.Verify(currentPassword, existing.Password))
        {
            throw new InvalidOperationException("Current password is incorrect.");
        }

        using var update = new NpgsqlCommand(
            """
            UPDATE admins
            SET password_hash = @password_hash,
                must_change_password = FALSE,
                updated_at_utc = NOW()
            WHERE id = @id
            RETURNING id, username, password_hash, role, must_change_password, mfa_enrolled
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("id", existing.Id);
        update.Parameters.AddWithValue("password_hash", PasswordProtector.Hash(newPassword));

        using var reader = update.ExecuteReader();
        reader.Read();
        var updated = MapAdmin(reader);
        reader.Close();

        AddAudit(connection, transaction, "admin", "password-change", "admin", updated.Id, "success", "Bootstrap admin password changed.");
        transaction.Commit();
        return updated;
    }

    public AdminAccount EnrollAdminMfa()
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var update = new NpgsqlCommand(
            """
            UPDATE admins
            SET mfa_enrolled = TRUE,
                updated_at_utc = NOW()
            WHERE username = @username
            RETURNING id, username, password_hash, role, must_change_password, mfa_enrolled
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("username", BootstrapAdminUsername);

        using var reader = update.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("Bootstrap admin account was not found.");
        }

        var updated = MapAdmin(reader);
        reader.Close();

        AddAudit(connection, transaction, "admin", "mfa-enroll", "admin", updated.Id, "success", "Bootstrap admin enrolled MFA.");
        transaction.Commit();
        return updated;
    }

    public User EnableUser(string userId, string actor)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var update = new NpgsqlCommand(
            """
            UPDATE users
            SET enabled = TRUE,
                enabled_at_utc = CASE WHEN test_account THEN NOW() ELSE enabled_at_utc END,
                updated_at_utc = NOW()
            WHERE id = @id
            RETURNING id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, tenant_id, enabled_at_utc
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("id", userId);

        using var reader = update.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("User not found.");
        }

        var updated = MapUser(reader);
        reader.Close();

        if (updated.TestAccount)
        {
            AddAlert(connection, transaction, HealthSeverity.Yellow, "Test account enabled", "The seeded test account was enabled and will be auto-disabled within one hour.", "user", updated.Id, updated.TenantId);
        }

        AddAudit(connection, transaction, actor, "enable-user", "user", updated.Id, "success", "User enabled.", updated.TenantId);
        transaction.Commit();
        if (updated.TestAccount)
        {
            _eventPublisher.Publish(ControlPlaneStreamTopics.Alerts, "created", updated.Id);
        }

        return updated;
    }

    public User UpsertUser(User user, string actor)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            INSERT INTO users (id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, tenant_id, enabled_at_utc, updated_at_utc)
            VALUES (@id, @username, @display_name, @enabled, @test_account, @provider_type, @group_ids, @policy_ids, @tenant_id, @enabled_at_utc, NOW())
            ON CONFLICT (id) DO UPDATE
            SET username = EXCLUDED.username,
                display_name = EXCLUDED.display_name,
                enabled = EXCLUDED.enabled,
                test_account = EXCLUDED.test_account,
                provider_type = EXCLUDED.provider_type,
                group_ids = EXCLUDED.group_ids,
                policy_ids = EXCLUDED.policy_ids,
                tenant_id = EXCLUDED.tenant_id,
                enabled_at_utc = EXCLUDED.enabled_at_utc,
                updated_at_utc = NOW()
            RETURNING id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, tenant_id, enabled_at_utc
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
        command.Parameters.AddWithValue("enabled_at_utc", user.Enabled && user.TestAccount ? DateTimeOffset.UtcNow : DBNull.Value);

        using var reader = command.ExecuteReader();
        reader.Read();
        var updated = MapUser(reader);
        reader.Close();

        AddAudit(connection, transaction, actor, "upsert-user", "user", updated.Id, "success", "User record created or updated.", updated.TenantId);
        transaction.Commit();
        return updated;
    }

    public AdminAccount UpsertAdmin(AdminAccount admin, string actor)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            INSERT INTO admins (id, username, password_hash, role, must_change_password, mfa_enrolled, updated_at_utc)
            VALUES (@id, @username, @password_hash, @role, @must_change_password, @mfa_enrolled, NOW())
            ON CONFLICT (id) DO UPDATE
            SET username = EXCLUDED.username,
                password_hash = EXCLUDED.password_hash,
                role = EXCLUDED.role,
                must_change_password = EXCLUDED.must_change_password,
                mfa_enrolled = EXCLUDED.mfa_enrolled,
                updated_at_utc = NOW()
            RETURNING id, username, password_hash, role, must_change_password, mfa_enrolled
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", admin.Id);
        command.Parameters.AddWithValue("username", admin.Username);
        command.Parameters.AddWithValue("password_hash", admin.Password);
        command.Parameters.AddWithValue("role", admin.Role.ToString());
        command.Parameters.AddWithValue("must_change_password", admin.MustChangePassword);
        command.Parameters.AddWithValue("mfa_enrolled", admin.MfaEnrolled);

        using var reader = command.ExecuteReader();
        reader.Read();
        var updated = MapAdmin(reader);
        reader.Close();

        AddAudit(connection, transaction, actor, "upsert-admin", "admin", updated.Id, "success", "Admin record created or updated.");
        transaction.Commit();
        return updated;
    }

    public bool DeleteAdmin(string adminId, string actor)
    {
        if (string.Equals(adminId, GetBootstrapAdminId(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Bootstrap admin cannot be deleted.");
        }

        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            DELETE FROM admins
            WHERE id = @id
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", adminId);
        var deleted = command.ExecuteNonQuery() > 0;
        if (deleted)
        {
            AddAudit(connection, transaction, actor, "delete-admin", "admin", adminId, "success", "Admin record deleted.");
        }

        transaction.Commit();
        return deleted;
    }

    public void DeleteUser(string userId)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteSessions = new NpgsqlCommand("DELETE FROM user_sessions WHERE user_id = @user_id", connection, transaction))
        {
            deleteSessions.Parameters.AddWithValue("user_id", userId);
            deleteSessions.ExecuteNonQuery();
        }

        using (var deleteUser = new NpgsqlCommand("DELETE FROM users WHERE id = @id", connection, transaction))
        {
            deleteUser.Parameters.AddWithValue("id", userId);
            deleteUser.ExecuteNonQuery();
        }

        AddAudit(connection, transaction, "admin", "delete-user", "user", userId, "success", "User record deleted.");
        transaction.Commit();
        _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "deleted-subject", userId);
    }

    public User DisableUser(string userId, string actor, string reason)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var update = new NpgsqlCommand(
            """
            UPDATE users
            SET enabled = FALSE,
                enabled_at_utc = NULL,
                updated_at_utc = NOW()
            WHERE id = @id
            RETURNING id, username, display_name, enabled, test_account, provider_type, group_ids, policy_ids, tenant_id, enabled_at_utc
            """,
            connection,
            transaction);
        update.Parameters.AddWithValue("id", userId);

        using var reader = update.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException("User not found.");
        }

        var updated = MapUser(reader);
        reader.Close();

        using (var revoke = new NpgsqlCommand(
                   """
                   UPDATE user_sessions
                   SET revoked_at_utc = NOW()
                   WHERE user_id = @user_id
                     AND revoked_at_utc IS NULL
                   """,
                   connection,
                   transaction))
        {
            revoke.Parameters.AddWithValue("user_id", userId);
            revoke.ExecuteNonQuery();
        }

        if (updated.TestAccount)
        {
            AddAlert(connection, transaction, HealthSeverity.Red, "Test account auto-disabled", "The seeded test account was disabled and all active sessions were revoked.", "user", updated.Id, updated.TenantId);
        }

        AddAudit(connection, transaction, actor, "disable-user", "user", updated.Id, "success", reason, updated.TenantId);
        transaction.Commit();
        if (updated.TestAccount)
        {
            _eventPublisher.Publish(ControlPlaneStreamTopics.Alerts, "created", updated.Id);
        }

        _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "revoked-subject", updated.Id);
        return updated;
    }

    public Gateway UpsertGatewayHeartbeat(Gateway gateway)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            INSERT INTO gateways (id, name, region, health, load_percent, peer_count, cpu_percent, memory_percent, latency_ms, tenant_id, last_heartbeat_utc)
            VALUES (@id, @name, @region, @health, @load_percent, @peer_count, @cpu_percent, @memory_percent, @latency_ms, @tenant_id, NOW())
            ON CONFLICT (id) DO UPDATE
            SET name = EXCLUDED.name,
                region = EXCLUDED.region,
                health = EXCLUDED.health,
                load_percent = EXCLUDED.load_percent,
                peer_count = EXCLUDED.peer_count,
                cpu_percent = EXCLUDED.cpu_percent,
                memory_percent = EXCLUDED.memory_percent,
                latency_ms = EXCLUDED.latency_ms,
                tenant_id = EXCLUDED.tenant_id,
                last_heartbeat_utc = NOW()
            RETURNING id, name, region, health, load_percent, peer_count, cpu_percent, memory_percent, latency_ms, tenant_id
            """,
            connection,
            transaction);
        BindGateway(command, gateway);

        using var reader = command.ExecuteReader();
        reader.Read();
        var updated = MapGateway(reader);
        reader.Close();

        AddAudit(connection, transaction, "gateway", "heartbeat", "gateway", updated.Id, "success", $"Gateway {updated.Name} reported health {updated.Health}.", updated.TenantId);
        transaction.Commit();
        _eventPublisher.Publish(ControlPlaneStreamTopics.GatewayHealth, "upserted", updated.Id);
        return updated;
    }

    public void AddHealthSample(HealthSample sample)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            INSERT INTO health_samples (id, device_id, state, severity, latency_ms, jitter_ms, packet_loss_percent, throughput_mbps, signal_strength_percent, dns_reachable, route_healthy, sampled_at_utc, message, tenant_id)
            VALUES (@id, @device_id, @state, @severity, @latency_ms, @jitter_ms, @packet_loss_percent, @throughput_mbps, @signal_strength_percent, @dns_reachable, @route_healthy, @sampled_at_utc, @message, @tenant_id)
            """,
            connection);
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
        command.ExecuteNonQuery();
        _eventPublisher.Publish(ControlPlaneStreamTopics.Telemetry, "recorded", sample.DeviceId);
    }

    public Device UpsertDevice(Device device)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            INSERT INTO devices (id, name, user_id, city, country, public_ip, managed, compliant, posture_score, connection_state, last_seen_utc, tenant_id, registration_state, enrollment_kind, hardware_key, serial_number, operating_system, registered_at_utc, last_enrollment_at_utc, disabled_at_utc, compliance_reasons)
            VALUES (@id, @name, @user_id, @city, @country, @public_ip, @managed, @compliant, @posture_score, @connection_state, @last_seen_utc, @tenant_id, @registration_state, @enrollment_kind, @hardware_key, @serial_number, @operating_system, @registered_at_utc, @last_enrollment_at_utc, @disabled_at_utc, @compliance_reasons)
            ON CONFLICT (id) DO UPDATE
            SET name = EXCLUDED.name,
                user_id = EXCLUDED.user_id,
                city = EXCLUDED.city,
                country = EXCLUDED.country,
                public_ip = EXCLUDED.public_ip,
                managed = EXCLUDED.managed,
                compliant = EXCLUDED.compliant,
                posture_score = EXCLUDED.posture_score,
                connection_state = EXCLUDED.connection_state,
                last_seen_utc = EXCLUDED.last_seen_utc,
                tenant_id = EXCLUDED.tenant_id,
                registration_state = EXCLUDED.registration_state,
                enrollment_kind = EXCLUDED.enrollment_kind,
                hardware_key = EXCLUDED.hardware_key,
                serial_number = EXCLUDED.serial_number,
                operating_system = EXCLUDED.operating_system,
                registered_at_utc = EXCLUDED.registered_at_utc,
                last_enrollment_at_utc = EXCLUDED.last_enrollment_at_utc,
                disabled_at_utc = EXCLUDED.disabled_at_utc,
                compliance_reasons = EXCLUDED.compliance_reasons
            RETURNING id, name, user_id, city, country, public_ip, managed, compliant, posture_score, connection_state, last_seen_utc, tenant_id, registration_state, enrollment_kind, hardware_key, serial_number, operating_system, registered_at_utc, last_enrollment_at_utc, disabled_at_utc, compliance_reasons
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

        using var reader = command.ExecuteReader();
        reader.Read();
        var updated = MapDevice(reader);
        reader.Close();

        AddAudit(connection, transaction, "admin", "upsert-device", "device", updated.Id, "success", "Device record created or updated.", updated.TenantId);
        transaction.Commit();
        return updated;
    }

    public void DeleteDevice(string deviceId)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteSessions = new NpgsqlCommand("DELETE FROM user_sessions WHERE device_id = @device_id", connection, transaction))
        {
            deleteSessions.Parameters.AddWithValue("device_id", deviceId);
            deleteSessions.ExecuteNonQuery();
        }

        using (var deleteHealth = new NpgsqlCommand("DELETE FROM health_samples WHERE device_id = @device_id", connection, transaction))
        {
            deleteHealth.Parameters.AddWithValue("device_id", deviceId);
            deleteHealth.ExecuteNonQuery();
        }

        using (var deleteDevice = new NpgsqlCommand("DELETE FROM devices WHERE id = @id", connection, transaction))
        {
            deleteDevice.Parameters.AddWithValue("id", deviceId);
            deleteDevice.ExecuteNonQuery();
        }

        AddAudit(connection, transaction, "admin", "delete-device", "device", deviceId, "success", "Device record deleted.");
        transaction.Commit();
        _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "deleted-device", deviceId);
        _eventPublisher.Publish(ControlPlaneStreamTopics.Telemetry, "deleted-device", deviceId);
    }

    public bool DisableExpiredTestUser()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM users
            WHERE username = 'user'
              AND enabled = TRUE
              AND enabled_at_utc IS NOT NULL
              AND enabled_at_utc + INTERVAL '1 hour' <= NOW()
            LIMIT 1
            """,
            connection);
        var userId = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        DisableUser(userId, "scheduler", "Automatic disable for seeded test user after one hour.");
        return true;
    }

    public bool ValidatePrivilegedOperation(bool stepUpSatisfied)
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT must_change_password, mfa_enrolled
            FROM admins
            WHERE username = @username
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("username", BootstrapAdminUsername);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        return reader.GetBoolean(1) && !reader.GetBoolean(0) && stepUpSatisfied;
    }

    private string GetBootstrapAdminId()
    {
        using var connection = _dataSource.OpenConnection();
        using var command = new NpgsqlCommand(
            """
            SELECT id
            FROM admins
            WHERE username = @username
            LIMIT 1
            """,
            connection);
        command.Parameters.AddWithValue("username", BootstrapAdminUsername);
        var adminId = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(adminId))
        {
            throw new InvalidOperationException("Bootstrap admin account was not found.");
        }

        return adminId;
    }

    public void DeleteGateway(string gatewayId)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteSessions = new NpgsqlCommand("DELETE FROM user_sessions WHERE gateway_id = @gateway_id", connection, transaction))
        {
            deleteSessions.Parameters.AddWithValue("gateway_id", gatewayId);
            deleteSessions.ExecuteNonQuery();
        }

        using (var deleteGateway = new NpgsqlCommand("DELETE FROM gateways WHERE id = @id", connection, transaction))
        {
            deleteGateway.Parameters.AddWithValue("id", gatewayId);
            deleteGateway.ExecuteNonQuery();
        }

        AddAudit(connection, transaction, "admin", "delete-gateway", "gateway", gatewayId, "success", "Gateway record deleted.");
        transaction.Commit();
        _eventPublisher.Publish(ControlPlaneStreamTopics.GatewayHealth, "deleted", gatewayId);
        _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "deleted-gateway", gatewayId);
    }

    public PolicyRule UpsertPolicy(PolicyRule policy)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            INSERT INTO policies (id, name, cidrs, dns_zones, ports, mode, tenant_id, priority, target_group_ids, require_managed, require_compliant, minimum_posture_score, allowed_registration_states, updated_at_utc)
            VALUES (@id, @name, @cidrs, @dns_zones, @ports, @mode, @tenant_id, @priority, @target_group_ids, @require_managed, @require_compliant, @minimum_posture_score, @allowed_registration_states, NOW())
            ON CONFLICT (id) DO UPDATE
            SET name = EXCLUDED.name,
                cidrs = EXCLUDED.cidrs,
                dns_zones = EXCLUDED.dns_zones,
                ports = EXCLUDED.ports,
                mode = EXCLUDED.mode,
                tenant_id = EXCLUDED.tenant_id,
                priority = EXCLUDED.priority,
                target_group_ids = EXCLUDED.target_group_ids,
                require_managed = EXCLUDED.require_managed,
                require_compliant = EXCLUDED.require_compliant,
                minimum_posture_score = EXCLUDED.minimum_posture_score,
                allowed_registration_states = EXCLUDED.allowed_registration_states,
                updated_at_utc = NOW()
            RETURNING id, name, cidrs, dns_zones, ports, mode, tenant_id, priority, target_group_ids, require_managed, require_compliant, minimum_posture_score, allowed_registration_states
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

        using var reader = command.ExecuteReader();
        reader.Read();
        var updated = new PolicyRule(
            reader.GetString(0),
            reader.GetString(1),
            ReadStringArray(reader, 2),
            ReadStringArray(reader, 3),
            ReadIntArray(reader, 4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetInt32(7),
            ReadStringArray(reader, 8),
            reader.GetBoolean(9),
            reader.GetBoolean(10),
            reader.GetInt32(11),
            ReadStringArray(reader, 12).Select(value => Enum.Parse<DeviceRegistrationState>(value, ignoreCase: true)).ToArray());
        reader.Close();

        AddAudit(connection, transaction, "admin", "upsert-policy", "policy", updated.Id, "success", "Policy record created or updated.", updated.TenantId);
        transaction.Commit();
        return updated;
    }

    public void DeletePolicy(string policyId)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deletePolicy = new NpgsqlCommand("DELETE FROM policies WHERE id = @id", connection, transaction))
        {
            deletePolicy.Parameters.AddWithValue("id", policyId);
            deletePolicy.ExecuteNonQuery();
        }

        AddAudit(connection, transaction, "admin", "delete-policy", "policy", policyId, "success", "Policy record deleted.");
        transaction.Commit();
    }

    public TunnelSession UpsertSession(TunnelSession session)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            INSERT INTO user_sessions (id, user_id, device_id, gateway_id, connected_at_utc, handshake_age_seconds, throughput_mbps, tenant_id, policy_bundle_version, authorized_at_utc, revalidate_after_utc, revoked_at_utc)
            VALUES (@id, @user_id, @device_id, @gateway_id, @connected_at_utc, @handshake_age_seconds, @throughput_mbps, @tenant_id, @policy_bundle_version, @authorized_at_utc, @revalidate_after_utc, NULL)
            ON CONFLICT (id) DO UPDATE
            SET user_id = EXCLUDED.user_id,
                device_id = EXCLUDED.device_id,
                gateway_id = EXCLUDED.gateway_id,
                connected_at_utc = EXCLUDED.connected_at_utc,
                handshake_age_seconds = EXCLUDED.handshake_age_seconds,
                throughput_mbps = EXCLUDED.throughput_mbps,
                tenant_id = EXCLUDED.tenant_id,
                policy_bundle_version = EXCLUDED.policy_bundle_version,
                authorized_at_utc = EXCLUDED.authorized_at_utc,
                revalidate_after_utc = EXCLUDED.revalidate_after_utc,
                revoked_at_utc = NULL
            RETURNING id, user_id, device_id, gateway_id, connected_at_utc, handshake_age_seconds, throughput_mbps, tenant_id, policy_bundle_version, authorized_at_utc, revalidate_after_utc
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

        using var reader = command.ExecuteReader();
        reader.Read();
        var updated = new TunnelSession(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10));
        reader.Close();

        AddAudit(connection, transaction, "admin", "upsert-session", "session", updated.Id, "success", "Session record created or updated.", updated.TenantId);
        transaction.Commit();
        _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "upserted", updated.Id);
        return updated;
    }

    public bool RevokeSession(string sessionId, string actor, string reason)
    {
        using var connection = _dataSource.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using var command = new NpgsqlCommand(
            """
            UPDATE user_sessions
            SET revoked_at_utc = NOW()
            WHERE id = @id
              AND revoked_at_utc IS NULL
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("id", sessionId);
        var affected = command.ExecuteNonQuery() > 0;
        if (affected)
        {
            AddAudit(connection, transaction, actor, "revoke-session", "session", sessionId, "success", reason);
        }

        transaction.Commit();
        if (affected)
        {
            _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "revoked", sessionId);
        }

        return affected;
    }
}
