using System.Text.Json.Serialization;

namespace OWLProtect.Core;

public sealed class InMemoryState :
    IBootstrapService,
    IDashboardQueryService,
    ITenantRepository,
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
    IAuditRetentionRepository,
    IMachineTrustRepository
{
    private readonly Lock _gate = new();
    private readonly IControlPlaneEventPublisher _eventPublisher;
    private readonly PlatformBootstrapSettings _bootstrapSettings;
    private AdminAccount _defaultAdmin;
    private readonly List<AdminAccount> _admins = [];
    private DateTimeOffset? _testUserEnabledAtUtc;

    private readonly List<Tenant> _tenants;
    private readonly List<User> _users;
    private readonly List<Device> _devices;
    private readonly List<Gateway> _gateways;
    private readonly List<GatewayPool> _gatewayPools;
    private readonly List<PolicyRule> _policies;
    private readonly List<TunnelSession> _sessions;
    private readonly List<HealthSample> _healthSamples;
    private readonly List<Alert> _alerts;
    private readonly List<AuthProviderConfig> _authProviders;
    private readonly List<AuditEvent> _auditEvents;
    private readonly List<AuditRetentionCheckpoint> _auditRetentionCheckpoints = [];
    private readonly List<MachineTrustMaterial> _trustMaterials = [];

    public InMemoryState(IBootstrapAdminCredentialsProvider bootstrapAdminCredentialsProvider, IControlPlaneEventPublisher eventPublisher, IPlatformBootstrapSettingsProvider bootstrapSettingsProvider)
    {
        _eventPublisher = eventPublisher;
        _bootstrapSettings = bootstrapSettingsProvider.GetSettings();
        var seed = SeedData.CreateSeedDataset(_bootstrapSettings);
        _defaultAdmin = SeedData.CreateDefaultAdmin(bootstrapAdminCredentialsProvider.GetBootstrapAdminCredentials());
        _admins.Add(_defaultAdmin);
        _tenants = [seed.DefaultTenant];
        _users = [.. seed.Users];
        _devices = [.. seed.Devices];
        _gateways = [.. seed.Gateways];
        _gatewayPools = [.. seed.GatewayPools];
        _policies = [.. seed.Policies];
        _sessions = [.. seed.Sessions];
        _healthSamples = [.. seed.HealthSamples];
        _alerts = [.. seed.Alerts];
        _authProviders = [.. seed.AuthProviders];
        _auditEvents = [.. seed.AuditEvents];
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
            if (!PasswordProtector.Verify(currentPassword, _defaultAdmin.Password))
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
                AddAlert(HealthSeverity.Yellow, "Test account enabled", "The seeded test account was enabled and will be auto-disabled within one hour.", "user", updated.Id, updated.TenantId);
                _eventPublisher.Publish(ControlPlaneStreamTopics.Alerts, "created");
            }

            AddAudit(actor, "enable-user", "user", updated.Id, "success", "User enabled.", updated.TenantId);
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
                AddAlert(HealthSeverity.Red, "Test account auto-disabled", "The seeded test account was disabled and all active sessions were revoked.", "user", updated.Id, updated.TenantId);
                _eventPublisher.Publish(ControlPlaneStreamTopics.Alerts, "created");
            }

            AddAudit(actor, "disable-user", "user", updated.Id, "success", reason, updated.TenantId);
            _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "revoked-subject", updated.Id);
            return updated;
        }
    }

    public Gateway UpsertGatewayHeartbeat(Gateway gateway)
    {
        lock (_gate)
        {
            var updatedGateway = gateway with { LastHeartbeatUtc = gateway.LastHeartbeatUtc ?? DateTimeOffset.UtcNow };
            var index = _gateways.FindIndex(item => item.Id == gateway.Id);
            if (index >= 0)
            {
                _gateways[index] = updatedGateway;
            }
            else
            {
                _gateways.Add(updatedGateway);
            }

            AddAudit("gateway", "heartbeat", "gateway", updatedGateway.Id, "success", $"Gateway {updatedGateway.Name} reported health {updatedGateway.Health}.", updatedGateway.TenantId);
            _eventPublisher.Publish(ControlPlaneStreamTopics.GatewayHealth, "upserted", updatedGateway.Id);
            return updatedGateway;
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

            _eventPublisher.Publish(ControlPlaneStreamTopics.Telemetry, "recorded", sample.DeviceId);
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

            AddAudit(actor, "upsert-user", "user", user.Id, "success", "User record created or updated.", user.TenantId);
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
            _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "deleted-subject", userId);
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

            AddAudit("admin", "upsert-device", "device", device.Id, "success", "Device record created or updated.", device.TenantId);
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
            _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "deleted-device", deviceId);
            _eventPublisher.Publish(ControlPlaneStreamTopics.Telemetry, "deleted-device", deviceId);
        }
    }

    public void DeleteGateway(string gatewayId)
    {
        lock (_gate)
        {
            _gateways.RemoveAll(gateway => gateway.Id == gatewayId);
            _sessions.RemoveAll(session => session.GatewayId == gatewayId);
            AddAudit("admin", "delete-gateway", "gateway", gatewayId, "success", "Gateway record deleted.");
            _eventPublisher.Publish(ControlPlaneStreamTopics.GatewayHealth, "deleted", gatewayId);
            _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "deleted-gateway", gatewayId);
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

            AddAudit("admin", "upsert-policy", "policy", policy.Id, "success", "Policy record created or updated.", policy.TenantId);
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

            AddAudit("admin", "upsert-session", "session", session.Id, "success", "Session record created or updated.", session.TenantId);
            _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "upserted", session.Id);
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
                _eventPublisher.Publish(ControlPlaneStreamTopics.Sessions, "revoked", sessionId);
            }

            return removed;
        }
    }

    public IReadOnlyList<Tenant> ListTenants()
    {
        lock (_gate)
        {
            return [.. _tenants];
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

    public AuthProviderConfig UpsertAuthProvider(AuthProviderConfig provider, string actor)
    {
        lock (_gate)
        {
            var index = _authProviders.FindIndex(item => item.Id == provider.Id);
            if (index >= 0)
            {
                _authProviders[index] = provider;
            }
            else
            {
                _authProviders.Add(provider);
            }

            AddAudit(actor, "upsert-auth-provider", "auth-provider", provider.Id, "success", "Auth provider record created or updated.", provider.TenantId);
            return provider;
        }
    }

    public bool DeleteAuthProvider(string providerId, string actor)
    {
        lock (_gate)
        {
            var removed = _authProviders.RemoveAll(provider => provider.Id == providerId) > 0;
            if (removed)
            {
                AddAudit(actor, "delete-auth-provider", "auth-provider", providerId, "success", "Auth provider record deleted.");
            }

            return removed;
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

    public void WriteAudit(string actor, string action, string targetType, string targetId, string outcome, string detail, string? tenantId = null)
    {
        lock (_gate)
        {
            AddAudit(actor, action, targetType, targetId, outcome, detail, tenantId);
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

    public IReadOnlyList<MachineTrustMaterial> ListTrustMaterials()
    {
        lock (_gate)
        {
            return [.. _trustMaterials.OrderByDescending(item => item.IssuedAtUtc)];
        }
    }

    public IReadOnlyList<MachineTrustMaterial> ListTrustMaterials(MachineTrustSubjectKind kind, string subjectId)
    {
        lock (_gate)
        {
            return [.. _trustMaterials
                .Where(item => item.Kind == kind && string.Equals(item.SubjectId, subjectId, StringComparison.Ordinal))
                .OrderByDescending(item => item.IssuedAtUtc)];
        }
    }

    public MachineTrustMaterial? GetTrustMaterial(string trustMaterialId)
    {
        lock (_gate)
        {
            return _trustMaterials.SingleOrDefault(item => item.Id == trustMaterialId);
        }
    }

    public IssuedMachineTrustMaterial IssueTrustMaterial(MachineTrustSubjectKind kind, string subjectId, string subjectName, string actor)
    {
        lock (_gate)
        {
            if (_trustMaterials.Any(item =>
                    item.Kind == kind &&
                    string.Equals(item.SubjectId, subjectId, StringComparison.Ordinal) &&
                    item.RevokedAtUtc is null &&
                    item.ExpiresAtUtc > DateTimeOffset.UtcNow))
            {
                throw new InvalidOperationException($"Active trust material already exists for {kind.ToString().ToLowerInvariant()} '{subjectId}'. Rotate it instead.");
            }

            var issued = MachineTrustFactory.Create(kind, subjectId, subjectName);
            _trustMaterials.Add(issued.Material);
            AddAudit(actor, "issue-machine-trust", kind.ToString().ToLowerInvariant(), subjectId, "success", $"Issued trust material {issued.Material.Id}.");
            return issued;
        }
    }

    public IssuedMachineTrustMaterial RotateTrustMaterial(MachineTrustSubjectKind kind, string subjectId, string subjectName, string actor)
    {
        lock (_gate)
        {
            var activeIndexes = _trustMaterials
                .Select((item, index) => (item, index))
                .Where(item =>
                    item.item.Kind == kind &&
                    string.Equals(item.item.SubjectId, subjectId, StringComparison.Ordinal) &&
                    item.item.RevokedAtUtc is null &&
                    item.item.ExpiresAtUtc > DateTimeOffset.UtcNow)
                .Select(item => item.index)
                .ToArray();

            if (activeIndexes.Length == 0)
            {
                throw new InvalidOperationException($"No active trust material exists for {kind.ToString().ToLowerInvariant()} '{subjectId}'.");
            }

            var issued = MachineTrustFactory.Create(kind, subjectId, subjectName);
            _trustMaterials.Add(issued.Material);

            foreach (var activeIndex in activeIndexes)
            {
                _trustMaterials[activeIndex] = _trustMaterials[activeIndex] with
                {
                    RevokedAtUtc = DateTimeOffset.UtcNow,
                    ReplacedById = issued.Material.Id
                };
            }

            AddAudit(actor, "rotate-machine-trust", kind.ToString().ToLowerInvariant(), subjectId, "success", $"Rotated trust material to {issued.Material.Id}.");
            return issued;
        }
    }

    public bool RevokeTrustMaterial(string trustMaterialId, string actor, string reason)
    {
        lock (_gate)
        {
            var index = _trustMaterials.FindIndex(item => item.Id == trustMaterialId);
            if (index < 0)
            {
                return false;
            }

            if (_trustMaterials[index].RevokedAtUtc is not null)
            {
                return false;
            }

            _trustMaterials[index] = _trustMaterials[index] with { RevokedAtUtc = DateTimeOffset.UtcNow };
            AddAudit(actor, "revoke-machine-trust", "machine-trust", trustMaterialId, "success", reason);
            return true;
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

    private Alert AddAlert(HealthSeverity severity, string title, string description, string targetType, string targetId, string? tenantId = null)
    {
        var alert = new Alert(
            Guid.NewGuid().ToString("n"),
            severity,
            title,
            description,
            targetType,
            targetId,
            DateTimeOffset.UtcNow,
            tenantId ?? _bootstrapSettings.DefaultTenantId);
        _alerts.Add(alert);
        return alert;
    }

    private void AddAudit(string actor, string action, string targetType, string targetId, string outcome, string detail, string? tenantId = null)
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
            detail,
            tenantId ?? _bootstrapSettings.DefaultTenantId));
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
[JsonSerializable(typeof(Tenant))]
[JsonSerializable(typeof(Gateway))]
[JsonSerializable(typeof(DashboardSnapshot))]
[JsonSerializable(typeof(BootstrapStatus))]
[JsonSerializable(typeof(ConnectionMapPoint[]))]
[JsonSerializable(typeof(ConnectionMapCityAggregate[]))]
[JsonSerializable(typeof(GatewayScore))]
[JsonSerializable(typeof(GatewayPoolStatus[]))]
[JsonSerializable(typeof(GatewayPlacement))]
[JsonSerializable(typeof(DeviceDiagnostics))]
[JsonSerializable(typeof(DeviceDiagnostics[]))]
[JsonSerializable(typeof(List<Alert>))]
[JsonSerializable(typeof(List<Gateway>))]
[JsonSerializable(typeof(List<Device>))]
[JsonSerializable(typeof(List<Tenant>))]
[JsonSerializable(typeof(List<TunnelSession>))]
[JsonSerializable(typeof(List<PolicyRule>))]
[JsonSerializable(typeof(List<AuthProviderConfig>))]
[JsonSerializable(typeof(List<AuditEvent>))]
[JsonSerializable(typeof(List<AuditRetentionCheckpoint>))]
[JsonSerializable(typeof(PlatformSession))]
[JsonSerializable(typeof(IssuedPlatformSession))]
[JsonSerializable(typeof(MachineTrustMaterial))]
[JsonSerializable(typeof(List<MachineTrustMaterial>))]
[JsonSerializable(typeof(IssuedMachineTrustMaterial))]
[JsonSerializable(typeof(PolicyResolutionResult))]
[JsonSerializable(typeof(ResolvedPolicyBundle))]
[JsonSerializable(typeof(SessionAuthorizationDecision))]
[JsonSerializable(typeof(DeviceEnrollmentResult))]
public partial class OwlProtectJsonContext : JsonSerializerContext;
