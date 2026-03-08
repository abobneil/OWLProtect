export type AdminRole = "SuperAdmin" | "Operator" | "ReadOnly";
export type PlatformSessionKind = "Admin" | "User" | "Client";
export type MachineTrustSubjectKind = "Gateway" | "Device";
export type DeviceRegistrationState = "Pending" | "Enrolled" | "Disabled" | "Revoked";
export type DeviceEnrollmentKind = "Bootstrap" | "ReEnrollment" | "Recovery" | "Reconciliation";
export const CONTROL_PLANE_API_VERSION = "v1";
export const CONTROL_PLANE_API_PREFIX = `/api/${CONTROL_PLANE_API_VERSION}`;
export const CONTROL_PLANE_SOCKET_PREFIX = `${CONTROL_PLANE_API_PREFIX}/ws`;
export const CONTROL_PLANE_STREAM_PATHS = {
  alerts: `${CONTROL_PLANE_SOCKET_PREFIX}/alert-stream`,
  gatewayHealth: `${CONTROL_PLANE_SOCKET_PREFIX}/gateway-health`,
  telemetry: `${CONTROL_PLANE_SOCKET_PREFIX}/client-health`,
  sessions: `${CONTROL_PLANE_SOCKET_PREFIX}/client-session`
} as const;

export type ControlPlaneStreamTopic = "alerts" | "gateway-health" | "telemetry" | "sessions";
export type ControlPlaneStreamKind = "snapshot" | "event" | "keepalive";

export interface ControlPlaneStreamFrame<TPayload = unknown> {
  kind: ControlPlaneStreamKind;
  topic: ControlPlaneStreamTopic;
  sequence: number;
  occurredAtUtc: string;
  eventType: string | null;
  entityId: string | null;
  payload: TPayload | null;
}

export type HealthSeverity = "green" | "yellow" | "red";
export type DiagnosticScope = "Healthy" | "LocalNetwork" | "Gateway" | "ServerSide" | "Policy" | "Authentication";

export type ConnectionState =
  | "Healthy"
  | "LocalNetworkPoor"
  | "LowBandwidth"
  | "HighJitter"
  | "GatewayDegraded"
  | "ServerUnavailable"
  | "AuthExpired"
  | "PolicyBlocked";

export interface User {
  id: string;
  username: string;
  displayName: string;
  enabled: boolean;
  testAccount: boolean;
  provider: "local" | "entra" | "oidc";
  groupIds: string[];
  policyIds: string[];
  tenantId: string;
}

export interface AdminAccount {
  id: string;
  username: string;
  role: AdminRole;
  mustChangePassword: boolean;
  mfaEnrolled: boolean;
}

export interface Device {
  id: string;
  name: string;
  userId: string;
  city: string;
  country: string;
  publicIp: string;
  managed: boolean;
  compliant: boolean;
  postureScore: number;
  connectionState: ConnectionState;
  lastSeenUtc: string;
  tenantId: string;
  registrationState: DeviceRegistrationState;
  enrollmentKind: DeviceEnrollmentKind;
  hardwareKey: string;
  serialNumber: string;
  operatingSystem: string;
  registeredAtUtc: string | null;
  lastEnrollmentAtUtc: string | null;
  disabledAtUtc: string | null;
  complianceReasons: string[];
}

export interface Gateway {
  id: string;
  name: string;
  region: string;
  health: HealthSeverity;
  loadPercent: number;
  peerCount: number;
  cpuPercent: number;
  memoryPercent: number;
  latencyMs: number;
  tenantId: string;
  lastHeartbeatUtc: string | null;
}

export interface GatewayPool {
  id: string;
  name: string;
  regions: string[];
  gatewayIds: string[];
  tenantId: string;
}

export interface PolicyRule {
  id: string;
  name: string;
  cidrs: string[];
  dnsZones: string[];
  ports: number[];
  mode: "split-tunnel";
  tenantId: string;
  priority: number;
  targetGroupIds: string[];
  requireManaged: boolean;
  requireCompliant: boolean;
  minimumPostureScore: number;
  allowedDeviceStates: DeviceRegistrationState[];
}

export interface TunnelSession {
  id: string;
  userId: string;
  deviceId: string;
  gatewayId: string;
  connectedAtUtc: string;
  handshakeAgeSeconds: number;
  throughputMbps: number;
  tenantId: string;
  policyBundleVersion: string;
  authorizedAtUtc: string | null;
  revalidateAfterUtc: string | null;
}

