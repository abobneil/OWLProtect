export type AdminRole = "SuperAdmin" | "Operator" | "ReadOnly";
export type PlatformSessionKind = "Admin" | "User" | "Client";
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
}

export interface GatewayPool {
  id: string;
  name: string;
  regions: string[];
  gatewayIds: string[];
}

export interface PolicyRule {
  id: string;
  name: string;
  cidrs: string[];
  dnsZones: string[];
  ports: number[];
  mode: "split-tunnel";
}

export interface TunnelSession {
  id: string;
  userId: string;
  deviceId: string;
  gatewayId: string;
  connectedAtUtc: string;
  handshakeAgeSeconds: number;
  throughputMbps: number;
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
}

export interface Alert {
  id: string;
  severity: HealthSeverity;
  title: string;
  description: string;
  targetType: "device" | "gateway" | "policy" | "auth";
  targetId: string;
  createdAtUtc: string;
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
      policyIds: ["policy-test"]
    },
    {
      id: "user-2",
      username: "maria.diaz",
      displayName: "Maria Diaz",
      enabled: true,
      testAccount: false,
      provider: "entra",
      groupIds: ["group-engineering"],
      policyIds: ["policy-core"]
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
      lastSeenUtc: "2026-03-07T23:45:00Z"
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
      lastSeenUtc: "2026-03-07T23:41:00Z"
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
      latencyMs: 18
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
      latencyMs: 42
    }
  ],
  gatewayPools: [
    {
      id: "pool-1",
      name: "East Coast Pool",
      regions: ["us-east"],
      gatewayIds: ["gw-1", "gw-2"]
    }
  ],
  policies: [
    {
      id: "policy-test",
      name: "Default Test Policy",
      cidrs: ["10.10.20.0/24"],
      dnsZones: ["test.owlprotect.local"],
      ports: [443, 8443],
      mode: "split-tunnel"
    },
    {
      id: "policy-core",
      name: "Core Enterprise Access",
      cidrs: ["10.0.0.0/8", "172.16.20.0/24"],
      dnsZones: ["corp.owlprotect.local", "eng.owlprotect.local"],
      ports: [53, 80, 443, 3389],
      mode: "split-tunnel"
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
      throughputMbps: 188
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
      message: "Tunnel healthy with low jitter and strong signal."
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
      message: "Device posture is noncompliant, so enterprise routes remain blocked."
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
      createdAtUtc: "2026-03-07T23:00:00Z"
    },
    {
      id: "alert-2",
      severity: "yellow",
      title: "Gateway load rising",
      description: "Gateway us-east-core-2 is above the yellow load threshold.",
      targetType: "gateway",
      targetId: "gw-2",
      createdAtUtc: "2026-03-07T23:39:00Z"
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
      silentSsoEnabled: true
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
      silentSsoEnabled: false
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
      eventHash: "5c1536bfc3f2d43bdf09bb0d4fc977d525e82bcc024969536980da25c021ed80"
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
      eventHash: "81c1d5d266f11fa950a49b4130563f9811c0433b1696795ab2bd58b8d085def2"
    }
  ]
};
