namespace OWLProtect.Core;

public static class GatewayDiagnostics
{
    private static readonly TimeSpan GatewayHeartbeatTtl = TimeSpan.FromSeconds(60);

    public static GatewayScore ScoreGateway(Gateway gateway, DateTimeOffset? now = null)
    {
        var evaluationTime = now ?? DateTimeOffset.UtcNow;
        var signals = new List<string>();
        var score = 100;
        var heartbeatAge = gateway.LastHeartbeatUtc.HasValue
            ? evaluationTime - gateway.LastHeartbeatUtc.Value
            : TimeSpan.MaxValue;

        if (!gateway.LastHeartbeatUtc.HasValue || heartbeatAge > GatewayHeartbeatTtl)
        {
            score -= 40;
            signals.Add("heartbeat_stale");
        }

        switch (gateway.Health)
        {
            case HealthSeverity.Red:
                score -= 45;
                signals.Add("health_red");
                break;
            case HealthSeverity.Yellow:
                score -= 20;
                signals.Add("health_yellow");
                break;
        }

        if (gateway.LoadPercent >= 85)
        {
            score -= 18;
            signals.Add("load_critical");
        }
        else if (gateway.LoadPercent >= 70)
        {
            score -= 8;
            signals.Add("load_rising");
        }

        if (gateway.LatencyMs >= 80)
        {
            score -= 20;
            signals.Add("latency_critical");
        }
        else if (gateway.LatencyMs >= 50)
        {
            score -= 10;
            signals.Add("latency_high");
        }
        else if (gateway.LatencyMs >= 30)
        {
            score -= 4;
            signals.Add("latency_elevated");
        }

        if (gateway.CpuPercent >= 90)
        {
            score -= 8;
            signals.Add("cpu_hot");
        }
        else if (gateway.CpuPercent >= 75)
        {
            score -= 4;
            signals.Add("cpu_busy");
        }

        if (gateway.MemoryPercent >= 90)
        {
            score -= 8;
            signals.Add("memory_hot");
        }
        else if (gateway.MemoryPercent >= 75)
        {
            score -= 4;
            signals.Add("memory_busy");
        }

        score = Math.Clamp(score, 0, 100);
        var available = gateway.Health != HealthSeverity.Red && heartbeatAge <= GatewayHeartbeatTtl;
        var health = !available || score < 40
            ? HealthSeverity.Red
            : score < 70
                ? HealthSeverity.Yellow
                : HealthSeverity.Green;

        return new GatewayScore(
            gateway.Id,
            gateway.Name,
            gateway.Region,
            health,
            score,
            available,
            gateway.LoadPercent,
            gateway.LatencyMs,
            gateway.CpuPercent,
            gateway.MemoryPercent,
            gateway.PeerCount,
            gateway.LastHeartbeatUtc,
            signals,
            gateway.TenantId);
    }