export interface HealthSample {
  id: string;
  deviceId: string;
  state: ConnectionState;
  severity: HealthSeverity;
  latencyMs: number;
  jitterMs: number;
  packetLossPercent: number;
  throughputMbps: number;
  signalStrengthPercent: number;
  dnsReachable: boolean;
  routeHealthy: boolean;
  sampledAtUtc: string;
  message: string;
  tenantId: string;
}

export interface GatewayScore {
  gatewayId: string;
  gatewayName: string;
  region: string;
  health: HealthSeverity;
  score: number;
  available: boolean;
  loadPercent: number;
  latencyMs: number;
  cpuPercent: number;
  memoryPercent: number;
  peerCount: number;
  lastHeartbeatUtc: string | null;
  signals: string[];
  tenantId: string;
}

export interface GatewayPoolStatus {
  poolId: string;
  name: string;
  regions: string[];
  health: HealthSeverity;
  score: number;
  primaryGatewayId: string | null;
  failoverGatewayIds: string[];
  gateways: GatewayScore[];
  tenantId: string;
}

export interface GatewayPlacement {
  gatewayId: string;
  gatewayName: string;
  gatewayPoolId: string;
  gatewayPoolName: string;
  score: number;
  failoverGatewayIds: string[];
  summary: string;
  tenantId: string;
}

export interface DeviceDiagnostics {
  deviceId: string;
  deviceName: string;
  state: ConnectionState;
  scope: DiagnosticScope;
  severity: HealthSeverity;
  summary: string;
  detail: string;
  gatewayId: string | null;
  gatewayName: string | null;
  observedAtUtc: string;
  signals: string[];
  tenantId: string;
}

export interface ConnectionMapCityAggregate {
  city: string;
  country: string;
  deviceCount: number;
  healthyCount: number;
  impactedCount: number;
  blockedCount: number;
  gatewayIds: string[];
  tenantId: string;
}

export interface Alert {
  id: string;
  severity: HealthSeverity;
  title: string;
  description: string;
  targetType: "device" | "gateway" | "policy" | "auth";
  targetId: string;
  createdAtUtc: string;
  tenantId: string;
}

export interface PostureReport {
  deviceId: string;
  managed: boolean;
  compliant: boolean;
  bitLockerEnabled: boolean;
  defenderHealthy: boolean;
  firewallEnabled: boolean;
  secureBootEnabled: boolean;
  tamperProtectionEnabled: boolean;
  osVersion: string;
  tenantId: string;
  schemaVersion: number;
  collectedAtUtc: string | null;
}

export interface AuthProviderConfig {
  id: string;
  name: string;
  type: "entra" | "oidc";
  issuer: string;
  clientId: string;
  usernameClaimPaths: string[];
  groupClaimPaths: string[];
  mfaClaimPaths: string[];
  requireMfa: boolean;
  silentSsoEnabled: boolean;
  tenantId: string;
}

export interface AuditEvent {
  id: string;
  sequence: number;
  actor: string;
  action: string;
  targetType: string;
  targetId: string;
  createdAtUtc: string;
  outcome: "success" | "failure";
  detail: string;
  previousEventHash: string | null;
  eventHash: string;
  tenantId: string;
}

export interface PolicyResolutionResult {
  tenantId: string;
  userId: string;
  deviceId: string;
  effectiveGroups: string[];
  policyIds: string[];
  decisionLog: string[];
}

export interface ResolvedPolicyBundle {
  tenantId: string;
  userId: string;
  deviceId: string;
  version: string;
  generatedAtUtc: string;
  policyIds: string[];
  cidrs: string[];
  dnsZones: string[];
  ports: number[];
}

export interface AuditRetentionCheckpoint {
  id: string;
  cutoffUtc: string;
  exportedAtUtc: string;
  exportPath: string;
  removedThroughSequence: number;
  removedThroughCreatedAtUtc: string;
  removedThroughEventHash: string;
  exportedEventCount: number;
}

export interface AuditExportResponse {
  generatedAtUtc: string;
  cutoffUtc: string | null;
  eventCount: number;
  events: AuditEvent[];
}

export interface PlatformSession {
  id: string;
  kind: PlatformSessionKind;
  subjectId: string;
  subjectName: string;
  role: AdminRole | null;
  createdAtUtc: string;
  accessTokenExpiresAtUtc: string;
  refreshTokenExpiresAtUtc: string;
  lastAuthenticatedAtUtc: string;
  stepUpExpiresAtUtc: string | null;
  revokedAtUtc: string | null;
}

