using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed class GatewayDiagnosticsQueryService(
    IDeviceRepository deviceRepository,
    IGatewayRepository gatewayRepository,
    IGatewayPoolRepository gatewayPoolRepository,
    ISessionRepository sessionRepository,
    IHealthSampleRepository healthSampleRepository)
{
    public IReadOnlyList<GatewayPoolStatus> ListGatewayPools(string? tenantId = null)
    {
        var pools = GatewayDiagnostics.BuildPoolStatuses(gatewayRepository.ListGateways(), gatewayPoolRepository.ListGatewayPools());
        return string.IsNullOrWhiteSpace(tenantId)
            ? pools
            : pools.Where(pool => string.Equals(pool.TenantId, tenantId, StringComparison.Ordinal)).ToArray();
    }

    public GatewayPlacement? SelectPlacement(string tenantId, string? preferredRegion = null) =>
        GatewayDiagnostics.SelectGatewayPlacement(
            tenantId,
            gatewayRepository.ListGateways(),
            gatewayPoolRepository.ListGatewayPools(),
            preferredRegion);

    public IReadOnlyList<DeviceDiagnostics> ListDeviceDiagnostics(string? tenantId = null)
    {
        var devices = deviceRepository.ListDevices();
        var sessions = sessionRepository.ListSessions();
        var samples = healthSampleRepository.ListHealthSamples();
        var gateways = gatewayRepository.ListGateways();

        return devices
            .Where(device => string.IsNullOrWhiteSpace(tenantId) || string.Equals(device.TenantId, tenantId, StringComparison.Ordinal))
            .Select(device =>
            {
                var session = sessions
                    .Where(item => string.Equals(item.DeviceId, device.Id, StringComparison.Ordinal))
                    .OrderByDescending(item => item.ConnectedAtUtc)
                    .FirstOrDefault();
                var sample = samples.FirstOrDefault(item => string.Equals(item.DeviceId, device.Id, StringComparison.Ordinal));
                var gateway = session is null
                    ? null
                    : gateways.SingleOrDefault(item => string.Equals(item.Id, session.GatewayId, StringComparison.Ordinal));
                return GatewayDiagnostics.ClassifyDevice(device, sample, session, gateway);
            })
            .OrderBy(diagnostic => diagnostic.Severity == HealthSeverity.Red ? 0 : diagnostic.Severity == HealthSeverity.Yellow ? 1 : 2)
            .ThenByDescending(diagnostic => diagnostic.ObservedAtUtc)
            .ThenBy(diagnostic => diagnostic.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public DeviceDiagnostics? GetDeviceDiagnostics(string deviceId) =>
        ListDeviceDiagnostics().SingleOrDefault(item => string.Equals(item.DeviceId, deviceId, StringComparison.Ordinal));

    public IReadOnlyList<ConnectionMapCityAggregate> GetConnectionCityMap(string? tenantId = null)
    {
        var aggregates = GatewayDiagnostics.AggregateConnectionCities(deviceRepository.ListDevices(), sessionRepository.ListSessions());
        return string.IsNullOrWhiteSpace(tenantId)
            ? aggregates
            : aggregates.Where(city => string.Equals(city.TenantId, tenantId, StringComparison.Ordinal)).ToArray();
    }
}
