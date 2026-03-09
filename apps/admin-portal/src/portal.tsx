import {
  startTransition,
  createContext,
  useContext,
  useEffect,
  useEffectEvent,
  useRef,
  useState,
  type Dispatch,
  type ReactNode,
  type SetStateAction
} from "react";
import {
  CONTROL_PLANE_STREAM_PATHS,
  type AdminAccount,
  type AdminLoginRequest,
  type Alert,
  type AuditEvent,
  type AuditExportResponse,
  type AuditRetentionCheckpoint,
  type AuditRetentionRunResponse,
  type AuthMeResponse,
  type AuthProviderConfig,
  type AuthProviderUpsertRequest,
  type AuthSessionResponse,
  type BootstrapStatus,
  type ConnectionMapCityAggregate,
  type ControlPlaneStreamFrame,
  type Device,
  type DeviceDisconnectResponse,
  type DeviceDiagnostics,
  type Gateway,
  type GatewayPool,
  type GatewayPoolStatus,
  type GatewayUpsertRequest,
  type PasswordChangeRequest,
  type PolicyRule,
  type PolicyUpsertRequest,
  type StepUpRequest,
  type Tenant,
  type TunnelSession,
  type User,
  type UserUpsertRequest
} from "@owlprotect/contracts";
import {
  ControlPlaneApiError,
  connectControlPlaneStream,
  createControlPlaneClient,
  isTokenExpired,
  loadStoredSession,
  persistSession,
  resolveControlPlaneBaseUrl,
  type StoredAdminSession
} from "./controlPlane";

type FetchState = "idle" | "loading" | "refreshing" | "ready" | "error";
type StreamState = "disconnected" | "connecting" | "live" | "retrying";
type StreamKey = "alerts" | "gatewayHealth" | "telemetry" | "sessions";

interface PortalData {
  admins: AdminAccount[];
  alerts: Alert[];
  auditCheckpoints: AuditRetentionCheckpoint[];
  auditEvents: AuditEvent[];
  connectionMap: ConnectionMapCityAggregate[];
  devices: Device[];
  diagnostics: DeviceDiagnostics[];
  gateways: Gateway[];
  gatewayPools: GatewayPool[];
  gatewayPoolStatuses: GatewayPoolStatus[];
  providers: AuthProviderConfig[];
  policies: PolicyRule[];
  sessions: TunnelSession[];
  tenants: Tenant[];
  users: User[];
}

interface StreamPresence {
  error: string | null;
  lastEventAtUtc: string | null;
  sequence: number;
  state: StreamState;
}

interface StepUpPromptState {
  error: string | null;
  open: boolean;
  operationName: string | null;
  submitting: boolean;
}

interface PortalContextValue {
  apiBaseUrl: string;
  authSession: StoredAdminSession | null;
  bootstrapError: string | null;
  bootstrapState: FetchState;
  bootstrapStatus: BootstrapStatus | null;
  data: PortalData;
  dataError: string | null;
  dataState: FetchState;
  exportAuditEvents: (before?: string | null, limit?: number) => Promise<AuditExportResponse>;
  hasActiveStepUp: boolean;
  hasCompliantAdmin: boolean;
  refreshAll: () => Promise<void>;
  signIn: (request: AdminLoginRequest) => Promise<void>;
  signOut: () => Promise<void>;
  stepUpPrompt: StepUpPromptState;
  streams: Record<StreamKey, StreamPresence>;
  submitStepUp: (password: string) => Promise<void>;
  cancelStepUp: () => void;
  updateBootstrapPassword: (request: PasswordChangeRequest) => Promise<void>;
  enrollBootstrapMfa: () => Promise<void>;
  upsertAuthProvider: (request: AuthProviderUpsertRequest) => Promise<AuthProviderConfig>;
  deleteAuthProvider: (providerId: string) => Promise<void>;
  upsertGateway: (request: GatewayUpsertRequest) => Promise<Gateway>;
  deleteGateway: (gatewayId: string) => Promise<void>;
  upsertPolicy: (request: PolicyUpsertRequest) => Promise<PolicyRule>;
  deletePolicy: (policyId: string) => Promise<void>;
  runAuditRetention: () => Promise<AuditRetentionRunResponse>;
  revokeSession: (sessionId: string) => Promise<void>;
  upsertUser: (request: UserUpsertRequest) => Promise<User>;
  enableUser: (userId: string) => Promise<User>;
  disableUser: (userId: string) => Promise<User>;
  deleteUser: (userId: string) => Promise<void>;
  approveDevice: (deviceId: string) => Promise<Device>;
  disconnectDevice: (deviceId: string) => Promise<DeviceDisconnectResponse>;
}