export interface MachineTrustMaterial {
  id: string;
  kind: MachineTrustSubjectKind;
  subjectId: string;
  subjectName: string;
  thumbprint: string;
  certificatePem: string;
  issuedAtUtc: string;
  notBeforeUtc: string;
  expiresAtUtc: string;
  rotateAfterUtc: string;
  revokedAtUtc: string | null;
  replacedById: string | null;
}

export interface IssuedMachineTrustMaterial {
  material: MachineTrustMaterial;
  privateKeyPem: string;
}

export interface SessionTokenPair {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  refreshToken: string;
  refreshTokenExpiresAtUtc: string;
}

export interface AuthSessionResponse {
  session: PlatformSession;
  tokens: SessionTokenPair;
  admin: AdminAccount | null;
  user: User | null;
}

export interface ClientAuthSessionResponse {
  session: PlatformSession;
  tokens: SessionTokenPair;
  user: User;
  device: Device;
  resolution: PolicyResolutionResult;
  bundle: ResolvedPolicyBundle;
  placement: GatewayPlacement;
}

export interface ApiErrorResponse {
  error: string;
  errorCode: string;
  policy?: string | null;
}

export interface ValidationErrorResponse {
  error: string;
  errorCode: string;
  details: string[];
}

export interface DashboardSnapshot {
  admins: AdminAccount[];
  users: User[];
  devices: Device[];
  gateways: Gateway[];
  gatewayPools: GatewayPool[];
  policies: PolicyRule[];
  sessions: TunnelSession[];
  healthSamples: HealthSample[];
  alerts: Alert[];
  authProviders: AuthProviderConfig[];
  auditEvents: AuditEvent[];
}

