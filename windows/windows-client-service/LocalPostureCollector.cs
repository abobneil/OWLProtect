using System.Management;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class LocalPostureCollector(
    ILogger<LocalPostureCollector> logger,
    IOptions<WindowsClientOptions> options)
{
    public PostureCollectionResult Collect(string deviceId)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("windowsclient.posture.collect");

        var managed = DetectManagedDevice();
        var firewallEnabled = ReadFirewallEnabled();
        var secureBootEnabled = ReadRegistryDword(
            RegistryHive.LocalMachine,
            @"SYSTEM\CurrentControlSet\Control\SecureBoot\State",
            "UEFISecureBootEnabled") == 1;
        var defenderHealthy = ReadRegistryDword(
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection",
            "DisableRealtimeMonitoring") != 1;
        var tamperProtectionEnabled = (ReadRegistryDword(
            RegistryHive.LocalMachine,
            @"SOFTWARE\Microsoft\Windows Defender\Features",
            "TamperProtection") ?? 0) > 0;
        var bitLockerEnabled = ReadBitLockerEnabled();
        var operatingSystem = RuntimeInformation.OSDescription;
        var score = CalculateScore(managed, firewallEnabled, secureBootEnabled, defenderHealthy, tamperProtectionEnabled, bitLockerEnabled);
        var reasons = BuildReasons(managed, firewallEnabled, secureBootEnabled, defenderHealthy, tamperProtectionEnabled, bitLockerEnabled);
        var compliant = score >= 80 && reasons.Count == 0;
        var collectedAtUtc = DateTimeOffset.UtcNow;

        var postureReport = new PostureReport(
            deviceId,
            managed,
            compliant,
            bitLockerEnabled,
            defenderHealthy,
            firewallEnabled,
            secureBootEnabled,
            tamperProtectionEnabled,
            operatingSystem,
            SchemaVersion: 1,
            CollectedAtUtc: collectedAtUtc);

        var postureStatus = new ClientPostureStatus(
            managed,
            compliant,
            score,
            bitLockerEnabled,
            defenderHealthy,
            firewallEnabled,
            secureBootEnabled,
            tamperProtectionEnabled,
            operatingSystem,
            reasons,
            collectedAtUtc);

        logger.LogInformation(
            "Collected posture for {DeviceId}: score={Score}, compliant={Compliant}, managed={Managed}.",
            deviceId,
            score,
            compliant,
            managed);

        activity?.SetTag("owlprotect.client.posture.compliant", compliant);
        activity?.SetTag("owlprotect.client.posture.managed", managed);
        OwlProtectTelemetry.ClientPostureCollections.Add(1, new TagList
        {
            { "compliant", compliant },
            { "managed", managed }
        });
        OwlProtectTelemetry.ClientPostureScore.Record(score, new TagList
        {
            { "compliant", compliant }
        });

        return new PostureCollectionResult(postureReport, postureStatus);
    }

    private bool DetectManagedDevice()
    {
        if (!options.Value.TreatDomainJoinedDeviceAsManaged)
        {
            return false;
        }

        return !string.Equals(Environment.UserDomainName, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static int CalculateScore(
        bool managed,
        bool firewallEnabled,
        bool secureBootEnabled,
        bool defenderHealthy,
        bool tamperProtectionEnabled,
        bool bitLockerEnabled)
    {
        var score = 0;
        score += managed ? 20 : 0;
        score += firewallEnabled ? 20 : 0;
        score += defenderHealthy ? 20 : 0;
        score += secureBootEnabled ? 15 : 0;
        score += tamperProtectionEnabled ? 15 : 0;
        score += bitLockerEnabled ? 10 : 0;
        return score;
    }

    private static IReadOnlyList<string> BuildReasons(
        bool managed,
        bool firewallEnabled,
        bool secureBootEnabled,
        bool defenderHealthy,
        bool tamperProtectionEnabled,
        bool bitLockerEnabled)
    {
        var reasons = new List<string>();
        if (!managed)
        {
            reasons.Add("device_not_managed");
        }

        if (!firewallEnabled)
        {
            reasons.Add("firewall_disabled");
        }

        if (!defenderHealthy)
        {
            reasons.Add("defender_unhealthy");
        }

        if (!secureBootEnabled)
        {
            reasons.Add("secure_boot_disabled");
        }

        if (!tamperProtectionEnabled)
        {
            reasons.Add("tamper_protection_disabled");
        }

        if (!bitLockerEnabled)
        {
            reasons.Add("bitlocker_disabled");
        }

        return reasons;
    }

    private static bool ReadFirewallEnabled()
    {
        var profiles = new[]
        {
            @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile",
            @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile",
            @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile"
        };

        return profiles.Any(path => ReadRegistryDword(RegistryHive.LocalMachine, path, "EnableFirewall") == 1);
    }

    private static int? ReadRegistryDword(RegistryHive hive, string path, string name)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(path);
            return key?.GetValue(name) as int?;
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadBitLockerEnabled()
    {
        try
        {
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory)?.TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(systemDrive))
            {
                return false;
            }

            using var searcher = new ManagementObjectSearcher(
                @"ROOT\CIMV2\Security\MicrosoftVolumeEncryption",
                "SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume");
            foreach (ManagementObject item in searcher.Get())
            {
                var driveLetter = item["DriveLetter"]?.ToString();
                if (!string.Equals(driveLetter, systemDrive, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return Convert.ToUInt32(item["ProtectionStatus"] ?? 0) == 1;
            }
        }
        catch
        {
        }

        return false;
    }
}

public sealed record PostureCollectionResult(
    PostureReport Report,
    ClientPostureStatus Status);
