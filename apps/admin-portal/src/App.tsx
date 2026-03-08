import { useDeferredValue, useEffect, useState, type FormEvent, type ReactNode } from "react";
import { BrowserRouter, NavLink, Navigate, Outlet, Route, Routes, useLocation, useNavigate } from "react-router-dom";
import {
  type Alert,
  type AuditEvent,
  type AuditExportResponse,
  type AuthProviderConfig,
  type AuthProviderUpsertRequest,
  type BootstrapStatus,
  type ConnectionMapCityAggregate,
  type DeviceDiagnostics,
  type DeviceRegistrationState,
  type Gateway,
  type GatewayUpsertRequest,
  type GatewayPoolStatus,
  type HealthSeverity,
  type PasswordChangeRequest,
  type PolicyRule,
  type PolicyUpsertRequest,
  type Tenant,
  type TunnelSession,
  type User,
  type UserUpsertRequest
} from "@owlprotect/contracts";
import { PortalProvider, usePortal } from "./portal";

const severityLabel: Record<HealthSeverity, string> = {
  green: "Healthy",
  yellow: "Attention",
  red: "Critical"
};

const scopeLabel: Record<DeviceDiagnostics["scope"], string> = {
  Authentication: "Authentication",
  Gateway: "Gateway",
  Healthy: "Healthy",
  LocalNetwork: "Local network",
  Policy: "Policy",
  ServerSide: "Server side"
};

const registrationStateLabel: Record<DeviceRegistrationState, string> = {
  Disabled: "Disabled",
  Enrolled: "Enrolled",
  Pending: "Pending",
  Revoked: "Revoked"
};

export function App() {
  return (
    <BrowserRouter>
      <PortalProvider>
        <AppRoutes />
      </PortalProvider>
    </BrowserRouter>
  );
}

function AppRoutes() {
  const portal = usePortal();

  return (
    <Routes>
      <Route
        path="/login"
        element={portal.authSession ? <Navigate replace to={portal.hasCompliantAdmin ? "/dashboard" : "/bootstrap"} /> : <LoginPage />}
      />
      <Route path="/*" element={portal.authSession ? <ShellLayout /> : <Navigate replace to="/login" />}>
        <Route index element={<Navigate replace to={portal.hasCompliantAdmin ? "/dashboard" : "/bootstrap"} />} />
        <Route path="bootstrap" element={<BootstrapPage />} />
        <Route path="dashboard" element={<DashboardPage />} />
        <Route path="fleet" element={<FleetHealthPage />} />
        <Route path="gateways" element={<GatewaysPage />} />
        <Route path="users" element={<UsersPage />} />
        <Route path="groups" element={<GroupsPage />} />
        <Route path="policies" element={<PoliciesPage />} />
        <Route path="providers" element={<ProvidersPage />} />
        <Route path="alerts" element={<AlertsPage />} />
        <Route path="audit" element={<AuditPage />} />
        <Route path="settings" element={<SettingsPage />} />
      </Route>
    </Routes>
  );
}

