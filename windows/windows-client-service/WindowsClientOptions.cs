namespace OWLProtect.WindowsClientService;

public sealed class WindowsClientOptions
{
    public string ControlPlaneBaseUrl { get; set; } = "http://localhost:5180";
    public string? SilentProviderId { get; set; } = "auth-1";
    public string? SilentProviderToken { get; set; }
    public string? SilentUsername { get; set; }
    public string? InteractiveProviderId { get; set; }
    public string? InteractiveProviderToken { get; set; }
    public string? InteractiveUsername { get; set; } = "maria.diaz";
    public string? OtlpEndpoint { get; set; }
    public string DeviceCity { get; set; } = "Unknown";
    public string DeviceCountry { get; set; } = "Unknown";
    public string SupportBundleDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OWLProtect",
            "support");
    public bool LaunchTrayAtLogon { get; set; } = true;
    public bool TreatDomainJoinedDeviceAsManaged { get; set; } = true;
}