    public static IReadOnlyList<GatewayPoolStatus> BuildPoolStatuses(IReadOnlyList<Gateway> gateways, IReadOnlyList<GatewayPool> pools, DateTimeOffset? now = null)
    {
        var evaluationTime = now ?? DateTimeOffset.UtcNow;
        var effectivePools = pools.Count > 0 ? pools : BuildSyntheticPools(gateways);

        return effectivePools
            .Select(pool =>
            {
                var members = gateways
                    .Where(gateway =>
                        string.Equals(gateway.TenantId, pool.TenantId, StringComparison.Ordinal) &&
                        pool.GatewayIds.Contains(gateway.Id, StringComparer.Ordinal))
                    .Select(gateway => ScoreGateway(gateway, evaluationTime))
                    .OrderByDescending(score => score.Available)
                    .ThenByDescending(score => score.Score)
                    .ThenBy(score => score.LatencyMs)
                    .ThenBy(score => score.GatewayName, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                var primary = members.FirstOrDefault(score => score.Available) ?? members.FirstOrDefault();
                var failovers = members
                    .Where(score => primary is null || !string.Equals(score.GatewayId, primary.GatewayId, StringComparison.Ordinal))
                    .Where(score => score.Available)
                    .Select(score => score.GatewayId)
                    .ToArray();

                var poolScore = primary?.Score ?? 0;
                if (failovers.Length > 0)
                {
                    poolScore = Math.Clamp(poolScore + 5, 0, 100);
                }

                var poolHealth = primary?.Health ?? HealthSeverity.Red;
                if (primary is not null && failovers.Length == 0 && poolHealth == HealthSeverity.Green && primary.Score < 85)
                {
                    poolHealth = HealthSeverity.Yellow;
                }

                return new GatewayPoolStatus(
                    pool.Id,
                    pool.Name,
                    pool.Regions,
                    poolHealth,
                    poolScore,
                    primary?.GatewayId,
                    failovers,
                    members,
                    pool.TenantId);
            })
            .OrderByDescending(pool => pool.PrimaryGatewayId is not null)
            .ThenByDescending(pool => pool.Score)
            .ThenBy(pool => pool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static GatewayPlacement? SelectGatewayPlacement(
        string tenantId,
        IReadOnlyList<Gateway> gateways,
        IReadOnlyList<GatewayPool> pools,
        string? preferredRegion = null,
        DateTimeOffset? now = null)
    {
        var candidatePools = BuildPoolStatuses(gateways, pools, now)
            .Where(pool => string.Equals(pool.TenantId, tenantId, StringComparison.Ordinal))
            .Where(pool =>
                string.IsNullOrWhiteSpace(preferredRegion) ||
                pool.Regions.Contains(preferredRegion, StringComparer.OrdinalIgnoreCase) ||
                pool.Gateways.Any(gateway => string.Equals(gateway.Region, preferredRegion, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var selectedPool = candidatePools
            .Where(pool => pool.PrimaryGatewayId is not null)
            .OrderByDescending(pool => pool.Score)
            .ThenBy(pool => pool.Health == HealthSeverity.Green ? 0 : pool.Health == HealthSeverity.Yellow ? 1 : 2)
            .ThenBy(pool => pool.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (selectedPool is null)
        {
            return null;
        }

        var primary = selectedPool.Gateways.Single(score => string.Equals(score.GatewayId, selectedPool.PrimaryGatewayId, StringComparison.Ordinal));
        return new GatewayPlacement(
            primary.GatewayId,
            primary.GatewayName,
            selectedPool.PoolId,
            selectedPool.Name,
            primary.Score,
            selectedPool.FailoverGatewayIds,
            selectedPool.FailoverGatewayIds.Count > 0
                ? $"Primary {primary.GatewayName} selected with {selectedPool.FailoverGatewayIds.Count} failover candidate(s)."
                : $"Primary {primary.GatewayName} selected with no warm failover candidates.",
            selectedPool.TenantId);
    }

    public static bool ShouldFailOver(
        string currentGatewayId,
        GatewayPlacement? recommendedPlacement,
        IReadOnlyList<Gateway> gateways,
        IReadOnlyList<GatewayPool> pools,
        DateTimeOffset? now = null)
    {
        if (recommendedPlacement is null || string.Equals(currentGatewayId, recommendedPlacement.GatewayId, StringComparison.Ordinal))
        {
            return false;
        }

        var allScores = BuildPoolStatuses(gateways, pools, now)
            .SelectMany(pool => pool.Gateways)
            .ToArray();
        var current = allScores.SingleOrDefault(score => string.Equals(score.GatewayId, currentGatewayId, StringComparison.Ordinal));
        var replacement = allScores.SingleOrDefault(score => string.Equals(score.GatewayId, recommendedPlacement.GatewayId, StringComparison.Ordinal));

        if (replacement is null)
        {
            return false;
        }

        if (current is null)
        {
            return true;
        }

        if (!current.Available)
        {
            return true;
        }

        return replacement.Score >= current.Score + 15;
    }

    public static DeviceDiagnostics ClassifyDevice(
        Device device,
        HealthSample? sample,
        TunnelSession? session,
        Gateway? gateway,
        DateTimeOffset? now = null)
    {
        var evaluationTime = now ?? DateTimeOffset.UtcNow;
        var observedAt = sample?.SampledAtUtc ?? device.LastSeenUtc;
        var signals = BuildSignals(sample, gateway, evaluationTime);
        var severity = sample?.Severity ?? InferSeverity(device.ConnectionState);

        return device.ConnectionState switch
        {
            ConnectionState.PolicyBlocked => new DeviceDiagnostics(
                device.Id,
                device.Name,
                device.ConnectionState,
                DiagnosticScope.Policy,
                HealthSeverity.Red,
                "Policy is blocking enterprise access.",
                "The device is failing posture or enrollment checks, so routes remain disabled until compliance is restored.",
                session?.GatewayId,
                gateway?.Name,
                observedAt,
                signals,
                device.TenantId),
            ConnectionState.AuthExpired => new DeviceDiagnostics(
                device.Id,
                device.Name,
                device.ConnectionState,
                DiagnosticScope.Authentication,
                HealthSeverity.Red,
                "Client authentication expired.",
                "The device must refresh its client session before the tunnel can be restored.",
                session?.GatewayId,
                gateway?.Name,
                observedAt,
                signals,
                device.TenantId),
            ConnectionState.LocalNetworkPoor or ConnectionState.LowBandwidth => new DeviceDiagnostics(
                device.Id,
                device.Name,
                device.ConnectionState,
                DiagnosticScope.LocalNetwork,
                severity,
                "Local network quality is degrading the tunnel.",
                "Wireless signal, packet loss, or local throughput is below the expected threshold before traffic reaches the gateway.",
                session?.GatewayId,
                gateway?.Name,
                observedAt,
                signals,
                device.TenantId),
            ConnectionState.HighJitter => ClassifyJitter(device, sample, session, gateway, observedAt, signals),
            ConnectionState.GatewayDegraded => new DeviceDiagnostics(
                device.Id,
                device.Name,
                device.ConnectionState,
                DiagnosticScope.Gateway,
                severity,
                "Gateway performance is the primary bottleneck.",
                "The tunnel is established, but the selected gateway is reporting degraded health or elevated latency.",
                session?.GatewayId,
                gateway?.Name,
                observedAt,
                signals,
                device.TenantId),
            ConnectionState.ServerUnavailable => new DeviceDiagnostics(
                device.Id,
                device.Name,
                device.ConnectionState,
                IsLikelyLocalTransport(sample) ? DiagnosticScope.LocalNetwork : gateway is null ? DiagnosticScope.ServerSide : DiagnosticScope.Gateway,
                HealthSeverity.Red,
                IsLikelyLocalTransport(sample)
                    ? "The client cannot reliably reach the internet uplink."
                    : gateway is null
                        ? "A server-side dependency is unavailable."
                        : "The selected gateway is unavailable.",
                IsLikelyLocalTransport(sample)
                    ? "DNS reachability or signal quality indicates a local network outage before the tunnel reaches OWLProtect."
                    : gateway is null
                        ? "The control plane or a backend dependency is not responding even though local connectivity is still present."
                        : "The active gateway stopped answering within the expected heartbeat window.",
                session?.GatewayId,
                gateway?.Name,
                observedAt,
                signals,
                device.TenantId),
            _ => new DeviceDiagnostics(
                device.Id,
                device.Name,
                device.ConnectionState,
                DiagnosticScope.Healthy,
                HealthSeverity.Green,
                "Tunnel performance is healthy.",
                "Latency, jitter, and route health are within the normal operating envelope.",
                session?.GatewayId,
                gateway?.Name,
                observedAt,
                signals,
                device.TenantId)
        };
    }

    public static IReadOnlyList<ConnectionMapCityAggregate> AggregateConnectionCities(
        IReadOnlyList<Device> devices,
        IReadOnlyList<TunnelSession> sessions)
    {
        return devices
            .GroupJoin(
                sessions,
                device => device.Id,
                session => session.DeviceId,
                (device, matchingSessions) => new
                {
                    Device = device,
                    GatewayIds = matchingSessions.Select(session => session.GatewayId).Distinct(StringComparer.Ordinal).ToArray()
                })
            .GroupBy(item => new { item.Device.City, item.Device.Country, item.Device.TenantId })
            .Select(group => new ConnectionMapCityAggregate(
                group.Key.City,
                group.Key.Country,
                group.Count(),
                group.Count(item => item.Device.ConnectionState == ConnectionState.Healthy),
                group.Count(item =>
                    item.Device.ConnectionState is ConnectionState.LocalNetworkPoor or ConnectionState.LowBandwidth or ConnectionState.HighJitter or ConnectionState.GatewayDegraded or ConnectionState.ServerUnavailable),
                group.Count(item =>
                    item.Device.ConnectionState is ConnectionState.PolicyBlocked or ConnectionState.AuthExpired),
                group.SelectMany(item => item.GatewayIds).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                group.Key.TenantId))
            .OrderByDescending(city => city.ImpactedCount)
            .ThenByDescending(city => city.DeviceCount)
            .ThenBy(city => city.City, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<GatewayPool> BuildSyntheticPools(IReadOnlyList<Gateway> gateways) =>
        gateways
            .GroupBy(gateway => gateway.TenantId, StringComparer.Ordinal)
            .Select(group => new GatewayPool(
                $"pool-{group.Key}-default",
                $"{group.Key} Default Pool",
                group.Select(gateway => gateway.Region).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                group.Select(gateway => gateway.Id).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                group.Key))
            .ToArray();

    private static DeviceDiagnostics ClassifyJitter(
        Device device,
        HealthSample? sample,
        TunnelSession? session,
        Gateway? gateway,
        DateTimeOffset observedAt,
        IReadOnlyList<string> signals)
    {
        var local = sample is not null && (sample.SignalStrengthPercent < 75 || sample.PacketLossPercent >= 1.0m);
        return new DeviceDiagnostics(
            device.Id,
            device.Name,
            device.ConnectionState,
            local ? DiagnosticScope.LocalNetwork : gateway is null ? DiagnosticScope.ServerSide : DiagnosticScope.Gateway,
            sample?.Severity ?? HealthSeverity.Yellow,
            local ? "Wireless conditions are causing elevated jitter." : "Jitter is rising beyond the gateway threshold.",
            local
                ? "Signal quality or packet loss is unstable on the client side before traffic reaches the gateway."
                : gateway is null
                    ? "Back-end response times are spiking even though the local link still looks healthy."
                    : "The active gateway is responding, but latency variance is above the supported threshold.",
            session?.GatewayId,
            gateway?.Name,
            observedAt,
            signals,
            device.TenantId);
    }

    private static bool IsLikelyLocalTransport(HealthSample? sample) =>
        sample is not null && (!sample.DnsReachable || sample.SignalStrengthPercent < 60 || sample.PacketLossPercent >= 2.0m);

    private static HealthSeverity InferSeverity(ConnectionState state) =>
        state switch
        {
            ConnectionState.Healthy => HealthSeverity.Green,
            ConnectionState.LocalNetworkPoor or ConnectionState.LowBandwidth or ConnectionState.HighJitter or ConnectionState.GatewayDegraded => HealthSeverity.Yellow,
            _ => HealthSeverity.Red
        };

    private static IReadOnlyList<string> BuildSignals(HealthSample? sample, Gateway? gateway, DateTimeOffset now)
    {
        var signals = new List<string>();
        if (sample is not null)
        {
            signals.Add($"latency:{sample.LatencyMs}ms");
            signals.Add($"jitter:{sample.JitterMs}ms");
            signals.Add($"loss:{sample.PacketLossPercent:0.##}%");
            signals.Add($"signal:{sample.SignalStrengthPercent}%");
            signals.Add(sample.DnsReachable ? "dns:reachable" : "dns:failed");
            signals.Add(sample.RouteHealthy ? "route:healthy" : "route:degraded");
        }

        if (gateway is not null)
        {
            var score = ScoreGateway(gateway, now);
            signals.Add($"gateway_score:{score.Score}");
            signals.Add($"gateway_health:{score.Health}");
        }

        return signals;
    }
}