function ShellLayout() {
  const portal = usePortal();
  const location = useLocation();

  if (!portal.hasCompliantAdmin && location.pathname !== "/bootstrap") {
    return <Navigate replace to="/bootstrap" />;
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="sidebar__brand">
          <p className="eyebrow">OWLProtect</p>
          <h1>Admin Portal</h1>
          <p className="muted">Live operator workflows on top of the control-plane API and event streams.</p>
        </div>

        <nav className="nav">
          {[
            ["Bootstrap", "/bootstrap"],
            ["Dashboard", "/dashboard"],
            ["Fleet Health", "/fleet"],
            ["Gateways", "/gateways"],
            ["Users", "/users"],
            ["Groups", "/groups"],
            ["Policies", "/policies"],
            ["Auth Providers", "/providers"],
            ["Alerts", "/alerts"],
            ["Audit Log", "/audit"],
            ["Settings", "/settings"]
          ].map(([label, path]) => (
            <NavLink
              className={({ isActive }) => isActive ? "nav__item nav__item--active" : "nav__item"}
              key={path}
              to={path}
            >
              {label}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar__footer">
          <div className="operator-card">
            <span className="status-dot" />
            <div>
              <strong>{portal.authSession?.admin.username}</strong>
              <p className="muted">{portal.authSession?.admin.role}</p>
            </div>
          </div>

          <button className="button button--ghost" onClick={() => void portal.signOut()} type="button">
            Sign out
          </button>
        </div>
      </aside>

      <div className="workspace">
        <TopBar />
        <main className="content">
          <Outlet />
        </main>
      </div>

      <StepUpModal />
    </div>
  );
}

function TopBar() {
  const portal = usePortal();

  return (
    <header className="topbar">
      <div>
        <p className="eyebrow">Control Plane</p>
        <strong>{portal.apiBaseUrl}</strong>
      </div>

      <div className="topbar__status">
        <span className="tag">Bootstrap {renderFetchState(portal.bootstrapState)}</span>
        <span className="tag">Data {renderFetchState(portal.dataState)}</span>
        <span className={portal.hasActiveStepUp ? "tag tag--good" : "tag"}>{portal.hasActiveStepUp ? "Step-up active" : "Step-up idle"}</span>
        {Object.entries(portal.streams).map(([key, stream]) => (
          <span className={stream.state === "live" ? "tag tag--good" : "tag"} key={key}>
            {key} {stream.state}
          </span>
        ))}
        <button className="button button--ghost" onClick={() => void portal.refreshAll()} type="button">
          Refresh
        </button>
      </div>
    </header>
  );
}

function LoginPage() {
  const portal = usePortal();
  const navigate = useNavigate();
  const [username, setUsername] = useState("admin");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    if (portal.authSession) {
      navigate(portal.hasCompliantAdmin ? "/dashboard" : "/bootstrap", { replace: true });
    }
  }, [navigate, portal.authSession, portal.hasCompliantAdmin]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);

    try {
      await portal.signIn({ password, username });
      navigate("/bootstrap", { replace: true });
    } catch (nextError) {
      setError(toErrorMessage(nextError));
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="auth-layout">
      <section className="auth-panel auth-panel--intro">
        <p className="eyebrow">Admin Milestone</p>
        <h1>Routed admin experience with bootstrap guardrails, streamed health, and reusable workflows.</h1>
        <p className="muted">
          The portal now authenticates against the control plane, gates privileged routes until bootstrap is complete,
          and keeps alerts and health views live with WebSocket reconnect handling.
        </p>
        <div className="stack-list">
          <FeatureCard body="Route structure covers bootstrap, dashboard, management, alerts, audit, and settings." title="Routed shell" />
          <FeatureCard body="The same table and form patterns handle loading, validation, empty, and failure states." title="Shared workflows" />
          <FeatureCard body="Alerts, gateway health, sessions, and telemetry are wired through reconnecting stream clients." title="Live operations" />
        </div>
      </section>

      <section className="auth-panel">
        <div className="panel__header">
          <div>
            <p className="eyebrow">Operator Sign-In</p>
            <h2>Bootstrap admin login</h2>
          </div>
          <StatusChip label="bootstrap" status={portal.bootstrapState} />
        </div>

        <BootstrapSummary bootstrapStatus={portal.bootstrapStatus} error={portal.bootstrapError} />

        <form className="form-grid" onSubmit={handleSubmit}>
          <label className="field">
            <span>Username</span>
            <input onChange={(event) => setUsername(event.target.value)} value={username} />
          </label>
          <label className="field">
            <span>Password</span>
            <input onChange={(event) => setPassword(event.target.value)} type="password" value={password} />
          </label>
          {error ? <Banner tone="danger">{error}</Banner> : null}
          <div className="form-actions">
            <button className="button" disabled={submitting} type="submit">
              {submitting ? "Signing in..." : "Sign in"}
            </button>
          </div>
        </form>
      </section>
    </div>
  );
}

function BootstrapPage() {
  const portal = usePortal();
  const navigate = useNavigate();
  const [passwordForm, setPasswordForm] = useState<PasswordChangeRequest>({ currentPassword: "", newPassword: "" });
  const [passwordState, setPasswordState] = useState<{ error: string | null; submitting: boolean; success: string | null }>({ error: null, submitting: false, success: null });
  const [mfaState, setMfaState] = useState<{ error: string | null; submitting: boolean; success: string | null }>({ error: null, submitting: false, success: null });
  const admin = portal.authSession?.admin;

  useEffect(() => {
    if (portal.hasCompliantAdmin) {
      navigate("/dashboard", { replace: true });
    }
  }, [navigate, portal.hasCompliantAdmin]);

  async function handlePasswordSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setPasswordState({ error: null, submitting: true, success: null });

    try {
      await portal.updateBootstrapPassword(passwordForm);
      setPasswordForm({ currentPassword: "", newPassword: "" });
      setPasswordState({ error: null, submitting: false, success: "Password rotation recorded." });
    } catch (error) {
      setPasswordState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  async function handleEnrollMfa() {
    setMfaState({ error: null, submitting: true, success: null });

    try {
      await portal.enrollBootstrapMfa();
      setMfaState({ error: null, submitting: false, success: "MFA enrollment recorded." });
    } catch (error) {
      setMfaState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  return (
    <div className="page-stack">
      <PageHero
        eyebrow="Bootstrap"
        title="Complete the bootstrap ceremony before the rest of the portal unlocks."
        subtitle="The UI matches the backend authorization policy: password rotation and MFA enrollment are visible and required before other admin routes are available."
        badges={[
          admin?.mustChangePassword ? "Password rotation required" : "Password rotated",
          admin?.mfaEnrolled ? "MFA enrolled" : "MFA required"
        ]}
      />

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Status" title="Bootstrap checklist" detail="Driven by the control-plane bootstrap endpoint." />
          <BootstrapChecklist bootstrapStatus={portal.bootstrapStatus} />
        </Panel>

        <Panel>
          <SectionHeading eyebrow="Operator" title="Current admin" detail={`${admin?.username} · ${admin?.role}`} />
          <div className="metric-grid">
            <Metric label="Password" value={admin?.mustChangePassword ? "Pending" : "Complete"} />
            <Metric label="MFA" value={admin?.mfaEnrolled ? "Complete" : "Pending"} />
            <Metric label="Test user" value={portal.bootstrapStatus?.testUserEnabled ? "Enabled" : "Disabled"} />
            <Metric label="Auto-disable" value={portal.bootstrapStatus?.testUserAutoDisableAtUtc ? formatDateTime(portal.bootstrapStatus.testUserAutoDisableAtUtc) : "Not scheduled"} />
          </div>
        </Panel>
      </section>

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Password" title="Rotate bootstrap password" detail="Required once for the seeded admin account." />
          <form className="form-grid" onSubmit={handlePasswordSubmit}>
            <label className="field">
              <span>Current password</span>
              <input onChange={(event) => setPasswordForm((current) => ({ ...current, currentPassword: event.target.value }))} type="password" value={passwordForm.currentPassword} />
            </label>
            <label className="field">
              <span>New password</span>
              <input onChange={(event) => setPasswordForm((current) => ({ ...current, newPassword: event.target.value }))} type="password" value={passwordForm.newPassword} />
            </label>
            {passwordState.error ? <Banner tone="danger">{passwordState.error}</Banner> : null}
            {passwordState.success ? <Banner tone="success">{passwordState.success}</Banner> : null}
            <div className="form-actions">
              <button className="button" disabled={passwordState.submitting} type="submit">
                {passwordState.submitting ? "Updating..." : "Rotate password"}
              </button>
            </div>
          </form>
        </Panel>

        <Panel>
          <SectionHeading eyebrow="MFA" title="Enroll multi-factor auth" detail="This is the second bootstrap requirement enforced by the portal." />
          <p className="muted">The current backend models MFA enrollment as a bootstrap action, so the portal exposes a direct operator workflow for it.</p>
          {mfaState.error ? <Banner tone="danger">{mfaState.error}</Banner> : null}
          {mfaState.success ? <Banner tone="success">{mfaState.success}</Banner> : null}
          <div className="form-actions">
            <button className="button" disabled={mfaState.submitting || admin?.mfaEnrolled} onClick={() => void handleEnrollMfa()} type="button">
              {admin?.mfaEnrolled ? "MFA already enrolled" : mfaState.submitting ? "Enrolling..." : "Enroll MFA"}
            </button>
          </div>
        </Panel>
      </section>
    </div>
  );
}

function DashboardPage() {
  const portal = usePortal();
  const healthyDevices = portal.data.devices.filter((device) => device.connectionState === "Healthy").length;
  const openIncidents = portal.data.alerts.filter((alert) => alert.severity !== "green").length;
  const averagePoolScore = portal.data.gatewayPoolStatuses.length
    ? Math.round(portal.data.gatewayPoolStatuses.reduce((sum, pool) => sum + pool.score, 0) / portal.data.gatewayPoolStatuses.length)
    : 0;

  return (
    <div className="page-stack">
      <PageHero
        eyebrow="Dashboard"
        title="Bootstrap is guarded, the portal is routed, and operator data is live."
        subtitle="The dashboard blends control-plane reads with live alert and gateway-health stream status so operators can move directly into action."
        badges={[
          `Healthy devices ${healthyDevices}/${portal.data.devices.length || 0}`,
          `Open incidents ${openIncidents}`,
          `Average pool score ${averagePoolScore}`
        ]}
      />

      <section className="stats-grid">
        <StatCard caption="Managed user records in the current tenant set." label="Users" value={`${portal.data.users.length}`} />
        <StatCard caption="Gateway inventory streamed from the control plane." label="Gateways" value={`${portal.data.gateways.length}`} />
        <StatCard caption="Operator-visible alerts sorted by recency." label="Alerts" value={`${portal.data.alerts.length}`} />
        <StatCard caption="Append-only audit history available for review and export." label="Audit events" value={`${portal.data.auditEvents.length}`} />
      </section>

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Gateway Health" title="Scored pools and failover placement" detail="Pool status combines live gateway reads with the pool-health endpoint." />
          <EntityState emptyMessage="No gateway pools are available." error={portal.dataError} items={portal.data.gatewayPoolStatuses} state={portal.dataState}>
            <div className="card-grid">
              {portal.data.gatewayPoolStatuses.map((pool) => (
                <GatewayPoolCard key={pool.poolId} pool={pool} />
              ))}
            </div>
          </EntityState>
        </Panel>

        <Panel>
          <SectionHeading eyebrow="Alerts" title="Live incident feed" detail={`Alert stream is ${portal.streams.alerts.state}.`} />
          <EntityState emptyMessage="No alerts are active." error={portal.dataError} items={portal.data.alerts} state={portal.dataState}>
            <div className="stack-list">
              {portal.data.alerts.slice(0, 6).map((alert) => (
                <AlertCard alert={alert} key={alert.id} />
              ))}
            </div>
          </EntityState>
        </Panel>
      </section>

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Connection Map" title="City-level fleet aggregation" detail="Grouped by city rather than exact client coordinates." />
          <EntityState emptyMessage="No city aggregates are available." error={portal.dataError} items={portal.data.connectionMap} state={portal.dataState}>
            <div className="card-grid">
              {portal.data.connectionMap.map((city) => (
                <ConnectionMapCard city={city} key={`${city.city}-${city.country}`} />
              ))}
            </div>
          </EntityState>
        </Panel>

        <Panel>
          <SectionHeading eyebrow="Audit" title="Recent change ledger" detail="Pulled from the append-only audit log endpoint." />
          <EntityState emptyMessage="No audit events are available." error={portal.dataError} items={portal.data.auditEvents} state={portal.dataState}>
            <div className="stack-list">
              {portal.data.auditEvents.slice(0, 6).map((event) => (
                <AuditEventCard event={event} key={event.id} />
              ))}
            </div>
          </EntityState>
        </Panel>
      </section>
    </div>
  );
}

function FleetHealthPage() {
  const portal = usePortal();

  return (
    <div className="page-stack">
      <PageHero
        eyebrow="Fleet Health"
        title="Diagnostics, sessions, and geography are split by fault domain."
        subtitle="Telemetry and session streams reconnect automatically and refresh diagnostics, sessions, and city aggregates inside the routed shell."
        badges={[
          `Telemetry ${portal.streams.telemetry.state}`,
          `Sessions ${portal.streams.sessions.state}`,
          `Diagnostics ${portal.data.diagnostics.length}`
        ]}
      />

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Diagnostics" title="Fault-domain classification" detail="Local network, gateway, policy, authentication, and server-side issues are separated for triage." />
          <EntityState emptyMessage="No diagnostics are available." error={portal.dataError} items={portal.data.diagnostics} state={portal.dataState}>
            <div className="stack-list">
              {portal.data.diagnostics.map((diagnostic) => (
                <DiagnosticCard diagnostic={diagnostic} key={diagnostic.deviceId} />
              ))}
            </div>
          </EntityState>
        </Panel>

        <Panel>
          <SectionHeading eyebrow="Cities" title="Current connection map" detail="Live city aggregates from the map endpoint." />
          <EntityState emptyMessage="No city aggregates are available." error={portal.dataError} items={portal.data.connectionMap} state={portal.dataState}>
            <div className="card-grid">
              {portal.data.connectionMap.map((city) => (
                <ConnectionMapCard city={city} key={`${city.city}-${city.country}-fleet`} />
              ))}
            </div>
          </EntityState>
        </Panel>
      </section>

      <Panel>
        <SectionHeading eyebrow="Sessions" title="Active tunnel sessions" detail="Privileged revocation requires step-up and updates optimistically." />
        <EntityState emptyMessage="No active sessions are present." error={portal.dataError} items={portal.data.sessions} state={portal.dataState}>
          <DataTable
            columns={[
              { header: "User", render: (session) => findUserLabel(portal.data.users, session.userId) },
              { header: "Device", render: (session) => findDeviceLabel(portal.data.devices, session.deviceId) },
              { header: "Gateway", render: (session) => findGatewayLabel(portal.data.gateways, session.gatewayId) },
              { header: "Policy bundle", render: (session) => session.policyBundleVersion },
              { header: "Connected", render: (session) => formatDateTime(session.connectedAtUtc) },
              { header: "Throughput", render: (session) => `${session.throughputMbps} Mbps` },
              {
                header: "Actions",
                render: (session) => (
                  <button className="button button--ghost" onClick={() => void portal.revokeSession(session.id)} type="button">
                    Revoke
                  </button>
                )
              }
            ]}
            rows={portal.data.sessions}
          />
        </EntityState>
      </Panel>
    </div>
  );
}

function UsersPage() {
  const portal = usePortal();
  const [selectedUserId, setSelectedUserId] = useState<string | null>(portal.data.users[0]?.id ?? null);
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search);
  const selectedUser = portal.data.users.find((user) => user.id === selectedUserId) ?? null;
  const [form, setForm] = useState<ReturnType<typeof toUserRequest>>(() => toUserRequest(selectedUser, portal.data.tenants));
  const [state, setState] = useState<{ error: string | null; submitting: boolean; success: string | null }>({ error: null, submitting: false, success: null });

  useEffect(() => {
    setForm(toUserRequest(selectedUser, portal.data.tenants));
  }, [selectedUser, portal.data.tenants]);

  const filteredUsers = portal.data.users.filter((user) =>
    `${user.username} ${user.displayName} ${user.provider}`.toLowerCase().includes(deferredSearch.toLowerCase())
  );

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ error: null, submitting: true, success: null });

    try {
      const updated = await portal.upsertUser(form);
      setSelectedUserId(updated.id);
      setState({ error: null, submitting: false, success: "User workflow saved." });
    } catch (error) {
      setState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  return (
    <EntityPageLayout description="Reusable user management with loading, validation, empty, optimistic, and failure states." title="Users">
      <Panel>
        <SectionHeading eyebrow="Directory" title="User records" detail={`${filteredUsers.length} visible of ${portal.data.users.length}.`} />
        <label className="field field--compact">
          <span>Search</span>
          <input onChange={(event) => setSearch(event.target.value)} placeholder="Filter by username or provider" value={search} />
        </label>
        <EntityState emptyMessage="No users are available." error={portal.dataError} items={filteredUsers} state={portal.dataState}>
          <DataTable
            columns={[
              { header: "Username", render: (user) => <button className="link-button" onClick={() => setSelectedUserId(user.id)} type="button">{user.username}</button> },
              { header: "Display name", render: (user) => user.displayName },
              { header: "Provider", render: (user) => user.provider },
              { header: "Groups", render: (user) => joinList(user.groupIds) },
              { header: "Policies", render: (user) => joinList(user.policyIds) },
              { header: "Status", render: (user) => <span className={user.enabled ? "status-pill status-pill--green" : "status-pill status-pill--red"}>{user.enabled ? "Enabled" : "Disabled"}</span> },
              {
                header: "Actions",
                render: (user) => (
                  <div className="table-actions">
                    <button className="button button--ghost" onClick={() => void (user.enabled ? portal.disableUser(user.id) : portal.enableUser(user.id))} type="button">
                      {user.enabled ? "Disable" : "Enable"}
                    </button>
                    <button className="button button--ghost" onClick={() => void portal.deleteUser(user.id)} type="button">
                      Delete
                    </button>
                  </div>
                )
              }
            ]}
            rows={filteredUsers}
          />
        </EntityState>
      </Panel>

      <Panel>
        <SectionHeading eyebrow="Editor" title={selectedUser ? `Edit ${selectedUser.username}` : "Create user"} detail="Shared form pattern with validation, failure banners, and explicit success confirmation." />
        <form className="form-grid" onSubmit={handleSubmit}>
          <label className="field">
            <span>Username</span>
            <input onChange={(event) => setForm((current) => ({ ...current, username: event.target.value }))} value={form.username} />
          </label>
          <label className="field">
            <span>Display name</span>
            <input onChange={(event) => setForm((current) => ({ ...current, displayName: event.target.value }))} value={form.displayName} />
          </label>
          <label className="field">
            <span>Provider</span>
            <select onChange={(event) => setForm((current) => ({ ...current, provider: event.target.value as User["provider"] }))} value={form.provider}>
              <option value="local">local</option>
              <option value="entra">entra</option>
              <option value="oidc">oidc</option>
            </select>
          </label>
          <label className="field">
            <span>Tenant</span>
            <select onChange={(event) => setForm((current) => ({ ...current, tenantId: event.target.value }))} value={form.tenantId}>
              {portal.data.tenants.map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.name}</option>)}
            </select>
          </label>
          <label className="field field--full">
            <span>Group IDs</span>
            <input onChange={(event) => setForm((current) => ({ ...current, groupIds: splitList(event.target.value) }))} value={form.groupIds.join(", ")} />
          </label>
          <label className="field field--full">
            <span>Policy IDs</span>
            <input onChange={(event) => setForm((current) => ({ ...current, policyIds: splitList(event.target.value) }))} value={form.policyIds.join(", ")} />
          </label>
          <label className="checkbox">
            <input checked={form.enabled} onChange={(event) => setForm((current) => ({ ...current, enabled: event.target.checked }))} type="checkbox" />
            <span>Enabled</span>
          </label>
          <label className="checkbox">
            <input checked={form.testAccount} onChange={(event) => setForm((current) => ({ ...current, testAccount: event.target.checked }))} type="checkbox" />
            <span>Test account</span>
          </label>
          {state.error ? <Banner tone="danger">{state.error}</Banner> : null}
          {state.success ? <Banner tone="success">{state.success}</Banner> : null}
          <div className="form-actions">
            <button className="button" disabled={state.submitting} type="submit">
              {state.submitting ? "Saving..." : selectedUser ? "Save user" : "Create user"}
            </button>
            <button className="button button--ghost" onClick={() => { setSelectedUserId(null); setForm(toUserRequest(null, portal.data.tenants)); }} type="button">
              New user
            </button>
          </div>
        </form>
      </Panel>
    </EntityPageLayout>
  );
}

