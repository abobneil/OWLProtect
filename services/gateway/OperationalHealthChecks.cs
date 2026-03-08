using Microsoft.Extensions.Diagnostics.HealthChecks;
using OWLProtect.Core;

namespace OWLProtect.Gateway;

internal sealed class GatewayHeartbeatState() : MonitoredOperationState("gateway-heartbeat");

internal sealed class GatewayTrustBundleHealthCheck(GatewayTrustBundleStore trustBundleStore) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var trustBundle = trustBundleStore.Current;
        return Task.FromResult(
            trustBundle is null
                ? HealthCheckResult.Unhealthy("Gateway trust bundle is not loaded.")
                : HealthCheckResult.Healthy(
                    "Gateway trust bundle is loaded.",
                    data: new Dictionary<string, object>
                    {
                        ["trustMaterialId"] = trustBundle.Material.Id,
                        ["rotateAfterUtc"] = trustBundle.Material.RotateAfterUtc.ToString("O"),
                        ["expiresAtUtc"] = trustBundle.Material.ExpiresAtUtc.ToString("O")
                    }));
    }
}

internal sealed class GatewayHeartbeatHealthCheck(GatewayHeartbeatState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationalHealthCheckEvaluator.EvaluateState(state.Snapshot(), TimeSpan.FromSeconds(45)));
}