const STORAGE_KEY = "owlprotect.admin.portal.session";
const STREAM_RETRY_DELAY_MS = 3_000;
const EMPTY_DATA: PortalData = {
  admins: [],
  alerts: [],
  auditCheckpoints: [],
  auditEvents: [],
  connectionMap: [],
  devices: [],
  diagnostics: [],
  gateways: [],
  gatewayPools: [],
  gatewayPoolStatuses: [],
  policies: [],
  providers: [],
  sessions: [],
  tenants: [],
  users: []
};
const INITIAL_STREAMS: Record<StreamKey, StreamPresence> = {
  alerts: { error: null, lastEventAtUtc: null, sequence: 0, state: "disconnected" },
  gatewayHealth: { error: null, lastEventAtUtc: null, sequence: 0, state: "disconnected" },
  telemetry: { error: null, lastEventAtUtc: null, sequence: 0, state: "disconnected" },
  sessions: { error: null, lastEventAtUtc: null, sequence: 0, state: "disconnected" }
};

const PortalContext = createContext<PortalContextValue | null>(null);

export function PortalProvider({ children }: { children: ReactNode }) {
  const [authSession, setAuthSession] = useState<StoredAdminSession | null>(() => loadStoredSession(STORAGE_KEY));
  const [bootstrapStatus, setBootstrapStatus] = useState<BootstrapStatus | null>(null);
  const [bootstrapState, setBootstrapState] = useState<FetchState>("idle");
  const [bootstrapError, setBootstrapError] = useState<string | null>(null);
  const [data, setData] = useState<PortalData>(EMPTY_DATA);
  const [dataState, setDataState] = useState<FetchState>("idle");
  const [dataError, setDataError] = useState<string | null>(null);
  const [streams, setStreams] = useState<Record<StreamKey, StreamPresence>>(INITIAL_STREAMS);
  const [stepUpPrompt, setStepUpPrompt] = useState<StepUpPromptState>({
    error: null,
    open: false,
    operationName: null,
    submitting: false
  });

  const authSessionRef = useRef(authSession);
  const pendingPrivilegedActionRef = useRef<{
    action: () => Promise<void>;
    operationName: string;
    reject: (error: unknown) => void;
    resolve: () => void;
  } | null>(null);

  authSessionRef.current = authSession;

  function setPersistedSession(nextSession: StoredAdminSession | null) {
    authSessionRef.current = nextSession;
    persistSession(STORAGE_KEY, nextSession);
    setAuthSession(nextSession);
    if (!nextSession) {
      startTransition(() => {
        setData(EMPTY_DATA);
        setDataState("idle");
        setDataError(null);
        setStreams(INITIAL_STREAMS);
      });
    }
  }

  const apiRef = useRef(createControlPlaneClient(() => authSessionRef.current, setPersistedSession));
  const api = apiRef.current;
  const apiBaseUrl = resolveControlPlaneBaseUrl();
  const hasCompliantAdmin = !!authSession?.admin && !authSession.admin.mustChangePassword && authSession.admin.mfaEnrolled;
  const hasActiveStepUp = !!authSession?.session.stepUpExpiresAtUtc && new Date(authSession.session.stepUpExpiresAtUtc).getTime() > Date.now();

  async function loadBootstrap() {
    setBootstrapState((current) => current === "ready" ? "refreshing" : "loading");
    setBootstrapError(null);

    try {
      const nextBootstrapStatus = await api.get<BootstrapStatus>("/bootstrap", { auth: false, retryOnAuthFailure: false });
      startTransition(() => {
        setBootstrapStatus(nextBootstrapStatus);
        setBootstrapState("ready");
      });
    } catch (error) {
      startTransition(() => {
        setBootstrapState("error");
        setBootstrapError(toDisplayError(error));
      });
    }
  }

  async function syncAdminIdentity() {
    if (!authSessionRef.current) {
      return null;
    }

    const me = await api.get<AuthMeResponse>("/auth/me");
    if (!me.admin) {
      throw new ControlPlaneApiError("Admin session required.", 403, "admin_role_required");
    }

    setPersistedSession({
      ...authSessionRef.current,
      admin: me.admin,
      session: me.session,
      user: null
    });

    return me.admin;
  }

  async function loadProtectedData(mode: FetchState = "loading") {
    if (!authSessionRef.current?.admin || authSessionRef.current.admin.mustChangePassword || !authSessionRef.current.admin.mfaEnrolled) {
      startTransition(() => {
        setData(EMPTY_DATA);
        setDataState("idle");
        setDataError(null);
      });
      return;
    }

    setDataState(mode);
    setDataError(null);

    try {
      const [
        tenants,
        admins,
        users,
        devices,
        gateways,
        gatewayPools,
        gatewayPoolStatuses,
        policies,
        sessions,
        alerts,
        diagnostics,
        connectionMap,
        providers,
        auditEvents,
        auditCheckpoints
      ] = await Promise.all([
        api.get<Tenant[]>("/tenants"),
        api.get<AdminAccount[]>("/admins"),
        api.get<User[]>("/users"),
        api.get<Device[]>("/devices"),
        api.get<Gateway[]>("/gateways"),
        api.get<GatewayPool[]>("/gateway-pools"),
        api.get<GatewayPoolStatus[]>("/gateway-pools/health"),
        api.get<PolicyRule[]>("/policies"),
        api.get<TunnelSession[]>("/sessions"),
        api.get<Alert[]>("/alerts"),
        api.get<DeviceDiagnostics[]>("/telemetry/diagnostics"),
        api.get<ConnectionMapCityAggregate[]>("/map/connections/cities"),
        api.get<AuthProviderConfig[]>("/auth/providers"),
        api.get<AuditEvent[]>("/audit"),
        api.get<AuditRetentionCheckpoint[]>("/audit/checkpoints")
      ]);

      startTransition(() => {
        setData({
          admins,
          alerts,
          auditCheckpoints,
          auditEvents,
          connectionMap,
          devices,
          diagnostics,
          gateways,
          gatewayPools,
          gatewayPoolStatuses,
          policies,
          providers,
          sessions,
          tenants,
          users
        });
        setDataState("ready");
      });
    } catch (error) {
      if (isUnauthorized(error)) {
        setPersistedSession(null);
        await loadBootstrap();
        return;
      }

      startTransition(() => {
        setDataState("error");
        setDataError(toDisplayError(error));
      });
    }
  }

  async function refreshHealthViews() {
    if (!hasCompliantAdmin) {
      return;
    }

    try {
      const [diagnostics, connectionMap] = await Promise.all([
        api.get<DeviceDiagnostics[]>("/telemetry/diagnostics"),
        api.get<ConnectionMapCityAggregate[]>("/map/connections/cities")
      ]);

      startTransition(() => {
        setData((current) => ({ ...current, connectionMap, diagnostics }));
      });
    } catch {
      // Keep the latest successful state visible.
    }
  }

  async function refreshGatewayViews() {
    if (!hasCompliantAdmin) {
      return;
    }

    try {
      const gatewayPoolStatuses = await api.get<GatewayPoolStatus[]>("/gateway-pools/health");
      startTransition(() => {
        setData((current) => ({ ...current, gatewayPoolStatuses }));
      });
    } catch {
      // Keep the latest successful state visible.
    }
  }

  async function restoreSession() {
    if (!authSessionRef.current) {
      await loadBootstrap();
      return;
    }

    try {
      const admin = await syncAdminIdentity();
      await loadBootstrap();

      if (admin && !admin.mustChangePassword && admin.mfaEnrolled) {
        await loadProtectedData("loading");
      } else {
        startTransition(() => {
          setData(EMPTY_DATA);
          setDataState("idle");
          setDataError(null);
        });
      }
    } catch {
      setPersistedSession(null);
      await loadBootstrap();
    }
  }

  useEffect(() => {
    void restoreSession();
  }, []);

  async function signIn(request: AdminLoginRequest) {
    const response = await api.post<AuthSessionResponse>("/auth/admin/login", request, { auth: false, retryOnAuthFailure: false });
    if (!response.admin) {
      throw new ControlPlaneApiError("Admin session required.", 403, "admin_role_required");
    }

    const nextSession = response as StoredAdminSession;
    setPersistedSession(nextSession);
    await loadBootstrap();
    if (!nextSession.admin.mustChangePassword && nextSession.admin.mfaEnrolled) {
      await loadProtectedData("loading");
    } else {
      startTransition(() => {
        setData(EMPTY_DATA);
        setDataState("idle");
        setDataError(null);
      });
    }
  }

  async function signOut() {
    cancelStepUp();
    await api.revokeSession();
    await loadBootstrap();
  }

  async function refreshAll() {
    await loadBootstrap();
    if (authSessionRef.current) {
      await syncAdminIdentity();
      await loadProtectedData("refreshing");
    }
  }

  async function updateBootstrapPassword(request: PasswordChangeRequest) {
    await api.post<AdminAccount>("/admins/default/password", request);
    await syncAdminIdentity();
    await loadBootstrap();
    if (authSessionRef.current?.admin && !authSessionRef.current.admin.mustChangePassword && authSessionRef.current.admin.mfaEnrolled) {
      await loadProtectedData("loading");
    }
  }

  async function enrollBootstrapMfa() {
    await api.post<AdminAccount>("/admins/default/mfa");
    await syncAdminIdentity();
    await loadBootstrap();
    if (authSessionRef.current?.admin && !authSessionRef.current.admin.mustChangePassword && authSessionRef.current.admin.mfaEnrolled) {
      await loadProtectedData("loading");
    }
  }

  async function submitStepUp(password: string) {
    const pendingAction = pendingPrivilegedActionRef.current;
    if (!pendingAction) {
      return;
    }

    setStepUpPrompt((current) => ({ ...current, error: null, submitting: true }));

    try {
      const response = await api.post<{ session: StoredAdminSession["session"]; stepUpExpiresAtUtc: string }>("/auth/step-up", { password } satisfies StepUpRequest);
      if (authSessionRef.current) {
        setPersistedSession({
          ...authSessionRef.current,
          session: response.session
        });
      }

      pendingPrivilegedActionRef.current = null;
      setStepUpPrompt({ error: null, open: false, operationName: null, submitting: false });
      await pendingAction.action();
      pendingAction.resolve();
    } catch (error) {
      setStepUpPrompt((current) => ({
        ...current,
        error: toDisplayError(error),
        submitting: false
      }));
    }
  }

  function cancelStepUp() {
    const pendingAction = pendingPrivilegedActionRef.current;
    if (pendingAction) {
      pendingAction.reject(new Error("Privileged action cancelled."));
    }

    pendingPrivilegedActionRef.current = null;
    setStepUpPrompt({ error: null, open: false, operationName: null, submitting: false });
  }

  function runWithPrivilege(operationName: string, action: () => Promise<void>) {
    if (hasActiveStepUp) {
      return action();
    }

    return new Promise<void>((resolve, reject) => {
      pendingPrivilegedActionRef.current = { action, operationName, reject, resolve };
      setStepUpPrompt({
        error: null,
        open: true,
        operationName,
        submitting: false
      });
    });
  }

  async function upsertUser(request: UserUpsertRequest) {
    const updated = await api.post<User>("/users", request);
    startTransition(() => {
      setData((current) => ({
        ...current,
        users: upsertById(current.users, updated)
      }));
    });
    await loadProtectedData("refreshing");
    return updated;
  }

  async function enableUser(userId: string) {
    startTransition(() => {
      setData((current) => ({
        ...current,
        users: current.users.map((user) => user.id === userId ? { ...user, enabled: true } : user)
      }));
    });

    try {
      const updated = await api.post<User>(`/users/${userId}/enable`);
      startTransition(() => {
        setData((current) => ({
          ...current,
          users: upsertById(current.users, updated)
        }));
      });
      await loadProtectedData("refreshing");
      return updated;
    } catch (error) {
      await loadProtectedData("refreshing");
      throw error;
    }
  }

  async function disableUser(userId: string) {
    await runWithPrivilege(`Disable user ${userId}`, async () => {
      startTransition(() => {
        setData((current) => ({
          ...current,
          users: current.users.map((user) => user.id === userId ? { ...user, enabled: false } : user)
        }));
      });
    });

    try {
      const updated = await api.post<User>(`/users/${userId}/disable`);
      startTransition(() => {
        setData((current) => ({
          ...current,
          users: upsertById(current.users, updated)
        }));
      });
      await loadProtectedData("refreshing");
      return updated;
    } catch (error) {
      await loadProtectedData("refreshing");
      throw error;
    }
  }

  async function deleteUser(userId: string) {
    await runWithPrivilege(`Delete user ${userId}`, async () => {
      startTransition(() => {
        setData((current) => ({
          ...current,
          users: current.users.filter((user) => user.id !== userId)
        }));
      });
    });

    try {
      await api.delete<void>(`/users/${userId}`);
      await loadProtectedData("refreshing");
    } catch (error) {
      await loadProtectedData("refreshing");
      throw error;
    }
  }

  async function upsertGateway(request: GatewayUpsertRequest) {
    const updated = await api.post<Gateway>("/gateways", request);
    startTransition(() => {
      setData((current) => ({
        ...current,
        gateways: upsertById(current.gateways, updated)
      }));
    });
    await loadProtectedData("refreshing");
    return updated;
  }

  async function deleteGateway(gatewayId: string) {
    await runWithPrivilege(`Delete gateway ${gatewayId}`, async () => {
      startTransition(() => {
        setData((current) => ({
          ...current,
          gateways: current.gateways.filter((gateway) => gateway.id !== gatewayId)
        }));
      });
    });

    try {
      await api.delete<void>(`/gateways/${gatewayId}`);
      await loadProtectedData("refreshing");
    } catch (error) {
      await loadProtectedData("refreshing");
      throw error;
    }
  }

  async function upsertPolicy(request: PolicyUpsertRequest) {
    const updated = await api.post<PolicyRule>("/policies", request);
    startTransition(() => {
      setData((current) => ({
        ...current,
        policies: upsertById(current.policies, updated)
      }));
    });
    await loadProtectedData("refreshing");
    return updated;
  }

  async function deletePolicy(policyId: string) {
    await runWithPrivilege(`Delete policy ${policyId}`, async () => {
      startTransition(() => {
        setData((current) => ({
          ...current,
          policies: current.policies.filter((policy) => policy.id !== policyId)
        }));
      });
    });

    try {
      await api.delete<void>(`/policies/${policyId}`);
      await loadProtectedData("refreshing");
    } catch (error) {
      await loadProtectedData("refreshing");
      throw error;
    }
  }

  async function upsertAuthProvider(request: AuthProviderUpsertRequest) {
    const updated = await api.post<AuthProviderConfig>("/auth/providers", request);
    startTransition(() => {
      setData((current) => ({
        ...current,
        providers: upsertById(current.providers, updated)
      }));
    });
    await loadProtectedData("refreshing");
    return updated;
  }

  async function deleteAuthProvider(providerId: string) {
    await runWithPrivilege(`Delete auth provider ${providerId}`, async () => {
      startTransition(() => {
        setData((current) => ({
          ...current,
          providers: current.providers.filter((provider) => provider.id !== providerId)
        }));
      });
    });

    try {
      await api.delete<void>(`/auth/providers/${providerId}`);
      await loadProtectedData("refreshing");
    } catch (error) {
      await loadProtectedData("refreshing");
      throw error;
    }
  }

  async function revokeSession(sessionId: string) {
    await runWithPrivilege(`Revoke session ${sessionId}`, async () => {
      startTransition(() => {
        setData((current) => ({
          ...current,
          sessions: current.sessions.filter((session) => session.id !== sessionId)
        }));
      });
    });

    try {
      await api.post<void>(`/sessions/${sessionId}/revoke`);
      await loadProtectedData("refreshing");
    } catch (error) {
      await loadProtectedData("refreshing");
      throw error;
    }
  }

  async function approveDevice(deviceId: string) {
    let updated: Device | null = null;

    await runWithPrivilege(`Approve device ${deviceId}`, async () => {
      updated = await api.post<Device>(`/devices/${deviceId}/approve`);
      startTransition(() => {
        setData((current) => ({
          ...current,
          devices: updated ? upsertById(current.devices, updated) : current.devices
        }));
      });
    });

    await loadProtectedData("refreshing");
    return updated!;
  }

  async function disconnectDevice(deviceId: string) {
    let response: DeviceDisconnectResponse | null = null;

    await runWithPrivilege(`Disconnect device ${deviceId}`, async () => {
      response = await api.post<DeviceDisconnectResponse>(`/devices/${deviceId}/disconnect`);
      startTransition(() => {
        setData((current) => ({
          ...current,
          devices: current.devices.map((device) =>
            device.id === deviceId
              ? {
                  ...device,
                  connectionState: "AdminDisconnected",
                  lastSeenUtc: new Date().toISOString()
                }
              : device),
          sessions: current.sessions.filter((session) => session.deviceId !== deviceId)
        }));
      });
    });

    await loadProtectedData("refreshing");
    return response!;
  }

  async function runAuditRetention() {
    let result: AuditRetentionRunResponse | null = null;

    await runWithPrivilege("Run audit retention", async () => {
      result = await api.post<AuditRetentionRunResponse>("/audit/retention/run");
    });

    await loadProtectedData("refreshing");
    return result!;
  }

  async function exportAuditEvents(before?: string | null, limit = 500) {
    const search = new URLSearchParams();
    search.set("limit", `${limit}`);
    if (before) {
      search.set("before", before);
    }

    return api.get<AuditExportResponse>(`/audit/export?${search.toString()}`);
  }

  useControlPlaneStream({
    accessToken: authSession?.tokens.accessToken ?? null,
    enabled: hasCompliantAdmin && !!authSession?.tokens.accessToken && !isTokenExpired(authSession.tokens),
    onFrame: async (frame: ControlPlaneStreamFrame<Alert[]>) => {
      if (frame.payload) {
        startTransition(() => {
          setData((current) => ({ ...current, alerts: frame.payload ?? current.alerts }));
        });
      }
    },
    path: CONTROL_PLANE_STREAM_PATHS.alerts,
    streamKey: "alerts",
    updateStreamState: setStreams
  });

  useControlPlaneStream({
    accessToken: authSession?.tokens.accessToken ?? null,
    enabled: hasCompliantAdmin && !!authSession?.tokens.accessToken && !isTokenExpired(authSession.tokens),
    onFrame: async (frame: ControlPlaneStreamFrame<Gateway[]>) => {
      if (frame.payload) {
        startTransition(() => {
          setData((current) => ({ ...current, gateways: frame.payload ?? current.gateways }));
        });
      }

      await refreshGatewayViews();
      await refreshHealthViews();
    },
    path: CONTROL_PLANE_STREAM_PATHS.gatewayHealth,
    streamKey: "gatewayHealth",
    updateStreamState: setStreams
  });

  useControlPlaneStream({
    accessToken: authSession?.tokens.accessToken ?? null,
    enabled: hasCompliantAdmin && !!authSession?.tokens.accessToken && !isTokenExpired(authSession.tokens),
    onFrame: async () => {
      await refreshHealthViews();
    },
    path: CONTROL_PLANE_STREAM_PATHS.telemetry,
    streamKey: "telemetry",
    updateStreamState: setStreams
  });

  useControlPlaneStream({
    accessToken: authSession?.tokens.accessToken ?? null,
    enabled: hasCompliantAdmin && !!authSession?.tokens.accessToken && !isTokenExpired(authSession.tokens),
    onFrame: async (frame: ControlPlaneStreamFrame<TunnelSession[]>) => {
      if (frame.payload) {
        startTransition(() => {
          setData((current) => ({ ...current, sessions: frame.payload ?? current.sessions }));
        });
      }

      await refreshHealthViews();
    },
    path: CONTROL_PLANE_STREAM_PATHS.sessions,
    streamKey: "sessions",
    updateStreamState: setStreams
  });

  return (
    <PortalContext.Provider
      value={{
        approveDevice,
        apiBaseUrl,
        authSession,
        bootstrapError,
        bootstrapState,
        bootstrapStatus,
        cancelStepUp,
        data,
        dataError,
        dataState,
        deleteAuthProvider,
        deleteGateway,
        deletePolicy,
        deleteUser,
        disconnectDevice,
        disableUser,
        enableUser,
        enrollBootstrapMfa,
        exportAuditEvents,
        hasActiveStepUp,
        hasCompliantAdmin,
        refreshAll,
        revokeSession,
        runAuditRetention,
        signIn,
        signOut,
        stepUpPrompt,
        streams,
        submitStepUp,
        updateBootstrapPassword,
        upsertAuthProvider,
        upsertGateway,
        upsertPolicy,
        upsertUser
      }}
    >
      {children}
    </PortalContext.Provider>
  );
}