export const seededSnapshot: DashboardSnapshot = {
  admins: [
    {
      id: "admin-1",
      username: "admin",
      role: "SuperAdmin",
      mustChangePassword: true,
      mfaEnrolled: false
    }
  ],
  users: [
    {
      id: "user-1",
      username: "user",
      displayName: "Default Test User",
      enabled: false,
      testAccount: true,
      provider: "local",
      groupIds: ["group-test"],
      policyIds: ["policy-test"],
      tenantId: "tenant-default"
    },
    {
      id: "user-2",
      username: "maria.diaz",
      displayName: "Maria Diaz",
      enabled: true,
      testAccount: false,
      provider: "entra",
      groupIds: ["group-engineering", "entra:eng"],
      policyIds: ["policy-core"],
      tenantId: "tenant-default"
    }
  ],
  devices: [
    {
      id: "device-1",
      name: "MARIAD-LT-14",
      userId: "user-2",
      city: "New York",
      country: "United States",
      publicIp: "203.0.113.10",
      managed: true,
      compliant: true,
      postureScore: 96,
      connectionState: "Healthy",
      lastSeenUtc: "2026-03-07T23:45:00Z",
      tenantId: "tenant-default",
      registrationState: "Enrolled",
      enrollmentKind: "ReEnrollment",
      hardwareKey: "hw-mariad-lt-14",
      serialNumber: "MDLT14-2394",
      operatingSystem: "Windows 11 24H2",
      registeredAtUtc: "2026-03-01T15:00:00Z",
      lastEnrollmentAtUtc: "2026-03-07T22:55:00Z",
      disabledAtUtc: null,
      complianceReasons: []
    },
    {
      id: "device-2",
      name: "QA-LAB-DEVICE",
      userId: "user-1",
      city: "Austin",
      country: "United States",
      publicIp: "203.0.113.22",
      managed: true,
      compliant: false,
      postureScore: 58,
      connectionState: "PolicyBlocked",
      lastSeenUtc: "2026-03-07T23:41:00Z",
      tenantId: "tenant-default",
      registrationState: "Pending",
      enrollmentKind: "Bootstrap",
      hardwareKey: "hw-qa-lab-device",
      serialNumber: "QALAB-9911",
      operatingSystem: "Windows 11 24H2",
      registeredAtUtc: "2026-03-07T22:40:00Z",
      lastEnrollmentAtUtc: "2026-03-07T22:40:00Z",
      disabledAtUtc: null,
      complianceReasons: ["firewall_disabled", "device_pending_approval"]
    }
  ],
  gateways: [
    {
      id: "gw-1",
      name: "us-east-core-1",
      region: "us-east",
      health: "green",
      loadPercent: 31,
      peerCount: 124,
      cpuPercent: 38,
      memoryPercent: 54,
      latencyMs: 18,
      tenantId: "tenant-default",
      lastHeartbeatUtc: "2026-03-07T23:45:00Z"
    },
    {
      id: "gw-2",
      name: "us-east-core-2",
      region: "us-east",
      health: "yellow",
      loadPercent: 72,
      peerCount: 140,
      cpuPercent: 70,
      memoryPercent: 68,
      latencyMs: 42,
      tenantId: "tenant-default",
      lastHeartbeatUtc: "2026-03-07T23:44:42Z"
    }
  ],
  gatewayPools: [
    {
      id: "pool-1",
      name: "East Coast Pool",
      regions: ["us-east"],
      gatewayIds: ["gw-1", "gw-2"],
      tenantId: "tenant-default"
    }
  ],
  policies: [
    {
      id: "policy-test",
      name: "Default Test Policy",
      cidrs: ["10.10.20.0/24"],
      dnsZones: ["test.owlprotect.local"],
      ports: [443, 8443],
      mode: "split-tunnel",
      tenantId: "tenant-default",
      priority: 50,
      targetGroupIds: ["group-test"],
      requireManaged: true,
      requireCompliant: false,
      minimumPostureScore: 40,
      allowedDeviceStates: ["Pending", "Enrolled"]
    },
    {
      id: "policy-core",
      name: "Core Enterprise Access",
      cidrs: ["10.0.0.0/8", "172.16.20.0/24"],
      dnsZones: ["corp.owlprotect.local", "eng.owlprotect.local"],
      ports: [53, 80, 443, 3389],
      mode: "split-tunnel",
      tenantId: "tenant-default",
      priority: 100,
      targetGroupIds: ["group-engineering", "entra:eng"],
      requireManaged: true,
      requireCompliant: true,
      minimumPostureScore: 80,
      allowedDeviceStates: ["Enrolled"]
    }
  ],
  sessions: [
    {
      id: "session-1",
      userId: "user-2",
      deviceId: "device-1",
      gatewayId: "gw-1",
      connectedAtUtc: "2026-03-07T22:59:00Z",
      handshakeAgeSeconds: 21,
      throughputMbps: 188,
      tenantId: "tenant-default",
      policyBundleVersion: "seed-policy-core-v1",
      authorizedAtUtc: "2026-03-07T22:59:00Z",
      revalidateAfterUtc: "2026-03-07T23:04:00Z"
    }
  ],
  healthSamples: [
    {
      id: "health-1",
      deviceId: "device-1",
      state: "Healthy",
      severity: "green",
      latencyMs: 18,
      jitterMs: 4,
      packetLossPercent: 0.1,
      throughputMbps: 188,
      signalStrengthPercent: 91,
      dnsReachable: true,
      routeHealthy: true,
      sampledAtUtc: "2026-03-07T23:45:00Z",
      message: "Tunnel healthy with low jitter and strong signal.",
      tenantId: "tenant-default"
    },
    {
      id: "health-2",
      deviceId: "device-2",
      state: "PolicyBlocked",
      severity: "red",
      latencyMs: 0,
      jitterMs: 0,
      packetLossPercent: 0,
      throughputMbps: 0,
      signalStrengthPercent: 72,
      dnsReachable: false,
      routeHealthy: false,
      sampledAtUtc: "2026-03-07T23:41:00Z",
      message: "Device posture is noncompliant, so enterprise routes remain blocked.",
      tenantId: "tenant-default"
    }
  ],
  alerts: [
    {
      id: "alert-1",
      severity: "red",
      title: "Test account disabled",
      description: "The seeded test user remains disabled until an admin explicitly enables it.",
      targetType: "device",
      targetId: "device-2",
      createdAtUtc: "2026-03-07T23:00:00Z",
      tenantId: "tenant-default"
    },
    {
      id: "alert-2",
      severity: "yellow",
      title: "Gateway load rising",
      description: "Gateway us-east-core-2 is above the yellow load threshold.",
      targetType: "gateway",
      targetId: "gw-2",
      createdAtUtc: "2026-03-07T23:39:00Z",
      tenantId: "tenant-default"
    }
  ],
  authProviders: [
    {
      id: "auth-1",
      name: "Microsoft Entra ID",
      type: "entra",
      issuer: "https://login.microsoftonline.com/example/v2.0",
      clientId: "entra-client-id",
      usernameClaimPaths: ["preferred_username", "upn", "email", "sub"],
      groupClaimPaths: ["groups"],
      mfaClaimPaths: ["amr", "acr"],
      requireMfa: true,
      silentSsoEnabled: true,
      tenantId: "tenant-default"
    },
    {
      id: "auth-2",
      name: "Generic OIDC",
      type: "oidc",
      issuer: "https://identity.example.com",
      clientId: "oidc-client-id",
      usernameClaimPaths: ["preferred_username", "email", "sub"],
      groupClaimPaths: ["groups"],
      mfaClaimPaths: ["amr"],
      requireMfa: true,
      silentSsoEnabled: false,
      tenantId: "tenant-default"
    }
  ],
  auditEvents: [
    {
      id: "audit-1",
      sequence: 1,
      actor: "system",
      action: "seed-default-admin",
      targetType: "admin",
      targetId: "admin-1",
      createdAtUtc: "2026-03-07T22:00:00Z",
      outcome: "success",
      detail: "Seeded default admin with forced password reset.",
      previousEventHash: null,
      eventHash: "5c1536bfc3f2d43bdf09bb0d4fc977d525e82bcc024969536980da25c021ed80",
      tenantId: "tenant-default"
    },
    {
      id: "audit-2",
      sequence: 2,
      actor: "system",
      action: "seed-test-user",
      targetType: "user",
      targetId: "user-1",
      createdAtUtc: "2026-03-07T22:00:00Z",
      outcome: "success",
      detail: "Seeded disabled test user with restricted default policy.",
      previousEventHash: "5c1536bfc3f2d43bdf09bb0d4fc977d525e82bcc024969536980da25c021ed80",
      eventHash: "81c1d5d266f11fa950a49b4130563f9811c0433b1696795ab2bd58b8d085def2",
      tenantId: "tenant-default"
    }
  ]
};

