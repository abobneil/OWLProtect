using System.Net;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal static class ManagementValidation
{
    public static IReadOnlyList<string> ValidateAdminRequest(AdminUpsertRequest request, bool creating, IReadOnlyList<AdminAccount> existingAdmins)
    {
        var errors = new List<string>();
        RequireNonEmpty(errors, request.Username, "Username is required.");
        if (string.IsNullOrWhiteSpace(request.Password) && creating)
        {
            errors.Add("Password is required when creating an admin.");
        }

        if (existingAdmins.Any(admin =>
                !string.Equals(admin.Id, request.Id, StringComparison.Ordinal) &&
                string.Equals(admin.Username, request.Username, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Username must be unique.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateUserRequest(UserUpsertRequest request, IReadOnlyList<User> existingUsers)
    {
        var errors = new List<string>();
        RequireNonEmpty(errors, request.Username, "Username is required.");
        RequireNonEmpty(errors, request.DisplayName, "Display name is required.");
        RequireNonEmpty(errors, request.TenantId, "Tenant ID is required.");
        if (!IsKnownProvider(request.Provider))
        {
            errors.Add("Provider must be one of: local, entra, oidc.");
        }

        if (existingUsers.Any(user =>
                !string.Equals(user.Id, request.Id, StringComparison.Ordinal) &&
                string.Equals(user.Username, request.Username, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Username must be unique.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateDeviceRequest(DeviceUpsertRequest request, IReadOnlyList<User> users)
    {
        var errors = new List<string>();
        RequireNonEmpty(errors, request.Name, "Device name is required.");
        RequireNonEmpty(errors, request.UserId, "User ID is required.");
        RequireNonEmpty(errors, request.City, "City is required.");
        RequireNonEmpty(errors, request.Country, "Country is required.");
        RequireNonEmpty(errors, request.PublicIp, "Public IP is required.");
        RequireNonEmpty(errors, request.TenantId, "Tenant ID is required.");
        if (request.PostureScore is < 0 or > 100)
        {
            errors.Add("Posture score must be between 0 and 100.");
        }

        if (!IPAddress.TryParse(request.PublicIp, out _))
        {
            errors.Add("Public IP must be a valid IP address.");
        }

        var user = users.SingleOrDefault(existing => string.Equals(existing.Id, request.UserId, StringComparison.Ordinal));
        if (user is null)
        {
            errors.Add("Referenced user does not exist.");
        }
        else if (!string.Equals(user.TenantId, request.TenantId, StringComparison.Ordinal))
        {
            errors.Add("Device tenant must match the owning user tenant.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateGatewayRequest(GatewayUpsertRequest request)
    {
        var errors = new List<string>();
        RequireNonEmpty(errors, request.Name, "Gateway name is required.");
        RequireNonEmpty(errors, request.Region, "Region is required.");
        RequireNonEmpty(errors, request.TenantId, "Tenant ID is required.");
        ValidatePercentage(errors, request.LoadPercent, "Load percent must be between 0 and 100.");
        ValidatePercentage(errors, request.CpuPercent, "CPU percent must be between 0 and 100.");
        ValidatePercentage(errors, request.MemoryPercent, "Memory percent must be between 0 and 100.");
        if (request.PeerCount < 0)
        {
            errors.Add("Peer count must be zero or greater.");
        }

        if (request.LatencyMs < 0)
        {
            errors.Add("Latency must be zero or greater.");
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidatePolicyRequest(PolicyUpsertRequest request, IReadOnlyList<PolicyRule> existingPolicies)
    {
        var errors = new List<string>();
        RequireNonEmpty(errors, request.Name, "Policy name is required.");
        RequireNonEmpty(errors, request.TenantId, "Tenant ID is required.");
        if (!string.Equals(request.Mode, "split-tunnel", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Policy mode must be 'split-tunnel'.");
        }

        if (request.MinimumPostureScore is < 0 or > 100)
        {
            errors.Add("Minimum posture score must be between 0 and 100.");
        }

        if (existingPolicies.Any(policy =>
                !string.Equals(policy.Id, request.Id, StringComparison.Ordinal) &&
                string.Equals(policy.Name, request.Name, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add("Policy name must be unique.");
        }

        foreach (var port in (request.Ports ?? []).Distinct())
        {
            if (port is < 1 or > 65535)
            {
                errors.Add("Policy ports must be between 1 and 65535.");
                break;
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateSessionRequest(SessionUpsertRequest request, IReadOnlyList<User> users, IReadOnlyList<Device> devices, IReadOnlyList<Gateway> gateways)
    {
        var errors = new List<string>();
        RequireNonEmpty(errors, request.UserId, "User ID is required.");
        RequireNonEmpty(errors, request.DeviceId, "Device ID is required.");
        RequireNonEmpty(errors, request.GatewayId, "Gateway ID is required.");
        RequireNonEmpty(errors, request.TenantId, "Tenant ID is required.");

        if (request.HandshakeAgeSeconds < 0)
        {
            errors.Add("Handshake age seconds must be zero or greater.");
        }

        if (request.ThroughputMbps < 0)
        {
            errors.Add("Throughput Mbps must be zero or greater.");
        }

        var user = users.SingleOrDefault(existing => string.Equals(existing.Id, request.UserId, StringComparison.Ordinal));
        if (user is null)
        {
            errors.Add("Referenced user does not exist.");
        }

        var device = devices.SingleOrDefault(existing => string.Equals(existing.Id, request.DeviceId, StringComparison.Ordinal));
        if (device is null)
        {
            errors.Add("Referenced device does not exist.");
        }

        var gateway = gateways.SingleOrDefault(existing => string.Equals(existing.Id, request.GatewayId, StringComparison.Ordinal));
        if (gateway is null)
        {
            errors.Add("Referenced gateway does not exist.");
        }

        if (user is not null && !string.Equals(user.TenantId, request.TenantId, StringComparison.Ordinal))
        {
            errors.Add("Session tenant must match the user tenant.");
        }

        if (device is not null && !string.Equals(device.TenantId, request.TenantId, StringComparison.Ordinal))
        {
            errors.Add("Session tenant must match the device tenant.");
        }

        if (gateway is not null && !string.Equals(gateway.TenantId, request.TenantId, StringComparison.Ordinal))
        {
            errors.Add("Session tenant must match the gateway tenant.");
        }

        return errors;
    }

    public static User ToUser(UserUpsertRequest request, string id) =>
        new(
            id,
            request.Username.Trim(),
            request.DisplayName.Trim(),
            request.Enabled,
            request.TestAccount,
            request.Provider.Trim().ToLowerInvariant(),
            NormalizeStrings(request.GroupIds),
            NormalizeStrings(request.PolicyIds),
            request.TenantId!.Trim());

    public static Device ToDevice(DeviceUpsertRequest request, string id) =>
        new(
            id,
            request.Name.Trim(),
            request.UserId.Trim(),
            request.City.Trim(),
            request.Country.Trim(),
            request.PublicIp.Trim(),
            request.Managed,
            request.Compliant,
            request.PostureScore,
            request.ConnectionState,
            request.LastSeenUtc ?? DateTimeOffset.UtcNow,
            request.TenantId!.Trim(),
            request.RegistrationState,
            request.EnrollmentKind,
            request.HardwareKey?.Trim() ?? string.Empty,
            request.SerialNumber?.Trim() ?? string.Empty,
            request.OperatingSystem?.Trim() ?? string.Empty,
            request.RegisteredAtUtc,
            request.LastEnrollmentAtUtc,
            request.DisabledAtUtc,
            NormalizeStrings(request.ComplianceReasons));

    public static Gateway ToGateway(GatewayUpsertRequest request, string id) =>
        new(
            id,
            request.Name.Trim(),
            request.Region.Trim(),
            request.Health,
            request.LoadPercent,
            request.PeerCount,
            request.CpuPercent,
            request.MemoryPercent,
            request.LatencyMs,
            request.TenantId!.Trim());

    public static PolicyRule ToPolicy(PolicyUpsertRequest request, string id) =>
        new(
            id,
            request.Name.Trim(),
            NormalizeStrings(request.Cidrs),
            NormalizeStrings(request.DnsZones),
            (request.Ports ?? []).Distinct().OrderBy(port => port).ToArray(),
            request.Mode.Trim().ToLowerInvariant(),
            request.TenantId!.Trim(),
            request.Priority,
            NormalizeStrings(request.TargetGroupIds),
            request.RequireManaged,
            request.RequireCompliant,
            request.MinimumPostureScore,
            NormalizeRegistrationStates(request.AllowedDeviceStates));

    public static TunnelSession ToSession(SessionUpsertRequest request, string id) =>
        new(
            id,
            request.UserId.Trim(),
            request.DeviceId.Trim(),
            request.GatewayId.Trim(),
            request.ConnectedAtUtc ?? DateTimeOffset.UtcNow,
            request.HandshakeAgeSeconds,
            request.ThroughputMbps,
            request.TenantId!.Trim());

    public static AdminAccount ToAdmin(AdminUpsertRequest request, string id, string passwordHash) =>
        new(
            id,
            request.Username.Trim(),
            passwordHash,
            request.Role,
            request.MustChangePassword,
            request.MfaEnrolled);

    private static string[] NormalizeStrings(IReadOnlyList<string>? values) =>
        (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static DeviceRegistrationState[] NormalizeRegistrationStates(IReadOnlyList<DeviceRegistrationState>? values) =>
        (values ?? [])
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

    private static bool IsKnownProvider(string provider) =>
        string.Equals(provider, "local", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "entra", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(provider, "oidc", StringComparison.OrdinalIgnoreCase);

    private static void ValidatePercentage(List<string> errors, int value, string message)
    {
        if (value is < 0 or > 100)
        {
            errors.Add(message);
        }
    }

    private static void RequireNonEmpty(List<string> errors, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(message);
        }
    }
}
