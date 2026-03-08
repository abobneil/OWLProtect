using System.Text.Json.Serialization;

namespace OWLProtect.Core;

public sealed class InMemoryState
{
    private readonly Lock _gate = new();
    private AdminAccount _defaultAdmin = SeedData.DefaultAdmin;
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

    public DashboardSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new DashboardSnapshot(
                [_defaultAdmin],
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
                password != _defaultAdmin.Password)
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
                Password = newPassword,
                MustChangePassword = false
            };

            AddAudit("admin", "password-change", "admin", _defaultAdmin.Id, "success", "Bootstrap admin password changed.");
            return _defaultAdmin;
        }
    }

    public AdminAccount EnrollAdminMfa()
    {
        lock (_gate)
        {
            _defaultAdmin = _defaultAdmin with { MfaEnrolled = true };
            AddAudit("admin", "mfa-enroll", "admin", _defaultAdmin.Id, "success", "Bootstrap admin enrolled MFA.");
            return _defaultAdmin;
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
        _auditEvents.Add(new AuditEvent(
            Guid.NewGuid().ToString("n"),
            actor,
            action,
            targetType,
            targetId,
            DateTimeOffset.UtcNow,
            outcome,
            detail));
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
public partial class OwlProtectJsonContext : JsonSerializerContext;