const heartbeatTtlMs = 60_000;

function normalizeHealth(score: number, available: boolean): HealthSeverity {
  if (!available || score < 40) {
    return "red";
  }

  if (score < 70) {
    return "yellow";
  }

  return "green";
}

export function scoreGateway(gateway: Gateway, now = "2026-03-07T23:45:00Z"): GatewayScore {
  const nowMs = new Date(now).getTime();
  const heartbeatMs = gateway.lastHeartbeatUtc ? new Date(gateway.lastHeartbeatUtc).getTime() : 0;
  const signals: string[] = [];
  let score = 100;

  if (!heartbeatMs || nowMs - heartbeatMs > heartbeatTtlMs) {
    score -= 40;
    signals.push("heartbeat_stale");
  }

  if (gateway.health === "red") {
    score -= 45;
    signals.push("health_red");
  } else if (gateway.health === "yellow") {
    score -= 20;
    signals.push("health_yellow");
  }

  if (gateway.loadPercent >= 85) {
    score -= 18;
    signals.push("load_critical");
  } else if (gateway.loadPercent >= 70) {
    score -= 8;
    signals.push("load_rising");
  }

  if (gateway.latencyMs >= 80) {
    score -= 20;
    signals.push("latency_critical");
  } else if (gateway.latencyMs >= 50) {
    score -= 10;
    signals.push("latency_high");
  } else if (gateway.latencyMs >= 30) {
    score -= 4;
    signals.push("latency_elevated");
  }

  if (gateway.cpuPercent >= 90) {
    score -= 8;
    signals.push("cpu_hot");
  } else if (gateway.cpuPercent >= 75) {
    score -= 4;
    signals.push("cpu_busy");
  }

  if (gateway.memoryPercent >= 90) {
    score -= 8;
    signals.push("memory_hot");
  } else if (gateway.memoryPercent >= 75) {
    score -= 4;
    signals.push("memory_busy");
  }

  score = Math.max(0, Math.min(100, score));
  const available = gateway.health !== "red" && heartbeatMs !== 0 && nowMs - heartbeatMs <= heartbeatTtlMs;

  return {
    gatewayId: gateway.id,
    gatewayName: gateway.name,
    region: gateway.region,
    health: normalizeHealth(score, available),
    score,
    available,
    loadPercent: gateway.loadPercent,
    latencyMs: gateway.latencyMs,
    cpuPercent: gateway.cpuPercent,
    memoryPercent: gateway.memoryPercent,
    peerCount: gateway.peerCount,
    lastHeartbeatUtc: gateway.lastHeartbeatUtc,
    signals,
    tenantId: gateway.tenantId
  };
}