function GroupsPage() {
  const portal = usePortal();
  const groups = deriveGroups(portal.data.users, portal.data.policies);
  const [selectedGroupId, setSelectedGroupId] = useState<string | null>(groups[0]?.id ?? null);
  const selectedGroup = groups.find((group) => group.id === selectedGroupId) ?? null;
  const [form, setForm] = useState<GroupEditorState>(() => toGroupEditorState(selectedGroup));
  const [state, setState] = useState<{ error: string | null; submitting: boolean; success: string | null }>({ error: null, submitting: false, success: null });

  useEffect(() => {
    setForm(toGroupEditorState(selectedGroup));
  }, [selectedGroup]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ error: null, submitting: true, success: null });

    try {
      const groupId = form.id.trim();
      const usersToUpdate = portal.data.users.filter((user) => user.groupIds.includes(groupId) !== form.userIds.includes(user.id));
      const policiesToUpdate = portal.data.policies.filter((policy) => policy.targetGroupIds.includes(groupId) !== form.policyIds.includes(policy.id));

      await Promise.all([
        ...usersToUpdate.map((user) =>
          portal.upsertUser({
            ...toUserRequest(user, portal.data.tenants),
            groupIds: toggleMembership(user.groupIds, groupId, form.userIds.includes(user.id))
          })),
        ...policiesToUpdate.map((policy) =>
          portal.upsertPolicy({
            ...toPolicyRequest(policy, portal.data.tenants),
            targetGroupIds: toggleMembership(policy.targetGroupIds, groupId, form.policyIds.includes(policy.id))
          }))
      ]);

      setSelectedGroupId(groupId);
      setState({ error: null, submitting: false, success: "Group membership updated." });
    } catch (error) {
      setState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  async function handleDeleteGroup(groupId: string) {
    setState({ error: null, submitting: true, success: null });

    try {
      const target = groups.find((group) => group.id === groupId);
      if (!target) {
        return;
      }

      await Promise.all([
        ...target.users.map((user) =>
          portal.upsertUser({
            ...toUserRequest(user, portal.data.tenants),
            groupIds: user.groupIds.filter((entry) => entry !== groupId)
          })),
        ...target.policies.map((policy) =>
          portal.upsertPolicy({
            ...toPolicyRequest(policy, portal.data.tenants),
            targetGroupIds: policy.targetGroupIds.filter((entry) => entry !== groupId)
          }))
      ]);

      setSelectedGroupId(null);
      setState({ error: null, submitting: false, success: "Group removed from all memberships." });
    } catch (error) {
      setState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  return (
    <EntityPageLayout description="Groups are derived from user membership and policy targeting, so the portal manages them by coordinating those live records." title="Groups">
      <Panel>
        <SectionHeading eyebrow="Catalog" title="Derived groups" detail={`${groups.length} groups discovered in users and policies.`} />
        <EntityState emptyMessage="No groups are defined yet." error={portal.dataError} items={groups} state={portal.dataState}>
          <DataTable
            columns={[
              { header: "Group ID", render: (group) => <button className="link-button" onClick={() => setSelectedGroupId(group.id)} type="button">{group.id}</button> },
              { header: "Users", render: (group) => `${group.users.length}` },
              { header: "Policies", render: (group) => `${group.policies.length}` },
              { header: "Members", render: (group) => joinList(group.users.map((user) => user.username)) }
            ]}
            rows={groups}
          />
        </EntityState>
      </Panel>

      <Panel>
        <SectionHeading eyebrow="Editor" title={selectedGroup ? `Edit ${selectedGroup.id}` : "Create group"} detail="This workflow updates users and policies with the same reusable mutation patterns as the rest of the portal." />
        <form className="form-grid" onSubmit={handleSubmit}>
          <label className="field field--full">
            <span>Group ID</span>
            <input onChange={(event) => setForm((current) => ({ ...current, id: event.target.value }))} value={form.id} />
          </label>
          <fieldset className="field-set">
            <legend>Users</legend>
            <div className="selection-grid">
              {portal.data.users.map((user) => (
                <label className="checkbox" key={user.id}>
                  <input checked={form.userIds.includes(user.id)} onChange={() => setForm((current) => ({ ...current, userIds: toggleString(current.userIds, user.id) }))} type="checkbox" />
                  <span>{user.username}</span>
                </label>
              ))}
            </div>
          </fieldset>
          <fieldset className="field-set">
            <legend>Policies</legend>
            <div className="selection-grid">
              {portal.data.policies.map((policy) => (
                <label className="checkbox" key={policy.id}>
                  <input checked={form.policyIds.includes(policy.id)} onChange={() => setForm((current) => ({ ...current, policyIds: toggleString(current.policyIds, policy.id) }))} type="checkbox" />
                  <span>{policy.name}</span>
                </label>
              ))}
            </div>
          </fieldset>
          {state.error ? <Banner tone="danger">{state.error}</Banner> : null}
          {state.success ? <Banner tone="success">{state.success}</Banner> : null}
          <div className="form-actions">
            <button className="button" disabled={state.submitting} type="submit">
              {state.submitting ? "Saving..." : selectedGroup ? "Save group" : "Create group"}
            </button>
            {selectedGroup ? <button className="button button--ghost" onClick={() => void handleDeleteGroup(selectedGroup.id)} type="button">Delete group</button> : null}
            <button className="button button--ghost" onClick={() => { setSelectedGroupId(null); setForm(toGroupEditorState(null)); }} type="button">
              New group
            </button>
          </div>
        </form>
      </Panel>
    </EntityPageLayout>
  );
}

function GatewaysPage() {
  const portal = usePortal();
  const [selectedGatewayId, setSelectedGatewayId] = useState<string | null>(portal.data.gateways[0]?.id ?? null);
  const selectedGateway = portal.data.gateways.find((gateway) => gateway.id === selectedGatewayId) ?? null;
  const [form, setForm] = useState<ReturnType<typeof toGatewayRequest>>(() => toGatewayRequest(selectedGateway, portal.data.tenants));
  const [state, setState] = useState<{ error: string | null; submitting: boolean; success: string | null }>({ error: null, submitting: false, success: null });

  useEffect(() => {
    setForm(toGatewayRequest(selectedGateway, portal.data.tenants));
  }, [selectedGateway, portal.data.tenants]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ error: null, submitting: true, success: null });

    try {
      const updated = await portal.upsertGateway(form);
      setSelectedGatewayId(updated.id);
      setState({ error: null, submitting: false, success: "Gateway record saved." });
    } catch (error) {
      setState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  return (
    <EntityPageLayout description="Gateway CRUD sits next to live health scoring so operators can update inventory with immediate operational context." title="Gateways">
      <Panel>
        <SectionHeading eyebrow="Inventory" title="Gateway records" detail={`Gateway-health stream is ${portal.streams.gatewayHealth.state}.`} />
        <EntityState emptyMessage="No gateways are available." error={portal.dataError} items={portal.data.gateways} state={portal.dataState}>
          <DataTable
            columns={[
              { header: "Name", render: (gateway) => <button className="link-button" onClick={() => setSelectedGatewayId(gateway.id)} type="button">{gateway.name}</button> },
              { header: "Region", render: (gateway) => gateway.region },
              { header: "Health", render: (gateway) => <span className={`status-pill status-pill--${gateway.health}`}>{severityLabel[gateway.health]}</span> },
              { header: "Load", render: (gateway) => `${gateway.loadPercent}%` },
              { header: "CPU", render: (gateway) => `${gateway.cpuPercent}%` },
              { header: "Latency", render: (gateway) => `${gateway.latencyMs} ms` },
              { header: "Actions", render: (gateway) => <button className="button button--ghost" onClick={() => void portal.deleteGateway(gateway.id)} type="button">Delete</button> }
            ]}
            rows={portal.data.gateways}
          />
        </EntityState>
      </Panel>

      <Panel>
        <SectionHeading eyebrow="Editor" title={selectedGateway ? `Edit ${selectedGateway.name}` : "Create gateway"} detail="Shared form pattern with explicit validation and failure handling." />
        <form className="form-grid" onSubmit={handleSubmit}>
          <label className="field"><span>Name</span><input onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} value={form.name} /></label>
          <label className="field"><span>Region</span><input onChange={(event) => setForm((current) => ({ ...current, region: event.target.value }))} value={form.region} /></label>
          <label className="field">
            <span>Health</span>
            <select onChange={(event) => setForm((current) => ({ ...current, health: event.target.value as Gateway["health"] }))} value={form.health}>
              <option value="green">green</option>
              <option value="yellow">yellow</option>
              <option value="red">red</option>
            </select>
          </label>
          <label className="field">
            <span>Tenant</span>
            <select onChange={(event) => setForm((current) => ({ ...current, tenantId: event.target.value }))} value={form.tenantId}>
              {portal.data.tenants.map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.name}</option>)}
            </select>
          </label>
          <label className="field"><span>Load percent</span><input onChange={(event) => setForm((current) => ({ ...current, loadPercent: Number(event.target.value) }))} type="number" value={form.loadPercent} /></label>
          <label className="field"><span>Peer count</span><input onChange={(event) => setForm((current) => ({ ...current, peerCount: Number(event.target.value) }))} type="number" value={form.peerCount} /></label>
          <label className="field"><span>CPU percent</span><input onChange={(event) => setForm((current) => ({ ...current, cpuPercent: Number(event.target.value) }))} type="number" value={form.cpuPercent} /></label>
          <label className="field"><span>Memory percent</span><input onChange={(event) => setForm((current) => ({ ...current, memoryPercent: Number(event.target.value) }))} type="number" value={form.memoryPercent} /></label>
          <label className="field field--full"><span>Latency ms</span><input onChange={(event) => setForm((current) => ({ ...current, latencyMs: Number(event.target.value) }))} type="number" value={form.latencyMs} /></label>
          {state.error ? <Banner tone="danger">{state.error}</Banner> : null}
          {state.success ? <Banner tone="success">{state.success}</Banner> : null}
          <div className="form-actions">
            <button className="button" disabled={state.submitting} type="submit">{state.submitting ? "Saving..." : selectedGateway ? "Save gateway" : "Create gateway"}</button>
            <button className="button button--ghost" onClick={() => { setSelectedGatewayId(null); setForm(toGatewayRequest(null, portal.data.tenants)); }} type="button">New gateway</button>
          </div>
        </form>
      </Panel>
    </EntityPageLayout>
  );
}

