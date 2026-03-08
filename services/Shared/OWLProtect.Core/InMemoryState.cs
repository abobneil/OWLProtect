using System.Text.Json.Serialization;

namespace OWLProtect.Core;

public sealed class InMemoryState :
    IBootstrapService,
    IDashboardQueryService,
    IAdminRepository,
    IUserRepository,
    IDeviceRepository,
    IGatewayRepository,
    IGatewayPoolRepository,
    IPolicyRepository,
    ISessionRepository,
    IHealthSampleRepository,
    IAlertRepository,
    IAuthProviderConfigRepository,
    IAuditRepository,
    IAuditWriter,
    IAuditRetentionRepository
{
    private readonly Lock _gate = new();
    private AdminAccount _defaultAdmin;
    private readonly List<AdminAccount> _admins = [];
    private DateTimeOffset? _testUserEnabledAtUtc;

    private readonly List<User> _users = [.. SeedData.Users];
    private readonly List<Device> _devices = [.. SeedData.Devices];
    private readonly List<Gateway> _gateways = [.. SeedData.Gateways];
    private readonly List<GatewayPool> _gatewayPools = [.. SeedData.GatewayPools];
    private readonly List<PolicyRule> _policies = [.. SeedData.Policies];
    private readonly List<TunnelSession> _sessions = [.. SeedData.Sessions];
    private readonly List<HealthSample> _healthSamples = [.. SeedData.HealthSamples];
    private readonly List<Alert> _alerts = [.. SeedData.Alerts];
    private readonly List<AuthProviderConfig> _authProviders = [.. SeedData.AuthProviders];
    private readonly List<AuditEvent> _auditEvents = [.. SeedData.AuditEvents];
    private readonly List<AuditRetentionCheckpoint> _auditRetentionCheckpoints = [];

    public InMemoryState(IBootstrapAdminCredentialsProvider bootstrapAdminCredentialsProvider)
    {
        _defaultAdmin = SeedData.CreateDefaultAdmin(bootstrapAdminCredentialsProvider.GetBootstrapAdminCredentials());
        _admins.Add(_defaultAdmin);
    }

    public DashboardSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new DashboardSnapshot(
                [.. _admins.OrderBy(item => item.Username, StringComparer.OrdinalIgnoreCase)],
                [.. _users],
                [.. _devices],
                [.. _gateways],
                [.. _gatewayPools],
                [.. _policies],
                [.. _sessions],
                [.. _healthSamples.OrderByDescending(sample => sample.SampledAtUtc)],
                [.. _alerts.OrderByDescending(alert => alert.CreatedAtUtc)],
                [.. _authProviders],
                [.. _auditEvents.OrderByDescending(evt => evt.CreatedAtUtc)]);
        }
    }

    public BootstrapStatus GetBootstrapStatus()
    {
        lock (_gate)
        {
            var testUser = _users.Single(user => user.Username == "user");
            var disableAt = _testUserEnabledAtUtc?.AddHours(1);
            return new BootstrapStatus(
                _defaultAdmin.MustChangePassword,
                !_defaultAdmin.MfaEnrolled,
                testUser.Enabled,
                disableAt);
        }
    }

    public AdminAccount LoginAdmin(string username, string password)
    {
        lock (_gate)
        {
            if (!string.Equals(username, _defaultAdmin.Username, StringComparison.OrdinalIgnoreCase) ||
                !PasswordProtector.Verify(password, _defaultAdmin.Password))
            {
                throw new InvalidOperationException("Invalid admin credentials.");
            }

            AddAudit("admin", "admin-login", "admin", _defaultAdmin.Id, "success", "Admin authenticated with local bootstrap account.");
            return _defaultAdmin;
        }
    }

    public User LoginUser(string username)
    {
        lock (_gate)
        {
            var user = _users.SingleOrDefault(item => string.Equals(item.Username, username, StringComparison.OrdinalIgnoreCase));
            if (user is null)
            {
                throw new InvalidOperationException("User not found.");
            }

            if (!user.Enabled)
            {
                AddAudit(username, "test-user-login", "user", user.Id, "failure", "Login rejected because the test user is disabled.");
                throw new InvalidOperationException("User is disabled.");
            }

            AddAudit(username, "test-user-login", "user", user.Id, "success", "Passwordless local test-user login accepted.");
            return user;
        }
    }

    public AdminAccount UpdateAdminPassword(string currentPassword, string newPassword)
    {
        lock (_gate)
        {
            if (_defaultAdmin.Password != currentPassword)
            {
                throw new InvalidOperationException("Current password is incorrect.");
            }

            _defaultAdmin = _defaultAdmin with
            {
                Password = PasswordProtector.Hash(newPassword),
                MustChangePassword = false
            };
            SyncBootstrapAdmin();

            AddAudit("admin", "password-change", "admin", _defaultAdmin.Id, "success", "Bootstrap admin password changed.");
            return _defaultAdmin;
        }
    }

    public AdminAccount EnrollAdminMfa()
    {
        lock (_gate)
        {
            _defaultAdmin = _defaultAdmin with { MfaEnrolled = true };
            SyncBootstrapAdmin();
            AddAudit("admin", "mfa-enroll", "admin", _defaultAdmin.Id, "success", "Bootstrap admin enrolled MFA.");
            return _defaultAdmin;
        }
    }

    public AdminAccount UpsertAdmin(AdminAccount admin, string actor)
    {
        lock (_gate)
        {
            var index = _admins.FindIndex(item => item.Id == admin.Id);
            if (index >= 0)
            {
                _admins[index] = admin;
            }
            else
            {
                _admins.Add(admin);
            }

            if (admin.Id == _defaultAdmin.Id)
            {
                _defaultAdmin = admin;
            }

            AddAudit(actor, "upsert-admin", "admin", admin.Id, "success", "Admin record created or updated.");
            return admin;
        }
    }

    public bool DeleteAdmin(string adminId, string actor)
    {
        lock (_gate)
        {
            if (adminId == _defaultAdmin.Id)
            {
                throw new InvalidOperationException("Bootstrap admin cannot be deleted.");
            }

            var removed = _admins.RemoveAll(admin => admin.Id == adminId) > 0;
            if (removed)
            {
                AddAudit(actor, "delete-admin", "admin", adminId, "success", "Admin record deleted.");
            }

            return removed;
        }
    }

    public User EnableUser(string userId, string actor)
    {
        lock (_gate)
        {
            var index = _users.FindIndex(user => user.Id == userId);
            if (index < 0)
            {
                throw new InvalidOperationException("User not found.");
            }

            var updated = _users[index] with { Enabled = true };
            _users[index] = updated;

            if (updated.Username == "user")
            {
                _testUserEnabledAtUtc = DateTimeOffset.UtcNow;
                AddAlert(HealthSeverity.Yellow, "Test account enabled", "The seeded test account was enabled and will be auto-disabled within one hour.", "user", updated.Id);
            }

            AddAudit(actor, "enable-user", "user", updated.Id, "success", "User enabled.");
            return updated;
        }
    }

    public User DisableUser(string userId, string actor, string reason)
    {
        lock (_gate)
        {
            var index = _users.FindIndex(user => user.Id == userId);
            if (index < 0)
            {
                throw new InvalidOperationException("User not found.");
            }

            var updated = _users[index] with { Enabled = false };
            _users[index] = updated;

            var sessionIndexes = _sessions
                .Select((session, index) => (session, index))
                .Where(item => item.session.UserId == userId)
                .Select(item => item.index)
                .OrderByDescending(index => index)
                .ToArray();

            foreach (var sessionIndex in sessionIndexes)
            {
                _sessions.RemoveAt(sessionIndex);
            }

            if (updated.Username == "user")
            {
                _testUserEnabledAtUtc = null;
                AddAlert(HealthSeverity.Red, "Test account auto-disabled", "The seeded test account was disabled and all active sessions were revoked.", "user", updated.Id);
            }

            AddAudit(actor, "disable-user", "user", updated.Id, "success", reason);
            return updated;
        }
    }

    public Gateway UpsertGatewayHeartbeat(Gateway gateway)
    {
        lock (_gate)
        {
            var index = _gateways.FindIndex(item => item.Id == gateway.Id);
            if (index >= 0)
            {
                _gateways[index] = gateway;
            }
            else
            {
                _gateways.Add(gateway);
            }

            AddAudit("gateway", "heartbeat", "gateway", gateway.Id, "success", $"Gateway {gateway.Name} reported health {gateway.Health}.");
            return gateway;
        }
    }

    public void AddHealthSample(HealthSample sample)
    {
        lock (_gate)
        {
            _healthSamples.Add(sample);
            if (_healthSamples.Count > 5000)
            {
                _healthSamples.RemoveRange(0, _healthSamples.Count - 5000);
            }
        }
    }

    public IReadOnlyList<ConnectionMapPoint> GetConnectionMap() =>
        Snapshot().Devices.Select(device => new ConnectionMapPoint(
            device.Id,
            device.Name,
            device.City,
            device.Country,
            device.PublicIp,
            device.ConnectionState)).ToArray();

    public User UpsertUser(User user, string actor)
    {
        lock (_gate)
        {
            var index = _users.FindIndex(item => item.Id == user.Id);
            if (index >= 0)
            {
                _users[index] = user;
            }
            else
            {
                _users.Add(user);
            }

            AddAudit(actor, "upsert-user", "user", user.Id, "success", "User record created or updated.");
            return user;
        }
    }

    public void DeleteUser(string userId)
    {
        lock (_gate)
        {
            _users.RemoveAll(user => user.Id == userId);
            _sessions.RemoveAll(session => session.UserId == userId);
            AddAudit("admin", "delete-user", "user", userId, "success", "User record deleted.");
        }
    }

    public Device UpsertDevice(Device device)
    {
        lock (_gate)
        {
            var index = _devices.FindIndex(item => item.Id == device.Id);
            if (index >= 0)
            {
                _devices[index] = device;
            }
            else
            {
                _devices.Add(device);
            }

            AddAudit("admin", "upsert-device", "device", device.Id, "success", "Device record created or updated.");
            return device;
        }
    }

    public void DeleteDevice(string deviceId)
    {
        lock (_gate)
        {
            _devices.RemoveAll(device => device.Id == deviceId);
            _sessions.RemoveAll(session => session.DeviceId == deviceId);
            _healthSamples.RemoveAll(sample => sample.DeviceId == deviceId);
            AddAudit("admin", "delete-device", "device", deviceId, "success", "Device record deleted.");
        }
    }

    public void DeleteGateway(string gatewayId)
    {
        lock (_gate)
        {
            _gateways.RemoveAll(gateway => gateway.Id == gatewayId);
            _sessions.RemoveAll(session => session.GatewayId == gatewayId);
            AddAudit("admin", "delete-gateway", "gateway", gatewayId, "success", "Gateway record deleted.");
        }
    }

    public PolicyRule UpsertPolicy(PolicyRule policy)
    {
        lock (_gate)
        {
            var index = _policies.FindIndex(item => item.Id == policy.Id);
            if (index >= 0)
            {
                _policies[index] = policy;
            }
            else
            {
                _policies.Add(policy);
            }

            AddAudit("admin", "upsert-policy", "policy", policy.Id, "success", "Policy record created or updated.");
            return policy;
        }
    }

    public void DeletePolicy(string policyId)
    {
        lock (_gate)
        {
            _policies.RemoveAll(policy => policy.Id == policyId);
            AddAudit("admin", "delete-policy", "policy", policyId, "success", "Policy record deleted.");
        }
    }

    public TunnelSession UpsertSession(TunnelSession session)
    {
        lock (_gate)
        {
            var index = _sessions.FindIndex(item => item.Id == session.Id);
            if (index >= 0)
            {
                _sessions[index] = session;
            }
            else
            {
                _sessions.Add(session);
            }

            AddAudit("admin", "upsert-session", "session", session.Id, "success", "Session record created or updated.");
            return session;
        }
    }

    public bool RevokeSession(string sessionId, string actor, string reason)
    {
        lock (_gate)
        {
            var removed = _sessions.RemoveAll(session => session.Id == sessionId) > 0;
            if (removed)
            {
                AddAudit(actor, "revoke-session", "session", sessionId, "success", reason);
            }

            return removed;
        }
    }

    public IReadOnlyList<AdminAccount> ListAdmins()
    {
        lock (_gate)
        {
            return [.. _admins.OrderBy(item => item.Username, StringComparer.OrdinalIgnoreCase)];
        }
    }

    public IReadOnlyList<User> ListUsers()
    {
        lock (_gate)
        {
            return [.. _users];
        }
    }

    public IReadOnlyList<Device> ListDevices()
    {
        lock (_gate)
        {
            return [.. _devices];
        }
    }

    public IReadOnlyList<Gateway> ListGateways()
    {
        lock (_gate)
        {
            return [.. _gateways];
        }
    }

    public IReadOnlyList<GatewayPool> ListGatewayPools()
    {
        lock (_gate)
        {
            return [.. _gatewayPools];
        }
    }

    public IReadOnlyList<PolicyRule> ListPolicies()
    {
        lock (_gate)
        {
            return [.. _policies];
        }
    }

    public IReadOnlyList<TunnelSession> ListSessions()
    {
        lock (_gate)
        {
            return [.. _sessions];
        }
    }

    public IReadOnlyList<HealthSample> ListHealthSamples()
    {
        lock (_gate)
        {
            return [.. _healthSamples.OrderByDescending(sample => sample.SampledAtUtc)];
        }
    }

    public IReadOnlyList<Alert> ListAlerts()
    {
        lock (_gate)
        {
            return [.. _alerts.OrderByDescending(alert => alert.CreatedAtUtc)];
        }
    }

    public IReadOnlyList<AuthProviderConfig> ListAuthProviders()
    {
        lock (_gate)
        {
            return [.. _authProviders];
        }
    }

    public IReadOnlyList<AuditEvent> ListAuditEvents()
    {
        lock (_gate)
        {
            return [.. _auditEvents.OrderByDescending(evt => evt.Sequence)];
        }
    }

    public IReadOnlyList<AuditEvent> ListAuditEventsForExport(DateTimeOffset createdBeforeUtc, int limit)
    {
        lock (_gate)
        {
            return [.. _auditEvents
                .Where(evt => evt.CreatedAtUtc <= createdBeforeUtc)
                .OrderBy(evt => evt.Sequence)
                .Take(limit)];
        }
    }

    public IReadOnlyList<AuditRetentionCheckpoint> ListAuditRetentionCheckpoints()
    {
        lock (_gate)
        {
            return [.. _auditRetentionCheckpoints.OrderByDescending(item => item.ExportedAtUtc)];
        }
    }

    public void WriteAudit(string actor, string action, string targetType, string targetId, string outcome, string detail)
    {
        lock (_gate)
        {
            AddAudit(actor, action, targetType, targetId, outcome, detail);
        }
    }

    public AuditRetentionCheckpoint ApplyRetention(AuditRetentionOperation operation)
    {
        lock (_gate)
        {
            _auditEvents.RemoveAll(item => item.Sequence <= operation.RemovedThroughSequence);
            var checkpoint = new AuditRetentionCheckpoint(
                Guid.NewGuid().ToString("n"),
                operation.CutoffUtc,
                operation.ExportedAtUtc,
                operation.ExportPath,
                operation.RemovedThroughSequence,
                operation.RemovedThroughCreatedAtUtc,
                operation.RemovedThroughEventHash,
                operation.ExportedEventCount);
            _auditRetentionCheckpoints.Add(checkpoint);
            return checkpoint;
        }
    }

    public bool DisableExpiredTestUser()
    {
        lock (_gate)
        {
            var testUser = _users.SingleOrDefault(user => user.Username == "user");
            if (testUser is null || !testUser.Enabled || _testUserEnabledAtUtc is null)
            {
                return false;
            }

            if (_testUserEnabledAtUtc.Value.AddHours(1) > DateTimeOffset.UtcNow)
            {
                return false;
            }

            DisableUser(testUser.Id, "scheduler", "Automatic disable for seeded test user after one hour.");
            return true;
        }
    }

    public bool ValidatePrivilegedOperation(bool stepUpSatisfied)
    {
        lock (_gate)
        {
            return _defaultAdmin.MfaEnrolled && !_defaultAdmin.MustChangePassword && stepUpSatisfied;
        }
    }

    private void AddAlert(HealthSeverity severity, string title, string description, string targetType, string targetId)
    {
        _alerts.Add(new Alert(
            Guid.NewGuid().ToString("n"),
            severity,
            title,
            description,
            targetType,
            targetId,
            DateTimeOffset.UtcNow));
    }

    private void AddAudit(string actor, string action, string targetType, string targetId, string outcome, string detail)
    {
        _auditEvents.Add(AuditChain.CreateNext(
            _auditEvents.LastOrDefault(),
            Guid.NewGuid().ToString("n"),
            actor,
            action,
            targetType,
            targetId,
            DateTimeOffset.UtcNow,
            outcome,
            detail));
    }

    private void SyncBootstrapAdmin()
    {
        var index = _admins.FindIndex(item => item.Id == _defaultAdmin.Id);
        if (index >= 0)
        {
            _admins[index] = _defaultAdmin;
        }
        else
        {
            _admins.Add(_defaultAdmin);
        }
    }
}

[JsonSerializable(typeof(AdminAccount))]
[JsonSerializable(typeof(User))]
[JsonSerializable(typeof(Gateway))]
[JsonSerializable(typeof(DashboardSnapshot))]
[JsonSerializable(typeof(BootstrapStatus))]
[JsonSerializable(typeof(ConnectionMapPoint[]))]
[JsonSerializable(typeof(List<Alert>))]
[JsonSerializable(typeof(List<Gateway>))]
[JsonSerializable(typeof(List<Device>))]
[JsonSerializable(typeof(List<TunnelSession>))]
[JsonSerializable(typeof(List<PolicyRule>))]
[JsonSerializable(typeof(List<AuthProviderConfig>))]
[JsonSerializable(typeof(List<AuditEvent>))]
[JsonSerializable(typeof(List<AuditRetentionCheckpoint>))]
[JsonSerializable(typeof(PlatformSession))]
[JsonSerializable(typeof(IssuedPlatformSession))]
public partial class OwlProtectJsonContext : JsonSerializerContext;
