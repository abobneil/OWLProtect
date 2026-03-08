namespace OWLProtect.Core;

public sealed class ObservabilityOptions
{
    public string ServiceNamespace { get; init; } = "owlprotect";
    public string? OtlpEndpoint { get; init; }
    public string OtlpProtocol { get; init; } = "grpc";
    public bool EnablePrometheusEndpoint { get; init; } = true;
    public bool EnableRequestLogging { get; init; } = true;
    public bool RedactIpAddresses { get; init; } = true;
}