function PoliciesPage() {
  const portal = usePortal();
  const [selectedPolicyId, setSelectedPolicyId] = useState<string | null>(portal.data.policies[0]?.id ?? null);
  const selectedPolicy = portal.data.policies.find((policy) => policy.id === selectedPolicyId) ?? null;
  const [form, setForm] = useState<ReturnType<typeof toPolicyRequest>>(() => toPolicyRequest(selectedPolicy, portal.data.tenants));
  const [state, setState] = useState<{ error: string | null; submitting: boolean; success: string | null }>({ error: null, submitting: false, success: null });

  useEffect(() => {
    setForm(toPolicyRequest(selectedPolicy, portal.data.tenants));
  }, [selectedPolicy, portal.data.tenants]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ error: null, submitting: true, success: null });

    try {
      const updated = await portal.upsertPolicy(form);
      setSelectedPolicyId(updated.id);
      setState({ error: null, submitting: false, success: "Policy saved." });
    } catch (error) {
      setState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  return (
    <EntityPageLayout description="Policies reuse the same request-validation and optimistic delete flow as the other operator workflows." title="Policies">
      <Panel>
        <SectionHeading eyebrow="Catalog" title="Policy rules" detail={`${portal.data.policies.length} policies loaded.`} />
        <EntityState emptyMessage="No policies are available." error={portal.dataError} items={portal.data.policies} state={portal.dataState}>
          <DataTable
            columns={[
              { header: "Name", render: (policy) => <button className="link-button" onClick={() => setSelectedPolicyId(policy.id)} type="button">{policy.name}</button> },
              { header: "Priority", render: (policy) => `${policy.priority}` },
              { header: "Groups", render: (policy) => joinList(policy.targetGroupIds) },
              { header: "CIDRs", render: (policy) => joinList(policy.cidrs) },
              { header: "DNS", render: (policy) => joinList(policy.dnsZones) },
              { header: "Actions", render: (policy) => <button className="button button--ghost" onClick={() => void portal.deletePolicy(policy.id)} type="button">Delete</button> }
            ]}
            rows={portal.data.policies}
          />
        </EntityState>
      </Panel>

      <Panel>
        <SectionHeading eyebrow="Editor" title={selectedPolicy ? `Edit ${selectedPolicy.name}` : "Create policy"} detail="CSV fields and device-state toggles use shared parsing helpers." />
        <form className="form-grid" onSubmit={handleSubmit}>
          <label className="field"><span>Name</span><input onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} value={form.name} /></label>
          <label className="field">
            <span>Tenant</span>
            <select onChange={(event) => setForm((current) => ({ ...current, tenantId: event.target.value }))} value={form.tenantId}>
              {portal.data.tenants.map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.name}</option>)}
            </select>
          </label>
          <label className="field field--full"><span>CIDRs</span><input onChange={(event) => setForm((current) => ({ ...current, cidrs: splitList(event.target.value) }))} value={form.cidrs.join(", ")} /></label>
          <label className="field field--full"><span>DNS zones</span><input onChange={(event) => setForm((current) => ({ ...current, dnsZones: splitList(event.target.value) }))} value={form.dnsZones.join(", ")} /></label>
          <label className="field field--full"><span>Ports</span><input onChange={(event) => setForm((current) => ({ ...current, ports: splitNumberList(event.target.value) }))} value={form.ports.join(", ")} /></label>
          <label className="field"><span>Priority</span><input onChange={(event) => setForm((current) => ({ ...current, priority: Number(event.target.value) }))} type="number" value={form.priority} /></label>
          <label className="field"><span>Minimum posture</span><input onChange={(event) => setForm((current) => ({ ...current, minimumPostureScore: Number(event.target.value) }))} type="number" value={form.minimumPostureScore} /></label>
          <label className="field field--full"><span>Target groups</span><input onChange={(event) => setForm((current) => ({ ...current, targetGroupIds: splitList(event.target.value) }))} value={form.targetGroupIds.join(", ")} /></label>
          <label className="checkbox"><input checked={form.requireManaged} onChange={(event) => setForm((current) => ({ ...current, requireManaged: event.target.checked }))} type="checkbox" /><span>Require managed</span></label>
          <label className="checkbox"><input checked={form.requireCompliant} onChange={(event) => setForm((current) => ({ ...current, requireCompliant: event.target.checked }))} type="checkbox" /><span>Require compliant</span></label>
          <fieldset className="field-set field-set--full">
            <legend>Allowed device states</legend>
            <div className="selection-grid">
              {(["Pending", "Enrolled", "Disabled", "Revoked"] as DeviceRegistrationState[]).map((value) => (
                <label className="checkbox" key={value}>
                  <input checked={form.allowedDeviceStates.includes(value)} onChange={() => setForm((current) => ({ ...current, allowedDeviceStates: toggleDeviceState(current.allowedDeviceStates, value) }))} type="checkbox" />
                  <span>{registrationStateLabel[value]}</span>
                </label>
              ))}
            </div>
          </fieldset>
          {state.error ? <Banner tone="danger">{state.error}</Banner> : null}
          {state.success ? <Banner tone="success">{state.success}</Banner> : null}
          <div className="form-actions">
            <button className="button" disabled={state.submitting} type="submit">{state.submitting ? "Saving..." : selectedPolicy ? "Save policy" : "Create policy"}</button>
            <button className="button button--ghost" onClick={() => { setSelectedPolicyId(null); setForm(toPolicyRequest(null, portal.data.tenants)); }} type="button">New policy</button>
          </div>
        </form>
      </Panel>
    </EntityPageLayout>
  );
}