export function usePortal() {
  const context = useContext(PortalContext);
  if (!context) {
    throw new Error("usePortal must be used within a PortalProvider.");
  }

  return context;
}

function useControlPlaneStream<TPayload>({
  accessToken,
  enabled,
  onFrame,
  path,
  streamKey,
  updateStreamState
}: {
  accessToken: string | null;
  enabled: boolean;
  onFrame: (frame: ControlPlaneStreamFrame<TPayload>) => Promise<void> | void;
  path: string;
  streamKey: StreamKey;
  updateStreamState: Dispatch<SetStateAction<Record<StreamKey, StreamPresence>>>;
}) {
  const sequenceRef = useRef(0);
  const handleFrame = useEffectEvent(async (frame: ControlPlaneStreamFrame<TPayload>) => {
    sequenceRef.current = frame.sequence;
    updateStreamState((current) => ({
      ...current,
      [streamKey]: {
        error: null,
        lastEventAtUtc: frame.occurredAtUtc,
        sequence: frame.sequence,
        state: "live"
      }
    }));
    await onFrame(frame);
  });

  useEffect(() => {
    if (!enabled || !accessToken) {
      sequenceRef.current = 0;
      updateStreamState((current) => ({
        ...current,
        [streamKey]: INITIAL_STREAMS[streamKey]
      }));
      return;
    }

    let disposed = false;
    let retryHandle: number | null = null;
    let disconnect: (() => void) | null = null;

    const connect = () => {
      updateStreamState((current) => ({
        ...current,
        [streamKey]: {
          ...current[streamKey],
          error: null,
          state: current[streamKey].sequence > 0 ? "retrying" : "connecting"
        }
      }));

      disconnect = connectControlPlaneStream({
        accessToken,
        afterSequence: sequenceRef.current,
        onClose: (reason) => {
          if (disposed) {
            return;
          }

          updateStreamState((current) => ({
            ...current,
            [streamKey]: {
              ...current[streamKey],
              error: reason === "error" ? "Connection dropped." : null,
              state: "retrying"
            }
          }));

          retryHandle = window.setTimeout(connect, STREAM_RETRY_DELAY_MS);
        },
        onFrame: (frame) => {
          void handleFrame(frame as ControlPlaneStreamFrame<TPayload>);
        },
        onOpen: () => {
          updateStreamState((current) => ({
            ...current,
            [streamKey]: {
              ...current[streamKey],
              error: null,
              state: "live"
            }
          }));
        },
        path
      });
    };

    connect();

    return () => {
      disposed = true;
      if (retryHandle !== null) {
        window.clearTimeout(retryHandle);
      }
      disconnect?.();
    };
  }, [accessToken, enabled, path, streamKey, updateStreamState]);
}

function upsertById<TItem extends { id: string }>(items: TItem[], updated: TItem) {
  const index = items.findIndex((item) => item.id === updated.id);
  if (index === -1) {
    return [updated, ...items];
  }

  return items.map((item) => item.id === updated.id ? updated : item);
}

function isUnauthorized(error: unknown) {
  return error instanceof ControlPlaneApiError && error.status === 401;
}

function toDisplayError(error: unknown) {
  if (error instanceof ControlPlaneApiError) {
    return error.details?.length
      ? `${error.message} ${error.details.join(" ")}`
      : error.message;
  }

  if (error instanceof Error) {
    return error.message;
  }

  return "Request failed.";
}