export function buildGatewayPoolStatuses(snapshot: DashboardSnapshot, now = "2026-03-07T23:45:00Z"): GatewayPoolStatus[] {
  return snapshot.gatewayPools
    .map((pool) => {
      const gateways = snapshot.gateways
        .filter((gateway) => pool.gatewayIds.includes(gateway.id))
        .map((gateway) => scoreGateway(gateway, now))
        .sort((left, right) => {
          if (left.available !== right.available) {
            return left.available ? -1 : 1;
          }

          if (left.score !== right.score) {
            return right.score - left.score;
          }

          return left.latencyMs - right.latencyMs;
        });
      const primary = gateways.find((gateway) => gateway.available) ?? gateways[0];
      const failoverGatewayIds = gateways
        .filter((gateway) => gateway.gatewayId !== primary?.gatewayId && gateway.available)
        .map((gateway) => gateway.gatewayId);
      const score = primary ? Math.min(100, primary.score + (failoverGatewayIds.length > 0 ? 5 : 0)) : 0;
      const health: HealthSeverity = primary
        ? primary.health === "green" && failoverGatewayIds.length === 0 && primary.score < 85
          ? "yellow"
          : primary.health
        : "red";

      return {
        poolId: pool.id,
        name: pool.name,
        regions: pool.regions,
        health,
        score,
        primaryGatewayId: primary?.gatewayId ?? null,
        failoverGatewayIds,
        gateways,
        tenantId: pool.tenantId
      };
    })
    .sort((left, right) => right.score - left.score);
}

export function buildDeviceDiagnostics(snapshot: DashboardSnapshot): DeviceDiagnostics[] {
  const asDiagnostic = (value: DeviceDiagnostics) => value;

  return snapshot.devices
    .map((device) => {
      const sample = snapshot.healthSamples.find((item) => item.deviceId === device.id) ?? null;
      const session = snapshot.sessions.find((item) => item.deviceId === device.id) ?? null;
      const gateway = session ? snapshot.gateways.find((item) => item.id === session.gatewayId) ?? null : null;
      const signals = [
        sample ? `latency:${sample.latencyMs}ms` : null,
        sample ? `jitter:${sample.jitterMs}ms` : null,
        sample ? `loss:${sample.packetLossPercent}%` : null,
        sample ? `signal:${sample.signalStrengthPercent}%` : null,
        gateway ? `gateway:${gateway.name}` : null
      ].filter(Boolean) as string[];

      switch (device.connectionState) {
        case "PolicyBlocked":
          return asDiagnostic({
            deviceId: device.id,
            deviceName: device.name,
            state: device.connectionState,
            scope: "Policy",
            severity: "red",
            summary: "Policy is blocking enterprise access.",
            detail: "The device is failing posture or enrollment checks, so enterprise routes remain disabled.",
            gatewayId: session?.gatewayId ?? null,
            gatewayName: gateway?.name ?? null,
            observedAtUtc: sample?.sampledAtUtc ?? device.lastSeenUtc,
            signals,
            tenantId: device.tenantId
          });
        case "AuthExpired":
          return asDiagnostic({
            deviceId: device.id,
            deviceName: device.name,
            state: device.connectionState,
            scope: "Authentication",
            severity: "red",
            summary: "Client authentication expired.",
            detail: "The device must refresh its client session before the tunnel can be restored.",
            gatewayId: session?.gatewayId ?? null,
            gatewayName: gateway?.name ?? null,
            observedAtUtc: sample?.sampledAtUtc ?? device.lastSeenUtc,
            signals,
            tenantId: device.tenantId
          });
        case "LocalNetworkPoor":
        case "LowBandwidth":
          return asDiagnostic({
            deviceId: device.id,
            deviceName: device.name,
            state: device.connectionState,
            scope: "LocalNetwork",
            severity: sample?.severity ?? "yellow",
            summary: "Local network quality is degrading the tunnel.",
            detail: "Signal strength, packet loss, or last-mile throughput is below the expected threshold before traffic reaches the gateway.",
            gatewayId: session?.gatewayId ?? null,
            gatewayName: gateway?.name ?? null,
            observedAtUtc: sample?.sampledAtUtc ?? device.lastSeenUtc,
            signals,
            tenantId: device.tenantId
          });
        case "GatewayDegraded":
          return asDiagnostic({
            deviceId: device.id,
            deviceName: device.name,
            state: device.connectionState,
            scope: "Gateway",
            severity: sample?.severity ?? "yellow",
            summary: "Gateway performance is the primary bottleneck.",
            detail: "The tunnel is established, but the selected gateway is reporting degraded health or elevated latency.",
            gatewayId: session?.gatewayId ?? null,
            gatewayName: gateway?.name ?? null,
            observedAtUtc: sample?.sampledAtUtc ?? device.lastSeenUtc,
            signals,
            tenantId: device.tenantId
          });
        case "ServerUnavailable":
          return asDiagnostic({
            deviceId: device.id,
            deviceName: device.name,
            state: device.connectionState,
            scope: sample && (!sample.dnsReachable || sample.signalStrengthPercent < 60) ? "LocalNetwork" : gateway ? "Gateway" : "ServerSide",
            severity: "red",
            summary: "A server-side dependency is unavailable.",
            detail: "The client still has local connectivity, but the selected gateway or a backend dependency is not responding.",
            gatewayId: session?.gatewayId ?? null,
            gatewayName: gateway?.name ?? null,
            observedAtUtc: sample?.sampledAtUtc ?? device.lastSeenUtc,
            signals,
            tenantId: device.tenantId
          });
        default:
          return asDiagnostic({
            deviceId: device.id,
            deviceName: device.name,
            state: device.connectionState,
            scope: "Healthy",
            severity: "green",
            summary: "Tunnel performance is healthy.",
            detail: "Latency, jitter, and route health are within the normal operating envelope.",
            gatewayId: session?.gatewayId ?? null,
            gatewayName: gateway?.name ?? null,
            observedAtUtc: sample?.sampledAtUtc ?? device.lastSeenUtc,
            signals,
            tenantId: device.tenantId
          });
      }
    })
    .sort((left, right) => {
      const severityWeight: Record<HealthSeverity, number> = { red: 0, yellow: 1, green: 2 };
      if (severityWeight[left.severity] !== severityWeight[right.severity]) {
        return severityWeight[left.severity] - severityWeight[right.severity];
      }

      return right.observedAtUtc.localeCompare(left.observedAtUtc);
    });
}

