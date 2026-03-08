import {
  seededConnectionCityAggregates,
  seededDeviceDiagnostics,
  seededGatewayPoolStatuses,
  seededSnapshot,
  type Alert,
  type ConnectionMapCityAggregate,
  type DeviceDiagnostics,
  type GatewayPoolStatus,
  type HealthSeverity
} from "@owlprotect/contracts";

const severityLabel: Record<HealthSeverity, string> = {
  green: "Healthy",
  yellow: "Attention",
  red: "Critical"
};

const scopeLabel: Record<DeviceDiagnostics["scope"], string> = {
  Healthy: "Healthy",
  LocalNetwork: "Local network",
  Gateway: "Gateway",
  ServerSide: "Server side",
  Policy: "Policy",
  Authentication: "Authentication"
};

function statusClass(severity: HealthSeverity) {
  return `status-pill status-pill--${severity}`;
}

function GatewayPoolCard({ pool }: { pool: GatewayPoolStatus }) {
  const primary = pool.gateways.find((gateway) => gateway.gatewayId === pool.primaryGatewayId) ?? null;

  return (
    <article className="metric-card metric-card--pool">
      <div className="metric-card__header">
        <div>
          <h3>{pool.name}</h3>
          <p className="muted">Primary: {primary?.gatewayName ?? "No healthy gateway"}</p>
        </div>
        <span className={statusClass(pool.health)}>{severityLabel[pool.health]}</span>
      </div>
      <div className="metric-grid">
        <div>
          <dt>Score</dt>
          <dd>{pool.score}</dd>
        </div>
        <div>
          <dt>Regions</dt>
          <dd>{pool.regions.join(", ")}</dd>
        </div>
        <div>
          <dt>Failover</dt>
          <dd>{pool.failoverGatewayIds.length}</dd>
        </div>
        <div>
          <dt>Members</dt>
          <dd>{pool.gateways.length}</dd>
        </div>
      </div>
      <div className="stack-list">
        {pool.gateways.map((gateway) => (
          <div className="stack-row" key={gateway.gatewayId}>
            <div>
              <strong>{gateway.gatewayName}</strong>
              <p className="muted">{gateway.region} | {gateway.latencyMs} ms | {gateway.loadPercent}% load</p>
            </div>
            <span className={statusClass(gateway.health)}>{gateway.score}</span>
          </div>
        ))}
      </div>
    </article>
  );
}

function CityCard({ city }: { city: ConnectionMapCityAggregate }) {
  return (
    <article className="map-cluster">
      <div className="map-cluster__header">
        <div>
          <strong>{city.city}</strong>
          <p>{city.country}</p>
        </div>
        <span>{city.deviceCount} device{city.deviceCount === 1 ? "" : "s"}</span>
      </div>
      <div className="map-cluster__stats">
        <span>Healthy {city.healthyCount}</span>
        <span>Impacted {city.impactedCount}</span>
        <span>Blocked {city.blockedCount}</span>
      </div>
      <p className="muted">Gateways: {city.gatewayIds.length > 0 ? city.gatewayIds.join(", ") : "No active sessions"}</p>
    </article>
  );
}

function DiagnosticCard({ diagnostic }: { diagnostic: DeviceDiagnostics }) {
  return (
    <article className="diagnostic-card">
      <div className="diagnostic-card__header">
        <div>
          <h3>{diagnostic.deviceName}</h3>
          <p className="muted">{scopeLabel[diagnostic.scope]} | {diagnostic.gatewayName ?? "No active gateway"}</p>
        </div>
        <span className={statusClass(diagnostic.severity)}>{severityLabel[diagnostic.severity]}</span>
      </div>
      <p>{diagnostic.summary}</p>
      <p className="muted">{diagnostic.detail}</p>
      <div className="signal-list">
        {diagnostic.signals.map((signal) => (
          <span className="tag tag--subtle" key={signal}>{signal}</span>
        ))}
      </div>
    </article>
  );
}

function AlertList({ alerts }: { alerts: Alert[] }) {
  return (
    <div className="alerts">
      {alerts.map((alert) => (
        <article className="alert-card" key={alert.id}>
          <div className="alert-card__title">
            <span className={statusClass(alert.severity)}>{severityLabel[alert.severity]}</span>
            <h3>{alert.title}</h3>
          </div>
          <p>{alert.description}</p>
          <span className="timestamp">{alert.createdAtUtc}</span>
        </article>
      ))}
    </div>
  );
}

