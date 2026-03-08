namespace OWLProtect.ControlPlane.Api;

public sealed class SecretManagementOptions
{
    public string BootstrapAdminUsername { get; set; } = "admin";
    public string? BootstrapAdminPassword { get; set; }
    public string? BootstrapAdminPasswordFile { get; set; }
    public string? BootstrapAdminPasswordHash { get; set; }
    public bool AllowGeneratedBootstrapAdminPassword { get; set; }
}
