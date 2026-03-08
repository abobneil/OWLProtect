using System.Security.Cryptography;
using System.Text;

namespace OWLProtect.Core;

public static class PolicyLifecycleEngine
{
    public static DeviceEnrollmentResult EnrollDevice(User user, Device? existingDevice, string deviceId, string deviceName, string city, string country, string publicIp, string hardwareKey, string serialNumber, string operatingSystem, DeviceEnrollmentKind enrollmentKind, bool managed)
    {
        var now = DateTimeOffset.UtcNow;
        if (existingDevice is null)
        {
            var created = new Device(
                deviceId,
                deviceName.Trim(),
                user.Id,
                city.Trim(),
                country.Trim(),
                publicIp.Trim(),
                Managed: managed,
                Compliant: false,
                PostureScore: 0,
                ConnectionState.PolicyBlocked,
                LastSeenUtc: now,
                TenantId: user.TenantId,
                RegistrationState: DeviceRegistrationState.Pending,
                EnrollmentKind: enrollmentKind,
                HardwareKey: hardwareKey.Trim(),
                SerialNumber: serialNumber.Trim(),
                OperatingSystem: operatingSystem.Trim(),
                RegisteredAtUtc: now,
                LastEnrollmentAtUtc: now,
                DisabledAtUtc: null,
                ComplianceReasons: ["awaiting_enrollment_approval"]);
            return new DeviceEnrollmentResult(created, RequiresApproval: true, ReconciledExistingRecord: false, Action: "created-pending");
        }

        var reconciled = existingDevice with
        {
            Name = deviceName.Trim(),
            UserId = user.Id,
            City = city.Trim(),
            Country = country.Trim(),
            PublicIp = publicIp.Trim(),
            Managed = managed,
            TenantId = user.TenantId,
            EnrollmentKind = enrollmentKind,
            HardwareKey = hardwareKey.Trim(),
            SerialNumber = serialNumber.Trim(),
            OperatingSystem = operatingSystem.Trim(),
            RegistrationState = string.Equals(existingDevice.HardwareKey, hardwareKey, StringComparison.Ordinal)
                ? DeviceRegistrationState.Enrolled
                : DeviceRegistrationState.Pending,
            RegisteredAtUtc = existingDevice.RegisteredAtUtc ?? now,
            LastEnrollmentAtUtc = now,
            DisabledAtUtc = null
        };

        var requiresApproval = reconciled.RegistrationState != DeviceRegistrationState.Enrolled;
        var action = requiresApproval ? "recovery-pending" : "re-enrolled";
        return new DeviceEnrollmentResult(reconciled, requiresApproval, ReconciledExistingRecord: true, action);
    }

    public static Device ApplyPostureReport(Device device, PostureReport report)
    {
        var reasons = new List<string>();
        if (!report.Managed)
        {
            reasons.Add("device_unmanaged");
        }

        if (!report.BitLockerEnabled)
        {
            reasons.Add("bitlocker_disabled");
        }

        if (!report.DefenderHealthy)
        {
            reasons.Add("defender_unhealthy");
        }

        if (!report.FirewallEnabled)
        {
            reasons.Add("firewall_disabled");
        }

        if (!report.SecureBootEnabled)
        {
            reasons.Add("secure_boot_disabled");
        }

        if (!report.TamperProtectionEnabled)
        {
            reasons.Add("tamper_protection_disabled");
        }

        if (device.RegistrationState != DeviceRegistrationState.Enrolled)
        {
            reasons.Add("device_pending_approval");
        }

        var score = 100 - (reasons.Count * 12);
        if (score < 0)
        {
            score = 0;
        }

        var compliant = reasons.Count == 0 && report.Compliant;
        return device with
        {
            Managed = report.Managed,
            Compliant = compliant,
            PostureScore = score,
            ConnectionState = compliant ? ConnectionState.Healthy : ConnectionState.PolicyBlocked,
            LastSeenUtc = report.CollectedAtUtc ?? DateTimeOffset.UtcNow,
            OperatingSystem = report.OsVersion.Trim(),
            ComplianceReasons = reasons
        };
    }