export function App() {
  const healthyDevices = seededSnapshot.devices.filter((device) => device.connectionState === "Healthy").length;
  const admin = seededSnapshot.admins[0];
  const gatewayIssues = seededDeviceDiagnostics.filter((diagnostic) => diagnostic.scope === "Gateway").length;
  const localIssues = seededDeviceDiagnostics.filter((diagnostic) => diagnostic.scope === "LocalNetwork").length;
  const poolScore = Math.round(seededGatewayPoolStatuses.reduce((sum, pool) => sum + pool.score, 0) / seededGatewayPoolStatuses.length);

  return (
    <div className="shell">
      <aside className="sidebar">
        <div>
          <p className="eyebrow">OWLProtect</p>
          <h1>Admin Portal</h1>
          <p className="muted">
            Gateway pool scoring, client failover, diagnostics classification, and city-level fleet visibility.
          </p>
        </div>

        <nav className="nav">
          {[
            "Bootstrap",
            "Dashboard",
            "Fleet Health",
            "Gateways",
            "Users",
            "Groups",
            "Policies",
            "Auth Providers",
            "Audit Log",
            "Alerts",
            "Settings"
          ].map((item) => (
            <a className={item === "Dashboard" ? "nav__item nav__item--active" : "nav__item"} href="/" key={item}>
              {item}
            </a>
          ))}
        </nav>
      </aside>

      <main className="content">
        <section className="hero">
          <div>
            <p className="eyebrow">Gateway Milestone</p>
            <h2>Primary placement is scored, failover-ready, and diagnostics are classified by fault domain.</h2>
            <p className="muted">
              Account <strong>{admin.username}</strong> still requires password rotation and MFA enrollment, but gateway pools now
              expose a ranked primary, warm failover candidates, and city-level device aggregation for operator triage.
            </p>
          </div>
          <div className="hero__badges">
            <span className="tag">Pool score {poolScore}</span>
            <span className="tag">Gateway issues {gatewayIssues}</span>
            <span className="tag">Local issues {localIssues}</span>
          </div>
        </section>

        <section className="stats">
          <article className="stat-card">
            <span className="stat-card__label">Healthy devices</span>
            <strong>{healthyDevices}/{seededSnapshot.devices.length}</strong>
            <p>Tunnels within latency and posture thresholds.</p>
          </article>
          <article className="stat-card">
            <span className="stat-card__label">Gateway pools</span>
            <strong>{seededGatewayPoolStatuses.length}</strong>
            <p>Each pool now advertises a ranked primary and failover chain.</p>
          </article>
          <article className="stat-card">
            <span className="stat-card__label">Cities on map</span>
            <strong>{seededConnectionCityAggregates.length}</strong>
            <p>Fleet geography is aggregated at city level for operators.</p>
          </article>
          <article className="stat-card">
            <span className="stat-card__label">Diagnostics split</span>
            <strong>{localIssues}:{gatewayIssues}</strong>
            <p>Local-network versus gateway-originated active issues.</p>
          </article>
        </section>

        <section className="panel-grid">
          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Gateway Health</p>
                <h2>Scored pools and failover readiness</h2>
              </div>
              <span className="muted">Selection prefers healthy latency headroom and warm redundancy</span>
            </div>
            <div className="gateway-grid">
              {seededGatewayPoolStatuses.map((pool) => (
                <GatewayPoolCard pool={pool} key={pool.poolId} />
              ))}
            </div>
          </section>

          <section className="panel panel--map">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Connection Map</p>
                <h2>City-level fleet aggregation</h2>
              </div>
              <span className="muted">Approximate geography grouped by city and active gateway footprint</span>
            </div>
            <div className="map-card">
              {seededConnectionCityAggregates.map((city) => (
                <CityCard city={city} key={`${city.city}-${city.country}`} />
              ))}
            </div>
          </section>
        </section>

        <section className="panel-grid">
          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Diagnostics</p>
                <h2>Fault-domain classification</h2>
              </div>
              <span className="muted">User-visible issues are separated into local, gateway, policy, auth, and server-side causes</span>
            </div>
            <div className="diagnostic-grid">
              {seededDeviceDiagnostics.map((diagnostic) => (
                <DiagnosticCard diagnostic={diagnostic} key={diagnostic.deviceId} />
              ))}
            </div>
          </section>

          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Alerts</p>
                <h2>Traffic-light incident feed</h2>
              </div>
              <span className="muted">Priority follows the same health scale used by the gateway pools</span>
            </div>
            <AlertList alerts={seededSnapshot.alerts} />
          </section>
        </section>
      </main>
    </div>
  );
}
