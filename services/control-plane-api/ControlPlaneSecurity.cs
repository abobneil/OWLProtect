using System.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed record AuthenticatedRequestContext(
    PlatformSession Session,
    AdminAccount? Admin,
    User? User)
{
    public string Actor => Admin?.Username ?? User?.Username ?? Session.SubjectName;
}

internal sealed record AuthorizationFailure(
    int StatusCode,
    string ErrorCode,
    string Message,
    string AuditDetail,
    string Actor);

internal static class ControlPlaneSecurity
{
    private const string IdentityItemKey = "owlprotect.identity";
    private const string WebSocketAuthProtocolPrefix = "owlprotect.auth.";

    public static void AttachIdentity(HttpContext httpContext)
    {
        var accessToken = ReadAccessToken(httpContext);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return;
        }

        var sessionStore = httpContext.RequestServices.GetRequiredService<IPlatformSessionStore>();
        var session = sessionStore.Authenticate(accessToken);
        if (session is null)
        {
            return;
        }

        var adminRepository = httpContext.RequestServices.GetRequiredService<IAdminRepository>();
        var userRepository = httpContext.RequestServices.GetRequiredService<IUserRepository>();
        AdminAccount? admin = null;
        User? user = null;

        switch (session.Kind)
        {
            case PlatformSessionKind.Admin:
                admin = adminRepository.ListAdmins().SingleOrDefault(item => item.Id == session.SubjectId);
                break;
            case PlatformSessionKind.User:
                user = userRepository.ListUsers().SingleOrDefault(item => item.Id == session.SubjectId);
                break;
        }

