using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class SessionRevalidationService(
    IUserRepository userRepository,
    IDeviceRepository deviceRepository,
    IGatewayRepository gatewayRepository,
    IPolicyRepository policyRepository,
    ISessionRepository sessionRepository,
    IPlatformSessionStore platformSessionStore,
    IAuditWriter auditWriter,
    IPlatformBootstrapSettingsProvider bootstrapSettingsProvider)
{
    private readonly TimeSpan _revalidationInterval = TimeSpan.FromSeconds(bootstrapSettingsProvider.GetSettings().SessionRevalidationSeconds);

    public SessionAuthorizationDecision AuthorizeForClient(User user, Device device)
    {
        var gateway = SelectGateway(user.TenantId);
        return gateway is null
            ? new SessionAuthorizationDecision(false, "gateway_unavailable", "No active gateway is available for the tenant.", null, null, null)
            : PolicyLifecycleEngine.AuthorizeSession(user, device, gateway, policyRepository.ListPolicies(), _revalidationInterval);
    }

    public SessionAuthorizationDecision AuthorizeForTunnel(string userId, string deviceId, string gatewayId)
    {
        var user = userRepository.ListUsers().SingleOrDefault(item => string.Equals(item.Id, userId, StringComparison.Ordinal));
        var device = deviceRepository.ListDevices().SingleOrDefault(item => string.Equals(item.Id, deviceId, StringComparison.Ordinal));
        var gateway = gatewayRepository.ListGateways().SingleOrDefault(item => string.Equals(item.Id, gatewayId, StringComparison.Ordinal));

        if (user is null)
        {
            return new SessionAuthorizationDecision(false, "user_not_found", "User was not found.", null, null, null);
        }

        if (device is null)
        {
            return new SessionAuthorizationDecision(false, "device_not_found", "Device was not found.", null, null, null);
        }

        if (gateway is null)
        {
            return new SessionAuthorizationDecision(false, "gateway_not_found", "Gateway was not found.", null, null, null);
        }

        return PolicyLifecycleEngine.AuthorizeSession(user, device, gateway, policyRepository.ListPolicies(), _revalidationInterval);
    }

    public int RevalidateActiveSessions(string actor, string? tenantId = null, string? deviceId = null)
    {
        var updatedCount = 0;
        foreach (var session in sessionRepository.ListSessions())
        {
            if (!string.IsNullOrWhiteSpace(tenantId) && !string.Equals(session.TenantId, tenantId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(deviceId) && !string.Equals(session.DeviceId, deviceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (session.RevalidateAfterUtc.HasValue && session.RevalidateAfterUtc.Value > DateTimeOffset.UtcNow && string.IsNullOrWhiteSpace(deviceId))
            {
                continue;
            }

            var decision = AuthorizeForTunnel(session.UserId, session.DeviceId, session.GatewayId);
            if (!decision.Authorized)
            {
                sessionRepository.RevokeSession(session.Id, actor, $"Session failed revalidation: {decision.ErrorCode}.");
                platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Client, session.DeviceId, actor, $"Device session failed revalidation: {decision.ErrorCode}.");
                continue;
            }

            sessionRepository.UpsertSession(session with
            {
                TenantId = decision.Bundle!.TenantId,
                PolicyBundleVersion = decision.Bundle.Version,
                AuthorizedAtUtc = DateTimeOffset.UtcNow,
                RevalidateAfterUtc = decision.RevalidateAfterUtc
            });
            updatedCount++;
        }

        if (updatedCount > 0)
        {
            auditWriter.WriteAudit(actor, "session-revalidation", "session", tenantId ?? deviceId ?? "all", "success", $"Revalidated {updatedCount} active session(s).", tenantId);
        }

        return updatedCount;
    }

    public Device ApplyPosture(Device device, PostureReport report)
    {
        return PolicyLifecycleEngine.ApplyPostureReport(device, report);
    }

    public DeviceEnrollmentResult EnrollDevice(User user, Device? existingDevice, string deviceId, DeviceEnrollmentRequest request)
    {
        return PolicyLifecycleEngine.EnrollDevice(
            user,
            existingDevice,
            deviceId,
            request.DeviceName,
            request.City,
            request.Country,
            request.PublicIp,
            request.HardwareKey,
            request.SerialNumber,
            request.OperatingSystem,
            request.EnrollmentKind,
            request.Managed);
    }

    private Gateway? SelectGateway(string tenantId) =>
        gatewayRepository.ListGateways()
            .Where(gateway => string.Equals(gateway.TenantId, tenantId, StringComparison.Ordinal))
            .OrderBy(gateway => gateway.Health == HealthSeverity.Green ? 0 : gateway.Health == HealthSeverity.Yellow ? 1 : 2)
            .ThenBy(gateway => gateway.LoadPercent)
            .ThenBy(gateway => gateway.LatencyMs)
            .FirstOrDefault();
}