    public static PolicyResolutionResult ResolvePolicies(User user, Device device, IReadOnlyList<PolicyRule> policies)
    {
        var decisionLog = new List<string>();
        var effectivePolicyIds = new List<string>();
        foreach (var policy in policies
                     .Where(policy => string.Equals(policy.TenantId, user.TenantId, StringComparison.Ordinal))
                     .OrderByDescending(policy => policy.Priority)
                     .ThenBy(policy => policy.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(policy => policy.Id, StringComparer.Ordinal))
        {
            var directAssignment = user.PolicyIds.Any(id => string.Equals(id, policy.Id, StringComparison.Ordinal));
            var groupAssignment = (policy.TargetGroupIds ?? [])
                .Intersect(user.GroupIds, StringComparer.OrdinalIgnoreCase)
                .Any();

            if (!directAssignment && !groupAssignment)
            {
                continue;
            }

            if (policy.RequireManaged && !device.Managed)
            {
                decisionLog.Add($"policy:{policy.Id}:skipped:device_unmanaged");
                continue;
            }

            if (policy.RequireCompliant && !device.Compliant)
            {
                decisionLog.Add($"policy:{policy.Id}:skipped:device_noncompliant");
                continue;
            }

            if (device.PostureScore < policy.MinimumPostureScore)
            {
                decisionLog.Add($"policy:{policy.Id}:skipped:posture_below_{policy.MinimumPostureScore}");
                continue;
            }

            var allowedStates = policy.AllowedDeviceStates ?? [];
            if (allowedStates.Count > 0 && !allowedStates.Contains(device.RegistrationState))
            {
                decisionLog.Add($"policy:{policy.Id}:skipped:device_state_{device.RegistrationState}");
                continue;
            }

            effectivePolicyIds.Add(policy.Id);
            decisionLog.Add($"policy:{policy.Id}:matched:{(directAssignment ? "direct" : "group")}");
        }

        return new PolicyResolutionResult(
            user.TenantId,
            user.Id,
            device.Id,
            user.GroupIds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            effectivePolicyIds,
            decisionLog);
    }

    public static ResolvedPolicyBundle CompileBundle(PolicyResolutionResult resolution, IReadOnlyList<PolicyRule> policies)
    {
        var matchedPolicies = policies
            .Where(policy => resolution.PolicyIds.Contains(policy.Id, StringComparer.Ordinal))
            .OrderByDescending(policy => policy.Priority)
            .ThenBy(policy => policy.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(policy => policy.Id, StringComparer.Ordinal)
            .ToArray();

        var cidrs = matchedPolicies.SelectMany(policy => policy.Cidrs).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var dnsZones = matchedPolicies.SelectMany(policy => policy.DnsZones).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        var ports = matchedPolicies.SelectMany(policy => policy.Ports).Distinct().OrderBy(value => value).ToArray();
        var version = ComputeBundleVersion(resolution.TenantId, resolution.UserId, resolution.DeviceId, resolution.PolicyIds, cidrs, dnsZones, ports);

        return new ResolvedPolicyBundle(
            resolution.TenantId,
            resolution.UserId,
            resolution.DeviceId,
            version,
            DateTimeOffset.UtcNow,
            resolution.PolicyIds,
            cidrs,
            dnsZones,
            ports);
    }

    public static SessionAuthorizationDecision AuthorizeSession(User user, Device device, Gateway gateway, IReadOnlyList<PolicyRule> policies, TimeSpan revalidationInterval)
    {
        if (!user.Enabled)
        {
            return Deny("user_disabled", "User is disabled.");
        }

        if (!string.Equals(user.TenantId, device.TenantId, StringComparison.Ordinal) ||
            !string.Equals(user.TenantId, gateway.TenantId, StringComparison.Ordinal))
        {
            return Deny("tenant_boundary_violation", "User, device, and gateway must belong to the same tenant.");
        }

        if (device.RegistrationState is DeviceRegistrationState.Disabled or DeviceRegistrationState.Revoked)
        {
            return Deny("device_inactive", $"Device is {device.RegistrationState.ToString().ToLowerInvariant()}.");
        }

        if (device.RegistrationState != DeviceRegistrationState.Enrolled)
        {
            return Deny("device_not_enrolled", "Device must be enrolled before a session can be authorized.");
        }

        if (!device.Managed)
        {
            return Deny("device_unmanaged", "Device must be managed before a session can be authorized.");
        }

        var resolution = ResolvePolicies(user, device, policies);
        if (resolution.PolicyIds.Count == 0)
        {
            return Deny("policy_not_resolved", "No effective policies resolved for the current user and device inputs.", resolution);
        }

        var bundle = CompileBundle(resolution, policies);
        return new SessionAuthorizationDecision(
            Authorized: true,
            ErrorCode: string.Empty,
            Message: "authorized",
            resolution,
            bundle,
            DateTimeOffset.UtcNow.Add(revalidationInterval));
    }

    private static SessionAuthorizationDecision Deny(string errorCode, string message, PolicyResolutionResult? resolution = null) =>
        new(
            Authorized: false,
            errorCode,
            message,
            resolution,
            Bundle: null,
            RevalidateAfterUtc: null);

    private static string ComputeBundleVersion(string tenantId, string userId, string deviceId, IReadOnlyList<string> policyIds, IReadOnlyList<string> cidrs, IReadOnlyList<string> dnsZones, IReadOnlyList<int> ports)
    {
        var payload = string.Join('\n',
            tenantId,
            userId,
            deviceId,
            string.Join(',', policyIds.OrderBy(value => value, StringComparer.Ordinal)),
            string.Join(',', cidrs),
            string.Join(',', dnsZones),
            string.Join(',', ports.Select(value => value.ToString())));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
