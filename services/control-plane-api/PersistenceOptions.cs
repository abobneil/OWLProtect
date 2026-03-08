namespace OWLProtect.ControlPlane.Api;

public sealed class PersistenceOptions
{
    public string Provider { get; set; } = "in-memory";
    public string? ConnectionString { get; set; }
    public string? RedisConnectionString { get; set; }
    public bool SeedOnStartup { get; set; } = true;
}