function ProvidersPage() {
  const portal = usePortal();
  const [selectedProviderId, setSelectedProviderId] = useState<string | null>(portal.data.providers[0]?.id ?? null);
  const selectedProvider = portal.data.providers.find((provider) => provider.id === selectedProviderId) ?? null;
  const [form, setForm] = useState<ReturnType<typeof toProviderRequest>>(() => toProviderRequest(selectedProvider, portal.data.tenants));
  const [state, setState] = useState<{ error: string | null; submitting: boolean; success: string | null }>({ error: null, submitting: false, success: null });

  useEffect(() => {
    setForm(toProviderRequest(selectedProvider, portal.data.tenants));
  }, [selectedProvider, portal.data.tenants]);

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setState({ error: null, submitting: true, success: null });

    try {
      const updated = await portal.upsertAuthProvider(form);
      setSelectedProviderId(updated.id);
      setState({ error: null, submitting: false, success: "Auth provider saved." });
    } catch (error) {
      setState({ error: toErrorMessage(error), submitting: false, success: null });
    }
  }

  return (
    <EntityPageLayout description="Auth providers now have real operator CRUD endpoints, so this page manages live configuration instead of a static list." title="Auth Providers">
      <Panel>
        <SectionHeading eyebrow="Providers" title="OIDC and Entra configuration" detail={`${portal.data.providers.length} providers loaded.`} />
        <EntityState emptyMessage="No auth providers are configured." error={portal.dataError} items={portal.data.providers} state={portal.dataState}>
          <DataTable
            columns={[
              { header: "Name", render: (provider) => <button className="link-button" onClick={() => setSelectedProviderId(provider.id)} type="button">{provider.name}</button> },
              { header: "Type", render: (provider) => provider.type },
              { header: "Issuer", render: (provider) => provider.issuer },
              { header: "Client ID", render: (provider) => provider.clientId },
              { header: "Claims", render: (provider) => joinList(provider.usernameClaimPaths) },
              { header: "Actions", render: (provider) => <button className="button button--ghost" onClick={() => void portal.deleteAuthProvider(provider.id)} type="button">Delete</button> }
            ]}
            rows={portal.data.providers}
          />
        </EntityState>
      </Panel>

      <Panel>
        <SectionHeading eyebrow="Editor" title={selectedProvider ? `Edit ${selectedProvider.name}` : "Create provider"} detail="Shared form pattern with claim-path editing and explicit validation errors." />
        <form className="form-grid" onSubmit={handleSubmit}>
          <label className="field"><span>Name</span><input onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} value={form.name} /></label>
          <label className="field">
            <span>Type</span>
            <select onChange={(event) => setForm((current) => ({ ...current, type: event.target.value as AuthProviderConfig["type"] }))} value={form.type}>
              <option value="entra">entra</option>
              <option value="oidc">oidc</option>
            </select>
          </label>
          <label className="field field--full"><span>Issuer</span><input onChange={(event) => setForm((current) => ({ ...current, issuer: event.target.value }))} value={form.issuer} /></label>
          <label className="field field--full"><span>Client ID</span><input onChange={(event) => setForm((current) => ({ ...current, clientId: event.target.value }))} value={form.clientId} /></label>
          <label className="field field--full"><span>Username claim paths</span><input onChange={(event) => setForm((current) => ({ ...current, usernameClaimPaths: splitList(event.target.value) }))} value={form.usernameClaimPaths.join(", ")} /></label>
          <label className="field field--full"><span>Group claim paths</span><input onChange={(event) => setForm((current) => ({ ...current, groupClaimPaths: splitList(event.target.value) }))} value={form.groupClaimPaths.join(", ")} /></label>
          <label className="field field--full"><span>MFA claim paths</span><input onChange={(event) => setForm((current) => ({ ...current, mfaClaimPaths: splitList(event.target.value) }))} value={form.mfaClaimPaths.join(", ")} /></label>
          <label className="field">
            <span>Tenant</span>
            <select onChange={(event) => setForm((current) => ({ ...current, tenantId: event.target.value }))} value={form.tenantId}>
              {portal.data.tenants.map((tenant) => <option key={tenant.id} value={tenant.id}>{tenant.name}</option>)}
            </select>
          </label>
          <label className="checkbox"><input checked={form.requireMfa} onChange={(event) => setForm((current) => ({ ...current, requireMfa: event.target.checked }))} type="checkbox" /><span>Require MFA</span></label>
          <label className="checkbox"><input checked={form.silentSsoEnabled} onChange={(event) => setForm((current) => ({ ...current, silentSsoEnabled: event.target.checked }))} type="checkbox" /><span>Enable silent SSO</span></label>
          {state.error ? <Banner tone="danger">{state.error}</Banner> : null}
          {state.success ? <Banner tone="success">{state.success}</Banner> : null}
          <div className="form-actions">
            <button className="button" disabled={state.submitting} type="submit">{state.submitting ? "Saving..." : selectedProvider ? "Save provider" : "Create provider"}</button>
            <button className="button button--ghost" onClick={() => { setSelectedProviderId(null); setForm(toProviderRequest(null, portal.data.tenants)); }} type="button">New provider</button>
          </div>
        </form>
      </Panel>
    </EntityPageLayout>
  );
}

