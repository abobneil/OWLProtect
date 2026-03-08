import { seededSnapshot, type Alert, type Device, type Gateway, type HealthSeverity } from "@owlprotect/contracts";

const severityLabel: Record<HealthSeverity, string> = {
  green: "Healthy",
  yellow: "Attention",
  red: "Critical"
};

function statusClass(severity: HealthSeverity) {
  return `status-pill status-pill--${severity}`;
}

function DeviceRow({ device }: { device: Device }) {
  return (
    <tr>
      <td>{device.name}</td>
      <td>{device.city}, {device.country}</td>
      <td>{device.connectionState}</td>
      <td>{device.postureScore}</td>
      <td>{device.managed ? "Managed" : "Unmanaged"}</td>
      <td>{device.lastSeenUtc}</td>
    </tr>
  );
}

function GatewayCard({ gateway }: { gateway: Gateway }) {
  return (
    <article className="metric-card">
      <div className="metric-card__header">
        <h3>{gateway.name}</h3>
        <span className={statusClass(gateway.health)}>{severityLabel[gateway.health]}</span>
      </div>
      <dl className="metric-grid">
        <div>
          <dt>Region</dt>
          <dd>{gateway.region}</dd>
        </div>
        <div>
          <dt>Peers</dt>
          <dd>{gateway.peerCount}</dd>
        </div>
        <div>
          <dt>Load</dt>
          <dd>{gateway.loadPercent}%</dd>
        </div>
        <div>
          <dt>Latency</dt>
          <dd>{gateway.latencyMs} ms</dd>
        </div>
      </dl>
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
  const totalDevices = seededSnapshot.devices.length;
  const healthyDevices = seededSnapshot.devices.filter((device) => device.connectionState === "Healthy").length;
  const enabledUsers = seededSnapshot.users.filter((user) => user.enabled).length;
  const admin = seededSnapshot.admins[0];

  return (
    <div className="shell">
      <aside className="sidebar">
        <div>
          <p className="eyebrow">OWLProtect</p>
          <h1>Admin Portal</h1>
          <p className="muted">
            Windows-first enterprise VPN with live diagnostics, seeded bootstrap accounts, and active-active gateways.
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
            <p className="eyebrow">Bootstrap State</p>
            <h2>Default admin must rotate password and enroll MFA.</h2>
            <p className="muted">
              Account <strong>{admin.username}</strong> is seeded with forced password reset and blocked admin actions
              until MFA is enrolled. Test user <strong>user</strong> remains disabled until explicitly enabled.
            </p>
          </div>
          <div className="hero__badges">
            <span className="tag">Dark mode default</span>
            <span className="tag">WireGuard UDP</span>
            <span className="tag">Entra + OIDC</span>
          </div>
        </section>

        <section className="stats">
          <article className="stat-card">
            <span className="stat-card__label">Connected devices</span>
            <strong>{healthyDevices}/{totalDevices}</strong>
            <p>Devices reporting healthy tunnel state.</p>
          </article>
          <article className="stat-card">
            <span className="stat-card__label">Enabled users</span>
            <strong>{enabledUsers}</strong>
            <p>Seeded test user is disabled by policy.</p>
          </article>
          <article className="stat-card">
            <span className="stat-card__label">Gateway pools</span>
            <strong>{seededSnapshot.gatewayPools.length}</strong>
            <p>Active-active gateway failover ready.</p>
          </article>
          <article className="stat-card">
            <span className="stat-card__label">Live alerts</span>
            <strong>{seededSnapshot.alerts.length}</strong>
            <p>Near-real-time health and policy signals.</p>
          </article>
        </section>

        <section className="panel-grid">
          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Gateway Health</p>
                <h2>Regional gateway pool</h2>
              </div>
              <span className="muted">WebSocket-backed every 10 seconds</span>
            </div>
            <div className="gateway-grid">
              {seededSnapshot.gateways.map((gateway) => (
                <GatewayCard gateway={gateway} key={gateway.id} />
              ))}
            </div>
          </section>

          <section className="panel panel--map">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Connection Map</p>
                <h2>City-level device visibility</h2>
              </div>
              <span className="muted">MapLibre + GeoLite2 planned</span>
            </div>
            <div className="map-card">
              {seededSnapshot.devices.map((device) => (
                <div className="map-pin" key={device.id}>
                  <span className="map-pin__dot" />
                  <div>
                    <strong>{device.city}</strong>
                    <p>{device.name}</p>
                  </div>
                </div>
              ))}
            </div>
          </section>
        </section>

        <section className="panel-grid">
          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Device Fleet</p>
                <h2>Policy and posture-aware sessions</h2>
              </div>
              <span className="muted">UI refresh target under 5 seconds</span>
            </div>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Device</th>
                  <th>Location</th>
                  <th>State</th>
                  <th>Posture</th>
                  <th>Management</th>
                  <th>Last Seen</th>
                </tr>
              </thead>
              <tbody>
                {seededSnapshot.devices.map((device) => (
                  <DeviceRow device={device} key={device.id} />
                ))}
              </tbody>
            </table>
          </section>

          <section className="panel">
            <div className="panel__header">
              <div>
                <p className="eyebrow">Alerts</p>
                <h2>Traffic-light incident feed</h2>
              </div>
              <span className="muted">Good = green, warning = yellow, bad = red</span>
            </div>
            <AlertList alerts={seededSnapshot.alerts} />
          </section>
        </section>
      </main>
    </div>
  );
}

