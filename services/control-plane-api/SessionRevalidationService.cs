using System.Diagnostics;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class SessionRevalidationService(
    IUserRepository userRepository,
    IDeviceRepository deviceRepository,
    IGatewayRepository gatewayRepository,
    IGatewayPoolRepository gatewayPoolRepository,
    IPolicyRepository policyRepository,
    ISessionRepository sessionRepository,
    IPlatformSessionStore platformSessionStore,
    IAuditWriter auditWriter,
    IPlatformBootstrapSettingsProvider bootstrapSettingsProvider)
{
    private readonly TimeSpan _revalidationInterval = TimeSpan.FromSeconds(bootstrapSettingsProvider.GetSettings().SessionRevalidationSeconds);

    public SessionAuthorizationDecision AuthorizeForClient(User user, Device device)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("controlplane.authorize_client_session");
        activity?.SetTag("owlprotect.user.id", user.Id);
        activity?.SetTag("owlprotect.device.id", device.Id);

        var start = Stopwatch.GetTimestamp();
        var placement = SelectPlacement(user.TenantId);
        var gateway = placement is null
            ? null
            : gatewayRepository.ListGateways().SingleOrDefault(item => string.Equals(item.Id, placement.GatewayId, StringComparison.Ordinal));
        var decision = gateway is null || placement is null
            ? new SessionAuthorizationDecision(false, "gateway_unavailable", "No active gateway is available for the tenant.", null, null, null)
            : PolicyLifecycleEngine.AuthorizeSession(user, device, gateway, policyRepository.ListPolicies(), _revalidationInterval) with
            {
                Placement = placement
            };

        RecordAuthorizationTelemetry(activity, "client", decision, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return decision;
    }

    public SessionAuthorizationDecision AuthorizeForTunnel(string userId, string deviceId, string gatewayId)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("controlplane.authorize_tunnel_session");
        activity?.SetTag("owlprotect.user.id", userId);
        activity?.SetTag("owlprotect.device.id", deviceId);
        activity?.SetTag("owlprotect.gateway.id", gatewayId);

        var start = Stopwatch.GetTimestamp();
        var user = userRepository.ListUsers().SingleOrDefault(item => string.Equals(item.Id, userId, StringComparison.Ordinal));
        var device = deviceRepository.ListDevices().SingleOrDefault(item => string.Equals(item.Id, deviceId, StringComparison.Ordinal));
        var gateway = gatewayRepository.ListGateways().SingleOrDefault(item => string.Equals(item.Id, gatewayId, StringComparison.Ordinal));

        SessionAuthorizationDecision decision;
        if (user is null)
        {
            decision = new SessionAuthorizationDecision(false, "user_not_found", "User was not found.", null, null, null);
        }
        else if (device is null)
        {
            decision = new SessionAuthorizationDecision(false, "device_not_found", "Device was not found.", null, null, null);
        }
        else if (gateway is null)
        {
            decision = new SessionAuthorizationDecision(false, "gateway_not_found", "Gateway was not found.", null, null, null);
        }
        else
        {
            decision = PolicyLifecycleEngine.AuthorizeSession(user, device, gateway, policyRepository.ListPolicies(), _revalidationInterval);
        }

        RecordAuthorizationTelemetry(activity, "tunnel", decision, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return decision;
    }

    public int RevalidateActiveSessions(string actor, string? tenantId = null, string? deviceId = null)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("controlplane.revalidate_active_sessions");
        activity?.SetTag("owlprotect.actor", actor);
        activity?.SetTag("owlprotect.tenant_id", tenantId);
        activity?.SetTag("owlprotect.device.id", deviceId);

        var updatedCount = 0;
        var outcome = "success";

        try
        {
            var users = userRepository.ListUsers();
            var devices = deviceRepository.ListDevices();
            var gateways = gatewayRepository.ListGateways();
            var pools = gatewayPoolRepository.ListGatewayPools();
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

                var user = users.SingleOrDefault(item => string.Equals(item.Id, session.UserId, StringComparison.Ordinal));
                var device = devices.SingleOrDefault(item => string.Equals(item.Id, session.DeviceId, StringComparison.Ordinal));
                if (user is null || device is null)
                {
                    sessionRepository.RevokeSession(session.Id, actor, "Session failed revalidation because the owning user or device record no longer exists.");
                    platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Client, session.DeviceId, actor, "Device session failed revalidation because the owning records no longer exist.");
                    continue;
                }

                var placement = GatewayDiagnostics.SelectGatewayPlacement(user.TenantId, gateways, pools);
                var targetGatewayId = session.GatewayId;
                if (GatewayDiagnostics.ShouldFailOver(session.GatewayId, placement, gateways, pools))
                {
                    targetGatewayId = placement!.GatewayId;
                }

                var decision = AuthorizeForTunnel(session.UserId, session.DeviceId, targetGatewayId);
                if (!decision.Authorized)
                {
                    sessionRepository.RevokeSession(session.Id, actor, $"Session failed revalidation: {decision.ErrorCode}.");
                    platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Client, session.DeviceId, actor, $"Device session failed revalidation: {decision.ErrorCode}.");
                    continue;
                }

                sessionRepository.UpsertSession(session with
                {
                    GatewayId = targetGatewayId,
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
        catch
        {
            outcome = "failure";
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            OwlProtectTelemetry.SessionRevalidationRuns.Add(1, new TagList { { "outcome", outcome } });
            OwlProtectTelemetry.SessionRevalidationAffectedSessions.Add(updatedCount, new TagList { { "outcome", outcome } });
            activity?.SetTag("owlprotect.revalidation.updated_count", updatedCount);
            activity?.SetTag("owlprotect.revalidation.outcome", outcome);
        }
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

    private GatewayPlacement? SelectPlacement(string tenantId) =>
        GatewayDiagnostics.SelectGatewayPlacement(
            tenantId,
            gatewayRepository.ListGateways(),
            gatewayPoolRepository.ListGatewayPools());

    private static void RecordAuthorizationTelemetry(Activity? activity, string flow, SessionAuthorizationDecision decision, double durationMs)
    {
        activity?.SetTag("owlprotect.authorization.authorized", decision.Authorized);
        activity?.SetTag("owlprotect.authorization.flow", flow);
        if (!string.IsNullOrWhiteSpace(decision.ErrorCode))
        {
            activity?.SetTag("owlprotect.authorization.error_code", decision.ErrorCode);
        }

        var tags = new TagList
        {
            { "flow", flow },
            { "authorized", decision.Authorized ? "true" : "false" }
        };
        if (!string.IsNullOrWhiteSpace(decision.ErrorCode))
        {
            tags.Add("error_code", decision.ErrorCode);
        }

        OwlProtectTelemetry.PolicyAuthorizationDuration.Record(durationMs, tags);
    }
}