function AlertsPage() {
  const portal = usePortal();

  return (
    <div className="page-stack">
      <PageHero
        eyebrow="Alerts"
        title="Incident review is directly bound to the live alert stream."
        subtitle="This page stays on the real alert dataset and exposes reconnect state in the same operator shell."
        badges={[
          `Alert stream ${portal.streams.alerts.state}`,
          `${portal.data.alerts.length} alert records`,
          `${portal.data.alerts.filter((alert) => alert.severity === "red").length} critical`
        ]}
      />

      <Panel>
        <SectionHeading eyebrow="Feed" title="All alerts" detail="Operator incident queue ordered by control-plane timestamps." />
        <EntityState emptyMessage="No alerts are active." error={portal.dataError} items={portal.data.alerts} state={portal.dataState}>
          <div className="stack-list">
            {portal.data.alerts.map((alert) => (
              <AlertCard alert={alert} key={alert.id} />
            ))}
          </div>
        </EntityState>
      </Panel>
    </div>
  );
}

function AuditPage() {
  const portal = usePortal();
  const [preview, setPreview] = useState<AuditExportResponse | null>(null);
  const [state, setState] = useState<{ error: string | null; loadingPreview: boolean; runningRetention: boolean }>({ error: null, loadingPreview: false, runningRetention: false });
  const [search, setSearch] = useState("");
  const deferredSearch = useDeferredValue(search);
  const filteredEvents = portal.data.auditEvents.filter((event) =>
    `${event.actor} ${event.action} ${event.targetType} ${event.targetId} ${event.detail}`.toLowerCase().includes(deferredSearch.toLowerCase())
  );

  async function handlePreviewExport() {
    setState((current) => ({ ...current, error: null, loadingPreview: true }));

    try {
      const exported = await portal.exportAuditEvents(null, 50);
      setPreview(exported);
      setState((current) => ({ ...current, loadingPreview: false }));
    } catch (error) {
      setState((current) => ({ ...current, error: toErrorMessage(error), loadingPreview: false }));
    }
  }

  async function handleRunRetention() {
    setState((current) => ({ ...current, error: null, runningRetention: true }));

    try {
      await portal.runAuditRetention();
      setState((current) => ({ ...current, runningRetention: false }));
    } catch (error) {
      setState((current) => ({ ...current, error: toErrorMessage(error), runningRetention: false }));
    }
  }

  return (
    <div className="page-stack">
      <PageHero
        eyebrow="Audit"
        title="Audit review, export preview, and retention controls share one routed workflow."
        subtitle="The portal uses live audit endpoints and keeps retention execution behind the same step-up guardrail as other privileged actions."
        badges={[
          `${portal.data.auditEvents.length} events`,
          `${portal.data.auditCheckpoints.length} checkpoints`,
          portal.hasActiveStepUp ? "Step-up active" : "Step-up required for retention"
        ]}
      />

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Ledger" title="Audit events" detail="Append-only event history from the control plane." />
          <label className="field field--compact">
            <span>Search</span>
            <input onChange={(event) => setSearch(event.target.value)} placeholder="Filter actor, action, target, or detail" value={search} />
          </label>
          <EntityState emptyMessage="No audit events are available." error={portal.dataError} items={filteredEvents} state={portal.dataState}>
            <DataTable
              columns={[
                { header: "Seq", render: (event) => `${event.sequence}` },
                { header: "Actor", render: (event) => event.actor },
                { header: "Action", render: (event) => event.action },
                { header: "Outcome", render: (event) => <span className={event.outcome === "success" ? "status-pill status-pill--green" : "status-pill status-pill--red"}>{event.outcome}</span> },
                { header: "Target", render: (event) => `${event.targetType}:${event.targetId}` },
                { header: "When", render: (event) => formatDateTime(event.createdAtUtc) }
              ]}
              rows={filteredEvents}
            />
          </EntityState>
        </Panel>

        <Panel>
          <SectionHeading eyebrow="Operations" title="Retention and export" detail="Retention runs and export previews use live endpoints." />
          {state.error ? <Banner tone="danger">{state.error}</Banner> : null}
          <div className="form-actions form-actions--vertical">
            <button className="button" disabled={state.loadingPreview} onClick={() => void handlePreviewExport()} type="button">
              {state.loadingPreview ? "Loading preview..." : "Preview export"}
            </button>
            <button className="button button--ghost" disabled={state.runningRetention} onClick={() => void handleRunRetention()} type="button">
              {state.runningRetention ? "Running retention..." : "Run retention"}
            </button>
          </div>
          <div className="stack-list">
            {preview ? <FeatureCard body={`${preview.eventCount} events prepared at ${formatDateTime(preview.generatedAtUtc)}.`} title="Latest export preview" /> : null}
            {portal.data.auditCheckpoints.map((checkpoint) => (
              <FeatureCard body={`Removed through sequence ${checkpoint.removedThroughSequence} and exported ${checkpoint.exportedEventCount} events.`} key={checkpoint.id} title={formatDateTime(checkpoint.exportedAtUtc)} />
            ))}
          </div>
        </Panel>
      </section>
    </div>
  );
}

function SettingsPage() {
  const portal = usePortal();

  return (
    <div className="page-stack">
      <PageHero
        eyebrow="Settings"
        title="Session, tenant, stream, and bootstrap posture are visible in one operator view."
        subtitle="This page concentrates the portal’s shared client state instead of burying those details inside individual screens."
        badges={[
          portal.authSession?.admin.role ?? "No role",
          portal.bootstrapStatus?.testUserEnabled ? "Test user enabled" : "Test user disabled",
          portal.hasActiveStepUp ? "Step-up active" : "Step-up idle"
        ]}
      />

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Session" title="Authenticated admin session" detail="Stored locally and refreshed through the shared API client." />
          <div className="metric-grid">
            <Metric label="Subject" value={portal.authSession?.session.subjectName ?? "Unknown"} />
            <Metric label="Created" value={portal.authSession ? formatDateTime(portal.authSession.session.createdAtUtc) : "Unknown"} />
            <Metric label="Access expires" value={portal.authSession ? formatDateTime(portal.authSession.tokens.accessTokenExpiresAtUtc) : "Unknown"} />
            <Metric label="Step-up" value={portal.authSession?.session.stepUpExpiresAtUtc ? formatDateTime(portal.authSession.session.stepUpExpiresAtUtc) : "Not active"} />
          </div>
        </Panel>

        <Panel>
          <SectionHeading eyebrow="Tenants" title="Tenant catalog" detail="Read from the tenant endpoint and reused by every management form." />
          <div className="stack-list">
            {portal.data.tenants.map((tenant) => (
              <FeatureCard body={`Region ${tenant.region}${tenant.isDefault ? " · default" : ""}`} key={tenant.id} title={tenant.name} />
            ))}
          </div>
        </Panel>
      </section>

      <section className="panel-grid">
        <Panel>
          <SectionHeading eyebrow="Admins" title="Admin accounts" detail="Control-plane admin inventory." />
          <EntityState emptyMessage="No admins are available." error={portal.dataError} items={portal.data.admins} state={portal.dataState}>
            <DataTable
              columns={[
                { header: "Username", render: (admin) => admin.username },
                { header: "Role", render: (admin) => admin.role },
                { header: "Password", render: (admin) => admin.mustChangePassword ? "Rotation required" : "Rotated" },
                { header: "MFA", render: (admin) => admin.mfaEnrolled ? "Enrolled" : "Pending" }
              ]}
              rows={portal.data.admins}
            />
          </EntityState>
        </Panel>

        <Panel>
          <SectionHeading eyebrow="Streams" title="Realtime status" detail="WebSocket reconnect state from the shared client layer." />
          <div className="stack-list">
            {Object.entries(portal.streams).map(([key, stream]) => (
              <FeatureCard body={`State ${stream.state}${stream.lastEventAtUtc ? ` · last event ${formatDateTime(stream.lastEventAtUtc)}` : ""}${stream.error ? ` · ${stream.error}` : ""}`} key={key} title={key} />
            ))}
          </div>
        </Panel>
      </section>
    </div>
  );
}