        httpContext.Items[IdentityItemKey] = new AuthenticatedRequestContext(session, admin, user);
        var sessionCorrelationId = SensitiveDataRedactor.CreateSessionCorrelationId(session.Id);
        httpContext.Items[OwlProtectTelemetry.SessionCorrelationItemKey] = sessionCorrelationId;
        Activity.Current?.SetTag("owlprotect.session.correlation_id", sessionCorrelationId);
        Activity.Current?.AddBaggage("owlprotect.session.correlation_id", sessionCorrelationId);
    }

    public static AuthenticatedRequestContext? GetIdentity(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(IdentityItemKey, out var value) ? value as AuthenticatedRequestContext : null;

    public static EndpointFilterDelegate RequireSession(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next) =>
        RequirePolicy(ControlPlaneAuthorizationPolicies.AuthenticatedSession("session.authenticated"), next);

    public static EndpointFilterDelegate RequireAdmin(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next,
        string policyName,
        AdminRole minimumRole,
        bool requireCompliantAdmin,
        bool requireStepUp) =>
        RequirePolicy(ControlPlaneAuthorizationPolicies.Admin(policyName, minimumRole, requireCompliantAdmin, requireStepUp), next);

    public static EndpointFilterDelegate RequireUser(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next,
        string policyName) =>
        RequirePolicy(ControlPlaneAuthorizationPolicies.EndUser(policyName), next);

    public static bool TryAuthorize(HttpContext httpContext, AuthorizationPolicyRequirement requirement, out AuthorizationFailure? failure)
    {
        var identity = GetIdentity(httpContext);
        if (requirement.RequireAuthenticatedSession && identity is null)
        {
            failure = new AuthorizationFailure(
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "Authenticated session required.",
                $"Policy '{requirement.PolicyName}' requires an authenticated session.",
                "anonymous");
            return false;
        }

        if (identity is null)
        {
            failure = new AuthorizationFailure(
                StatusCodes.Status401Unauthorized,
                "authentication_required",
                "Authenticated session required.",
                $"Policy '{requirement.PolicyName}' requires an authenticated session.",
                "anonymous");
            return false;
        }

        if (requirement.RequireEndUser && identity.User is null)
        {
            failure = new AuthorizationFailure(
                StatusCodes.Status403Forbidden,
                "user_session_required",
                "End-user session required.",
                $"Policy '{requirement.PolicyName}' requires an end-user session.",
                identity.Actor);
            return false;
        }

        if (requirement.MinimumAdminRole is not null && identity.Admin is null)
        {
            failure = new AuthorizationFailure(
                StatusCodes.Status403Forbidden,
                "admin_role_required",
                "Admin role required.",
                $"Policy '{requirement.PolicyName}' requires an admin session.",
                identity.Actor);
            return false;
        }

        if (requirement.RequireCompliantAdmin && identity.Admin is not null && (identity.Admin.MustChangePassword || !identity.Admin.MfaEnrolled))
        {
            failure = new AuthorizationFailure(
                StatusCodes.Status403Forbidden,
                "bootstrap_admin_incomplete",
                "Bootstrap admin must change password and enroll MFA before accessing this route.",
                $"Policy '{requirement.PolicyName}' requires bootstrap admin password rotation and MFA enrollment.",
                identity.Actor);
            return false;
        }

        if (requirement.MinimumAdminRole is not null && identity.Admin is not null && !HasMinimumRole(identity.Admin.Role, requirement.MinimumAdminRole.Value))
        {
            failure = new AuthorizationFailure(
                StatusCodes.Status403Forbidden,
                "admin_role_denied",
                "Role does not permit this operation.",
                $"Policy '{requirement.PolicyName}' requires role {requirement.MinimumAdminRole}; caller has {identity.Admin.Role}.",
                identity.Actor);
            return false;
        }

        if (requirement.RequireStepUp)
        {
            var bootstrapService = httpContext.RequestServices.GetRequiredService<IBootstrapService>();
            var hasStepUp = HasActiveStepUp(identity.Session);
            if (!bootstrapService.ValidatePrivilegedOperation(hasStepUp))
            {
                failure = new AuthorizationFailure(
                    StatusCodes.Status412PreconditionFailed,
                    "step_up_required",
                    "Privileged step-up is required.",
                    $"Policy '{requirement.PolicyName}' requires valid server-side privileged step-up evidence.",
                    identity.Actor);
                return false;
            }
        }

        failure = null;
        return true;
    }

    public static IResult BuildDeniedResult(HttpContext httpContext, AuthorizationPolicyRequirement requirement, AuthorizationFailure failure)
    {
        AuditFailure(httpContext, requirement.PolicyName, failure);
        return TypedResults.Json(
            new ApiErrorResponse(failure.Message, failure.ErrorCode, requirement.PolicyName),
            statusCode: failure.StatusCode);
    }

    public static bool HasActiveStepUp(PlatformSession session) =>
        session.StepUpExpiresAtUtc is not null && session.StepUpExpiresAtUtc > DateTimeOffset.UtcNow;

    private static EndpointFilterDelegate RequirePolicy(AuthorizationPolicyRequirement requirement, EndpointFilterDelegate next)
    {
        return async invocationContext =>
        {
            var httpContext = invocationContext.HttpContext;
            if (!TryAuthorize(httpContext, requirement, out var failure))
            {
                return BuildDeniedResult(httpContext, requirement, failure!);
            }

            return await next(invocationContext);
        };
    }

    private static bool HasMinimumRole(AdminRole current, AdminRole required) => RoleWeight(current) >= RoleWeight(required);

    private static int RoleWeight(AdminRole role) =>
        role switch
        {
            AdminRole.ReadOnly => 0,
            AdminRole.Operator => 1,
            AdminRole.SuperAdmin => 2,
            _ => -1
        };

    private static string? ReadAccessToken(HttpContext httpContext)
    {
        var authorizationHeader = httpContext.Request.Headers.Authorization.ToString();
        if (authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorizationHeader["Bearer ".Length..].Trim();
        }

        return ReadAccessTokenFromWebSocketProtocols(httpContext.Request.Headers["Sec-WebSocket-Protocol"]);
    }

    private static string? ReadAccessTokenFromWebSocketProtocols(StringValues requestedProtocols)
    {
        foreach (var headerValue in requestedProtocols)
        {
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                continue;
            }

            foreach (var rawProtocol in headerValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (rawProtocol.StartsWith(WebSocketAuthProtocolPrefix, StringComparison.Ordinal))
                {
                    return rawProtocol[WebSocketAuthProtocolPrefix.Length..];
                }
            }
        }

        return null;
    }

    private static void AuditFailure(HttpContext httpContext, string policyName, AuthorizationFailure failure)
    {
        var auditWriter = httpContext.RequestServices.GetRequiredService<IAuditWriter>();
        auditWriter.WriteAudit(failure.Actor, "authorization-denied", "route", httpContext.Request.Path, "failure", $"[{failure.ErrorCode}] policy={policyName} {failure.AuditDetail}");
    }
}
