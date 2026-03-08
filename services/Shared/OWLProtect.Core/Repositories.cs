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

public interface IAdminRepository
{
    IReadOnlyList<AdminAccount> ListAdmins();
}

public interface IUserRepository
{
    IReadOnlyList<User> ListUsers();
    User EnableUser(string userId, string actor);
    User DisableUser(string userId, string actor, string reason);
}

public interface IDeviceRepository
{
    IReadOnlyList<Device> ListDevices();
    IReadOnlyList<ConnectionMapPoint> GetConnectionMap();
}

public interface IGatewayRepository
{
    IReadOnlyList<Gateway> ListGateways();
    Gateway UpsertGatewayHeartbeat(Gateway gateway);
}

public interface IGatewayPoolRepository
{
    IReadOnlyList<GatewayPool> ListGatewayPools();
}

public interface IPolicyRepository
{
    IReadOnlyList<PolicyRule> ListPolicies();
}

public interface ISessionRepository
{
    IReadOnlyList<TunnelSession> ListSessions();
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
