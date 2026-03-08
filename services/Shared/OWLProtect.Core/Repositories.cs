namespace OWLProtect.Core;

public interface IBootstrapService
{
    BootstrapStatus GetBootstrapStatus();
    AdminAccount LoginAdmin(string username, string password);
    User LoginUser(string username);
    AdminAccount UpdateAdminPassword(string currentPassword, string newPassword);
    AdminAccount EnrollAdminMfa();
    bool DisableExpiredTestUser();
    bool ValidatePrivilegedOperation(bool stepUpSatisfied);
}

public interface IDashboardQueryService
{
    DashboardSnapshot Snapshot();
}

public interface IAdminRepository
{
    IReadOnlyList<AdminAccount> ListAdmins();
}

public interface IUserRepository
{
    IReadOnlyList<User> ListUsers();
    User UpsertUser(User user);
    User EnableUser(string userId, string actor);
    User DisableUser(string userId, string actor, string reason);
    void DeleteUser(string userId);
}

public interface IDeviceRepository
{
    IReadOnlyList<Device> ListDevices();
    IReadOnlyList<ConnectionMapPoint> GetConnectionMap();
    Device UpsertDevice(Device device);
    void DeleteDevice(string deviceId);
}

public interface IGatewayRepository
{
    IReadOnlyList<Gateway> ListGateways();
    Gateway UpsertGatewayHeartbeat(Gateway gateway);
    void DeleteGateway(string gatewayId);
}

public interface IGatewayPoolRepository
{
    IReadOnlyList<GatewayPool> ListGatewayPools();
}

public interface IPolicyRepository
{
    IReadOnlyList<PolicyRule> ListPolicies();
    PolicyRule UpsertPolicy(PolicyRule policy);
    void DeletePolicy(string policyId);
}

public interface ISessionRepository
{
    IReadOnlyList<TunnelSession> ListSessions();
    TunnelSession UpsertSession(TunnelSession session);
    bool RevokeSession(string sessionId, string actor, string reason);
}

public interface IHealthSampleRepository
{
    IReadOnlyList<HealthSample> ListHealthSamples();
    void AddHealthSample(HealthSample sample);
}

public interface IAlertRepository
{
    IReadOnlyList<Alert> ListAlerts();
}

public interface IAuthProviderConfigRepository
{
    IReadOnlyList<AuthProviderConfig> ListAuthProviders();
}

public interface IAuditRepository
{
    IReadOnlyList<AuditEvent> ListAuditEvents();
}

public interface IAuditWriter
{
    void WriteAudit(string actor, string action, string targetType, string targetId, string outcome, string detail);
}

public interface IPlatformSessionStore
{
    IssuedPlatformSession CreateSession(PlatformSessionKind kind, string subjectId, string subjectName, string? role);
    PlatformSession? Authenticate(string accessToken);
    PlatformSession? GetSession(string sessionId);
    IssuedPlatformSession Refresh(string refreshToken);
    PlatformSession MarkStepUp(string sessionId, DateTimeOffset expiresAtUtc, string actor);
    bool RevokeSession(string sessionId, string actor, string reason);
    int RevokeSubjectSessions(PlatformSessionKind kind, string subjectId, string actor, string reason);
}