export function buildConnectionCityAggregates(snapshot: DashboardSnapshot): ConnectionMapCityAggregate[] {
  return Array.from(
    snapshot.devices.reduce((accumulator, device) => {
      const key = `${device.city}|${device.country}|${device.tenantId}`;
      const sessionGateways = snapshot.sessions
        .filter((session) => session.deviceId === device.id)
        .map((session) => session.gatewayId);
      const current = accumulator.get(key) ?? {
        city: device.city,
        country: device.country,
        deviceCount: 0,
        healthyCount: 0,
        impactedCount: 0,
        blockedCount: 0,
        gatewayIds: new Set<string>(),
        tenantId: device.tenantId
      };

      current.deviceCount += 1;
      if (device.connectionState === "Healthy") {
        current.healthyCount += 1;
      } else if (device.connectionState === "PolicyBlocked" || device.connectionState === "AuthExpired") {
        current.blockedCount += 1;
      } else {
        current.impactedCount += 1;
      }

      sessionGateways.forEach((gatewayId) => current.gatewayIds.add(gatewayId));
      accumulator.set(key, current);
      return accumulator;
    }, new Map<string, { city: string; country: string; deviceCount: number; healthyCount: number; impactedCount: number; blockedCount: number; gatewayIds: Set<string>; tenantId: string }>())
  ).map(([, aggregate]) => ({
    city: aggregate.city,
    country: aggregate.country,
    deviceCount: aggregate.deviceCount,
    healthyCount: aggregate.healthyCount,
    impactedCount: aggregate.impactedCount,
    blockedCount: aggregate.blockedCount,
    gatewayIds: Array.from(aggregate.gatewayIds).sort(),
    tenantId: aggregate.tenantId
  }))
    .sort((left, right) => right.impactedCount - left.impactedCount || right.deviceCount - left.deviceCount || left.city.localeCompare(right.city));
}

export const seededGatewayPoolStatuses = buildGatewayPoolStatuses(seededSnapshot);
export const seededDeviceDiagnostics = buildDeviceDiagnostics(seededSnapshot);
export const seededConnectionCityAggregates = buildConnectionCityAggregates(seededSnapshot);