function StepUpModal() {
  const portal = usePortal();
  const [password, setPassword] = useState("");

  useEffect(() => {
    if (!portal.stepUpPrompt.open) {
      setPassword("");
    }
  }, [portal.stepUpPrompt.open]);

  if (!portal.stepUpPrompt.open) {
    return null;
  }

  return (
    <div className="modal-backdrop">
      <div className="modal">
        <div className="panel__header">
          <div>
            <p className="eyebrow">Step-Up Required</p>
            <h2>{portal.stepUpPrompt.operationName}</h2>
          </div>
        </div>
        <p className="muted">This action targets a privileged control-plane endpoint and needs a current admin step-up.</p>
        <label className="field">
          <span>Password</span>
          <input onChange={(event) => setPassword(event.target.value)} type="password" value={password} />
        </label>
        {portal.stepUpPrompt.error ? <Banner tone="danger">{portal.stepUpPrompt.error}</Banner> : null}
        <div className="form-actions">
          <button className="button" disabled={portal.stepUpPrompt.submitting} onClick={() => void portal.submitStepUp(password)} type="button">
            {portal.stepUpPrompt.submitting ? "Verifying..." : "Confirm"}
          </button>
          <button className="button button--ghost" onClick={portal.cancelStepUp} type="button">
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}

function PageHero({ badges, eyebrow, subtitle, title }: { badges: string[]; eyebrow: string; subtitle: string; title: string }) {
  return (
    <section className="hero">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
        <p className="muted">{subtitle}</p>
      </div>
      <div className="hero__badges">
        {badges.map((badge) => <span className="tag" key={badge}>{badge}</span>)}
      </div>
    </section>
  );
}

function EntityPageLayout({ children, description, title }: { children: ReactNode; description: string; title: string }) {
  return (
    <div className="page-stack">
      <PageHero eyebrow={title} title={`${title} workflows`} subtitle={description} badges={["Loading, error, empty, and optimistic states are shared"]} />
      <section className="panel-grid panel-grid--management">{children}</section>
    </div>
  );
}

function Panel({ children }: { children: ReactNode }) {
  return <section className="panel">{children}</section>;
}

function SectionHeading({ detail, eyebrow, title }: { detail: string; eyebrow: string; title: string }) {
  return (
    <div className="panel__header">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h2>{title}</h2>
      </div>
      <span className="muted">{detail}</span>
    </div>
  );
}

function EntityState<TItem>({ children, emptyMessage, error, items, state }: { children: ReactNode; emptyMessage: string; error: string | null; items: TItem[]; state: "idle" | "loading" | "refreshing" | "ready" | "error" }) {
  if ((state === "loading" || state === "idle") && items.length === 0) {
    return <Banner tone="neutral">Loading data from the control plane...</Banner>;
  }

  if (state === "error" && items.length === 0) {
    return <Banner tone="danger">{error ?? "The portal could not load this dataset."}</Banner>;
  }

  if (items.length === 0) {
    return <Banner tone="neutral">{emptyMessage}</Banner>;
  }

  return (
    <>
      {state === "refreshing" ? <Banner tone="neutral">Refreshing live data...</Banner> : null}
      {children}
    </>
  );
}

function DataTable<TItem>({ columns, rows }: { columns: Array<{ header: string; render: (row: TItem) => ReactNode }>; rows: TItem[] }) {
  return (
    <div className="table-wrap">
      <table className="table">
        <thead>
          <tr>
            {columns.map((column) => <th key={column.header}>{column.header}</th>)}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, index) => (
            <tr key={index}>
              {columns.map((column) => <td key={column.header}>{column.render(row)}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function FeatureCard({ body, title }: { body: string; title: string }) {
  return (
    <article className="feature-card">
      <h3>{title}</h3>
      <p className="muted">{body}</p>
    </article>
  );
}

function StatCard({ caption, label, value }: { caption: string; label: string; value: string }) {
  return (
    <article className="stat-card">
      <span className="stat-card__label">{label}</span>
      <strong>{value}</strong>
      <p>{caption}</p>
    </article>
  );
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value}</dd>
    </div>
  );
}

function Banner({ children, tone }: { children: ReactNode; tone: "danger" | "neutral" | "success" }) {
  return <div className={`banner banner--${tone}`}>{children}</div>;
}

function StatusChip({ label, status }: { label: string; status: string }) {
  return <span className="tag">{label} {status}</span>;
}

function GatewayPoolCard({ pool }: { pool: GatewayPoolStatus }) {
  const primary = pool.gateways.find((gateway) => gateway.gatewayId === pool.primaryGatewayId) ?? null;

  return (
    <article className="feature-card">
      <div className="panel__header">
        <div>
          <h3>{pool.name}</h3>
          <p className="muted">Primary {primary?.gatewayName ?? "No healthy gateway"}</p>
        </div>
        <span className={`status-pill status-pill--${pool.health}`}>{pool.score}</span>
      </div>
      <div className="metric-grid">
        <Metric label="Regions" value={joinList(pool.regions)} />
        <Metric label="Failovers" value={`${pool.failoverGatewayIds.length}`} />
        <Metric label="Members" value={`${pool.gateways.length}`} />
        <Metric label="Health" value={severityLabel[pool.health]} />
      </div>
    </article>
  );
}

function ConnectionMapCard({ city }: { city: ConnectionMapCityAggregate }) {
  return (
    <article className="feature-card">
      <div className="panel__header">
        <div>
          <h3>{city.city}</h3>
          <p className="muted">{city.country}</p>
        </div>
        <span className="tag">{city.deviceCount} devices</span>
      </div>
      <div className="metric-grid">
        <Metric label="Healthy" value={`${city.healthyCount}`} />
        <Metric label="Impacted" value={`${city.impactedCount}`} />
        <Metric label="Blocked" value={`${city.blockedCount}`} />
        <Metric label="Gateways" value={joinList(city.gatewayIds)} />
      </div>
    </article>
  );
}

function DiagnosticCard({ diagnostic }: { diagnostic: DeviceDiagnostics }) {
  return (
    <article className="feature-card">
      <div className="panel__header">
        <div>
          <h3>{diagnostic.deviceName}</h3>
          <p className="muted">{scopeLabel[diagnostic.scope]}</p>
        </div>
        <span className={`status-pill status-pill--${diagnostic.severity}`}>{severityLabel[diagnostic.severity]}</span>
      </div>
      <p>{diagnostic.summary}</p>
      <p className="muted">{diagnostic.detail}</p>
      <div className="tag-row">
        {diagnostic.signals.map((signal) => <span className="tag tag--subtle" key={signal}>{signal}</span>)}
      </div>
    </article>
  );
}

function AlertCard({ alert }: { alert: Alert }) {
  return (
    <article className="feature-card">
      <div className="panel__header">
        <div>
          <h3>{alert.title}</h3>
          <p className="muted">{alert.description}</p>
        </div>
        <span className={`status-pill status-pill--${alert.severity}`}>{severityLabel[alert.severity]}</span>
      </div>
      <div className="feature-card__footer">
        <span>{alert.targetType}:{alert.targetId}</span>
        <span>{formatDateTime(alert.createdAtUtc)}</span>
      </div>
    </article>
  );
}

function AuditEventCard({ event }: { event: AuditEvent }) {
  return (
    <article className="feature-card">
      <div className="panel__header">
        <div>
          <h3>{event.action}</h3>
          <p className="muted">{event.actor} · {event.targetType}:{event.targetId}</p>
        </div>
        <span className={event.outcome === "success" ? "status-pill status-pill--green" : "status-pill status-pill--red"}>{event.outcome}</span>
      </div>
      <p className="muted">{event.detail}</p>
      <div className="feature-card__footer">
        <span>Seq {event.sequence}</span>
        <span>{formatDateTime(event.createdAtUtc)}</span>
      </div>
    </article>
  );
}

function BootstrapSummary({ bootstrapStatus, error }: { bootstrapStatus: BootstrapStatus | null; error: string | null }) {
  if (error) {
    return <Banner tone="danger">{error}</Banner>;
  }

  if (!bootstrapStatus) {
    return <Banner tone="neutral">Loading bootstrap status...</Banner>;
  }

  return (
    <div className="stack-list">
      <FeatureCard body={bootstrapStatus.requiresPasswordChange ? "The control plane still expects the seeded admin password to rotate." : "Password rotation is already complete."} title="Password rotation" />
      <FeatureCard body={bootstrapStatus.requiresMfaEnrollment ? "MFA must be enrolled before privileged routes unlock." : "MFA enrollment is complete."} title="MFA enrollment" />
      <FeatureCard body={bootstrapStatus.testUserAutoDisableAtUtc ? `Auto-disable scheduled for ${formatDateTime(bootstrapStatus.testUserAutoDisableAtUtc)}.` : "No test-user auto-disable timer is active."} title={bootstrapStatus.testUserEnabled ? "Test user enabled" : "Test user disabled"} />
    </div>
  );
}

function BootstrapChecklist({ bootstrapStatus }: { bootstrapStatus: BootstrapStatus | null }) {
  const items = [
    bootstrapStatus?.requiresPasswordChange ? "Rotate the default bootstrap password." : "Password rotation is complete.",
    bootstrapStatus?.requiresMfaEnrollment ? "Enroll MFA for the bootstrap admin." : "MFA enrollment is complete.",
    bootstrapStatus?.testUserEnabled ? "The seeded test user is enabled and subject to auto-disable." : "The seeded test user is disabled."
  ];

  return (
    <div className="stack-list">
      {items.map((item) => <FeatureCard body={item} key={item} title="Checklist item" />)}
    </div>
  );
}

interface DerivedGroup {
  id: string;
  policies: PolicyRule[];
  users: User[];
}

interface GroupEditorState {
  id: string;
  policyIds: string[];
  userIds: string[];
}

function deriveGroups(users: User[], policies: PolicyRule[]): DerivedGroup[] {
  const groups = new Map<string, DerivedGroup>();

  users.forEach((user) => {
    user.groupIds.forEach((groupId) => {
      const current = groups.get(groupId) ?? { id: groupId, policies: [], users: [] };
      current.users.push(user);
      groups.set(groupId, current);
    });
  });

  policies.forEach((policy) => {
    policy.targetGroupIds.forEach((groupId) => {
      const current = groups.get(groupId) ?? { id: groupId, policies: [], users: [] };
      current.policies.push(policy);
      groups.set(groupId, current);
    });
  });

  return Array.from(groups.values()).sort((left, right) => left.id.localeCompare(right.id));
}

function toGroupEditorState(group: DerivedGroup | null): GroupEditorState {
  return group ? {
    id: group.id,
    policyIds: group.policies.map((policy) => policy.id),
    userIds: group.users.map((user) => user.id)
  } : {
    id: "",
    policyIds: [],
    userIds: []
  };
}

function toUserRequest(user: User | null, tenants: Tenant[]): UserUpsertRequest {
  return user ? {
    displayName: user.displayName,
    enabled: user.enabled,
    groupIds: [...user.groupIds],
    id: user.id,
    policyIds: [...user.policyIds],
    provider: user.provider,
    tenantId: user.tenantId,
    testAccount: user.testAccount,
    username: user.username
  } : {
    displayName: "",
    enabled: true,
    groupIds: [],
    id: null,
    policyIds: [],
    provider: "local" as const,
    tenantId: tenants[0]?.id ?? "tenant-default",
    testAccount: false,
    username: ""
  };
}

function toGatewayRequest(gateway: Gateway | null, tenants: Tenant[]): GatewayUpsertRequest {
  return gateway ? {
    cpuPercent: gateway.cpuPercent,
    health: gateway.health,
    id: gateway.id,
    latencyMs: gateway.latencyMs,
    loadPercent: gateway.loadPercent,
    memoryPercent: gateway.memoryPercent,
    name: gateway.name,
    peerCount: gateway.peerCount,
    region: gateway.region,
    tenantId: gateway.tenantId
  } : {
    cpuPercent: 20,
    health: "green" as const,
    id: null,
    latencyMs: 20,
    loadPercent: 10,
    memoryPercent: 20,
    name: "",
    peerCount: 0,
    region: "",
    tenantId: tenants[0]?.id ?? "tenant-default"
  };
}

function toPolicyRequest(policy: PolicyRule | null, tenants: Tenant[]): PolicyUpsertRequest {
  return policy ? {
    allowedDeviceStates: [...policy.allowedDeviceStates],
    cidrs: [...policy.cidrs],
    dnsZones: [...policy.dnsZones],
    id: policy.id,
    minimumPostureScore: policy.minimumPostureScore,
    mode: policy.mode,
    name: policy.name,
    ports: [...policy.ports],
    priority: policy.priority,
    requireCompliant: policy.requireCompliant,
    requireManaged: policy.requireManaged,
    targetGroupIds: [...policy.targetGroupIds],
    tenantId: policy.tenantId
  } : {
    allowedDeviceStates: ["Enrolled"] as DeviceRegistrationState[],
    cidrs: [],
    dnsZones: [],
    id: null,
    minimumPostureScore: 80,
    mode: "split-tunnel" as const,
    name: "",
    ports: [],
    priority: 100,
    requireCompliant: true,
    requireManaged: true,
    targetGroupIds: [],
    tenantId: tenants[0]?.id ?? "tenant-default"
  };
}

function toProviderRequest(provider: AuthProviderConfig | null, tenants: Tenant[]): AuthProviderUpsertRequest {
  return provider ? {
    clientId: provider.clientId,
    groupClaimPaths: [...provider.groupClaimPaths],
    id: provider.id,
    issuer: provider.issuer,
    mfaClaimPaths: [...provider.mfaClaimPaths],
    name: provider.name,
    requireMfa: provider.requireMfa,
    silentSsoEnabled: provider.silentSsoEnabled,
    tenantId: provider.tenantId,
    type: provider.type,
    usernameClaimPaths: [...provider.usernameClaimPaths]
  } : {
    clientId: "",
    groupClaimPaths: ["groups"],
    id: null,
    issuer: "",
    mfaClaimPaths: ["amr"],
    name: "",
    requireMfa: true,
    silentSsoEnabled: false,
    tenantId: tenants[0]?.id ?? "tenant-default",
    type: "oidc" as const,
    usernameClaimPaths: ["preferred_username", "email", "sub"]
  };
}

function findUserLabel(users: User[], userId: string) {
  return users.find((user) => user.id === userId)?.username ?? userId;
}

function findDeviceLabel(devices: Array<{ id: string; name: string }>, deviceId: string) {
  return devices.find((device) => device.id === deviceId)?.name ?? deviceId;
}

function findGatewayLabel(gateways: Gateway[], gatewayId: string) {
  return gateways.find((gateway) => gateway.id === gatewayId)?.name ?? gatewayId;
}

function toggleString(values: string[], value: string) {
  return values.includes(value) ? values.filter((entry) => entry !== value) : [...values, value];
}

function toggleMembership(values: string[], value: string, include: boolean) {
  return include
    ? sortStrings(values.includes(value) ? values : [...values, value])
    : values.filter((entry) => entry !== value);
}

function toggleDeviceState(values: DeviceRegistrationState[], value: DeviceRegistrationState) {
  return values.includes(value) ? values.filter((entry) => entry !== value) : [...values, value];
}

function splitList(value: string) {
  return sortStrings(value.split(",").map((item) => item.trim()).filter(Boolean));
}

function splitNumberList(value: string) {
  return value
    .split(",")
    .map((item) => Number(item.trim()))
    .filter((item) => Number.isFinite(item) && item > 0);
}

function sortStrings(values: string[]) {
  return [...new Set(values)].sort((left, right) => left.localeCompare(right));
}

function renderFetchState(state: string) {
  return state === "ready" ? "live" : state;
}

function joinList(values: string[]) {
  return values.length > 0 ? values.join(", ") : "None";
}

function formatDateTime(value: string) {
  return new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" }).format(new Date(value));
}

function toErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : "Request failed.";
}
