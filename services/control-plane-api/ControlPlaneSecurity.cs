using Microsoft.AspNetCore.Http.HttpResults;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed record AuthenticatedRequestContext(
    PlatformSession Session,
    AdminAccount? Admin,
    User? User)
{
    public string Actor => Admin?.Username ?? User?.Username ?? Session.SubjectName;
}

internal static class ControlPlaneSecurity
{
    private const string IdentityItemKey = "owlprotect.identity";

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
    }

    public static AuthenticatedRequestContext? GetIdentity(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(IdentityItemKey, out var value) ? value as AuthenticatedRequestContext : null;

    public static EndpointFilterDelegate RequireSession(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next)
    {
        return async invocationContext =>
        {
            var httpContext = invocationContext.HttpContext;
            var identity = GetIdentity(httpContext);
            if (identity is null)
            {
                AuditFailure(httpContext, actor: "anonymous", "authentication-required", "Authenticated session required.");
                return TypedResults.Json(new { error = "Authenticated session required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            return await next(invocationContext);
        };
    }

    public static EndpointFilterDelegate RequireAdmin(
        EndpointFilterFactoryContext _,
        EndpointFilterDelegate next,
        AdminRole minimumRole,
        bool requireCompliantAdmin,
        bool requireStepUp)
    {
        return async invocationContext =>
        {
            var httpContext = invocationContext.HttpContext;
            var identity = GetIdentity(httpContext);
            if (identity is null)
            {
                AuditFailure(httpContext, actor: "anonymous", "authentication-required", "Authenticated admin session required.");
                return TypedResults.Json(new { error = "Authenticated admin session required." }, statusCode: StatusCodes.Status401Unauthorized);
            }

            if (identity.Admin is null)
            {
                AuditFailure(httpContext, identity.Actor, "authorization-denied", "Admin role required.");
                return TypedResults.Json(new { error = "Admin role required." }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (requireCompliantAdmin && (identity.Admin.MustChangePassword || !identity.Admin.MfaEnrolled))
            {
                AuditFailure(httpContext, identity.Actor, "authorization-denied", "Bootstrap admin must rotate password and enroll MFA before accessing this route.");
                return TypedResults.Json(new { error = "Bootstrap admin must change password and enroll MFA before accessing this route." }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (!HasMinimumRole(identity.Admin.Role, minimumRole))
            {
                AuditFailure(httpContext, identity.Actor, "authorization-denied", $"Role {identity.Admin.Role} is below required role {minimumRole}.");
                return TypedResults.Json(new { error = "Role does not permit this operation." }, statusCode: StatusCodes.Status403Forbidden);
            }

            if (requireStepUp && !HasActiveStepUp(identity.Session))
            {
                AuditFailure(httpContext, identity.Actor, "step-up-required", "Privileged step-up is required.");
                return TypedResults.Json(new { error = "Privileged step-up is required." }, statusCode: StatusCodes.Status412PreconditionFailed);
            }

            return await next(invocationContext);
        };
    }

    public static bool HasActiveStepUp(PlatformSession session) =>
        session.StepUpExpiresAtUtc is not null && session.StepUpExpiresAtUtc > DateTimeOffset.UtcNow;

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

        return httpContext.Request.Query["access_token"].ToString();
    }

    private static void AuditFailure(HttpContext httpContext, string actor, string outcome, string detail)
    {
        var auditWriter = httpContext.RequestServices.GetRequiredService<IAuditWriter>();
        auditWriter.WriteAudit(actor, outcome, "route", httpContext.Request.Path, "failure", detail);
    }
}
