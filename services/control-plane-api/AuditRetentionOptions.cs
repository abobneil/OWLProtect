namespace OWLProtect.ControlPlane.Api;

public sealed class AuditRetentionOptions
{
    public bool Enabled { get; set; } = true;
    public int RetentionDays { get; set; } = 365;
    public int ExportBatchSize { get; set; } = 5000;
    public int CheckIntervalHours { get; set; } = 24;
    public string ExportDirectory { get; set; } = "audit-exports";
}
