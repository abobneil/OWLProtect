using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed record AuthorizationPolicyRequirement(
    string PolicyName,
    AdminRole? MinimumAdminRole,
    bool RequireAuthenticatedSession,
    bool RequireEndUser,
    bool RequireCompliantAdmin,
    bool RequireStepUp);

internal static class ControlPlaneAuthorizationPolicies
{
    public static AuthorizationPolicyRequirement AuthenticatedSession(string policyName) =>
        new(policyName, MinimumAdminRole: null, RequireAuthenticatedSession: true, RequireEndUser: false, RequireCompliantAdmin: false, RequireStepUp: false);

    public static AuthorizationPolicyRequirement EndUser(string policyName) =>
        new(policyName, MinimumAdminRole: null, RequireAuthenticatedSession: true, RequireEndUser: true, RequireCompliantAdmin: false, RequireStepUp: false);

    public static AuthorizationPolicyRequirement Admin(string policyName, AdminRole minimumRole, bool requireCompliantAdmin, bool requireStepUp) =>
        new(policyName, minimumRole, RequireAuthenticatedSession: true, RequireEndUser: false, RequireCompliantAdmin: requireCompliantAdmin, RequireStepUp: requireStepUp);
}
