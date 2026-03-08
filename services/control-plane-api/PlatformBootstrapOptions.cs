using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed class PlatformBootstrapOptions
{
    public string DefaultTenantId { get; set; } = SeedData.DefaultTenantId;
    public string DefaultTenantName { get; set; } = "OWLProtect Default";
    public string DefaultTenantRegion { get; set; } = "global";
    public bool SeedTestUserEnabled { get; set; }
    public int SessionRevalidationSeconds { get; set; } = 300;
}
