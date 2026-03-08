namespace OWLProtect.Core;

public static class ControlPlaneStreamTopics
{
    public const string Alerts = "alerts";
    public const string GatewayHealth = "gateway-health";
    public const string Telemetry = "telemetry";
    public const string Sessions = "sessions";
}

public interface IControlPlaneEventPublisher
{
    void Publish(string topic, string eventType, string? entityId = null);
}
