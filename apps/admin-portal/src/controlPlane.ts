import {
  CONTROL_PLANE_API_PREFIX,
  type AuthSessionResponse,
  type RefreshSessionRequest,
  type SessionTokenPair
} from "@owlprotect/contracts";

const DEFAULT_CONTROL_PLANE_PORT = "5180";
const CONTROL_PLANE_STREAM_PROTOCOL = "owlprotect.admin-stream.v1";
const CONTROL_PLANE_AUTH_PROTOCOL_PREFIX = "owlprotect.auth.";

export interface StoredAdminSession extends AuthSessionResponse {
  admin: NonNullable<AuthSessionResponse["admin"]>;
}

export interface RequestOptions {
  auth?: boolean;
  retryOnAuthFailure?: boolean;
  signal?: AbortSignal;
}

export interface StreamConnectionOptions<TPayload> {
  accessToken: string;
  afterSequence?: number;
  path: string;
  onClose: (reason: "closed" | "error") => void;
  onFrame: (frame: TPayload) => void;
  onOpen: () => void;
}

export class ControlPlaneApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly errorCode?: string,
    readonly details?: string[],
    readonly policy?: string | null
  ) {
    super(message);
    this.name = "ControlPlaneApiError";
  }
}

export function resolveControlPlaneBaseUrl() {
  const configured = import.meta.env.VITE_CONTROL_PLANE_BASE_URL;
  if (configured) {
    return configured.replace(/\/$/, "");
  }

  if (typeof window === "undefined") {
    return `http://localhost:${DEFAULT_CONTROL_PLANE_PORT}`;
  }

  const protocol = window.location.protocol === "https:" ? "https:" : "http:";
  return `${protocol}//${window.location.hostname}:${DEFAULT_CONTROL_PLANE_PORT}`;
}

export function createControlPlaneClient(
  getSession: () => StoredAdminSession | null,
  setSession: (session: StoredAdminSession | null) => void
) {
  let refreshPromise: Promise<StoredAdminSession> | null = null;

  async function refreshSession() {
    if (refreshPromise) {
      return refreshPromise;
    }

    const currentSession = getSession();
    if (!currentSession) {
      throw new ControlPlaneApiError("Authenticated session required.", 401, "authentication_required");
    }

    refreshPromise = request<AuthSessionResponse>(
      "/auth/session/refresh",
      {
        method: "POST",
        body: { refreshToken: currentSession.tokens.refreshToken } satisfies RefreshSessionRequest
      },
      { auth: false, retryOnAuthFailure: false }
    ).then((response) => {
      if (!response.admin) {
        throw new ControlPlaneApiError("Admin session refresh did not return an admin identity.", 401, "admin_role_required");
      }

      const refreshed = response as StoredAdminSession;
      setSession(refreshed);
      return refreshed;
    }).finally(() => {
      refreshPromise = null;
    });

    return refreshPromise;
  }

  async function request<TResponse>(
    path: string,
    init: {
      method?: string;
      body?: unknown;
      headers?: HeadersInit;
    } = {},
    options: RequestOptions = {}
  ): Promise<TResponse> {
    const auth = options.auth ?? true;
    const retryOnAuthFailure = options.retryOnAuthFailure ?? true;
    const session = getSession();
    const headers = new Headers(init.headers);
    headers.set("Accept", "application/json");

    if (init.body !== undefined) {
      headers.set("Content-Type", "application/json");
    }

    if (auth && session) {
      headers.set("Authorization", `Bearer ${session.tokens.accessToken}`);
    }

    const response = await fetch(buildUrl(path), {
      method: init.method ?? (init.body === undefined ? "GET" : "POST"),
      headers,
      body: init.body === undefined ? undefined : JSON.stringify(init.body),
      signal: options.signal
    });

    if (response.status === 401 && auth && retryOnAuthFailure && session?.tokens.refreshToken) {
      await refreshSession();
      return request<TResponse>(path, init, { ...options, retryOnAuthFailure: false });
    }

    if (!response.ok) {
      throw await toApiError(response);
    }

    if (response.status === 204) {
      return undefined as TResponse;
    }

    return response.json() as Promise<TResponse>;
  }

  async function revokeSession() {
    try {
      await request<void>("/auth/session/revoke", { method: "POST" }, { auth: true, retryOnAuthFailure: false });
    } catch {
      // Best effort logout.
    } finally {
      setSession(null);
    }
  }

  return {
    delete: <TResponse>(path: string, options?: RequestOptions) =>
      request<TResponse>(path, { method: "DELETE" }, options),
    get: <TResponse>(path: string, options?: RequestOptions) =>
      request<TResponse>(path, { method: "GET" }, options),
    post: <TResponse>(path: string, body?: unknown, options?: RequestOptions) =>
      request<TResponse>(path, { method: "POST", body }, options),
    revokeSession
  };
}

export function connectControlPlaneStream<TFrame>({
  accessToken,
  afterSequence,
  path,
  onClose,
  onFrame,
  onOpen
}: StreamConnectionOptions<TFrame>) {
  const url = new URL(path, toSocketBaseUrl(resolveControlPlaneBaseUrl()));
  if (afterSequence && afterSequence > 0) {
    url.searchParams.set("afterSequence", `${afterSequence}`);
  }

  const socket = new WebSocket(url, [
    CONTROL_PLANE_STREAM_PROTOCOL,
    `${CONTROL_PLANE_AUTH_PROTOCOL_PREFIX}${accessToken}`
  ]);
  socket.addEventListener("open", onOpen);
  socket.addEventListener("message", (event) => {
    onFrame(JSON.parse(event.data) as TFrame);
  });
  socket.addEventListener("close", () => onClose("closed"));
  socket.addEventListener("error", () => onClose("error"));

  return () => {
    if (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING) {
      socket.close();
    }
  };
}

export function isTokenExpired(tokens: SessionTokenPair) {
  return new Date(tokens.accessTokenExpiresAtUtc).getTime() <= Date.now();
}

export function loadStoredSession(storageKey: string) {
  if (typeof window === "undefined") {
    return null;
  }

  const rawValue = window.localStorage.getItem(storageKey);
  if (!rawValue) {
    return null;
  }

  try {
    const parsed = JSON.parse(rawValue) as StoredAdminSession;
    return parsed.admin ? parsed : null;
  } catch {
    return null;
  }
}

export function persistSession(storageKey: string, session: StoredAdminSession | null) {
  if (typeof window === "undefined") {
    return;
  }

  if (!session) {
    window.localStorage.removeItem(storageKey);
    return;
  }

  window.localStorage.setItem(storageKey, JSON.stringify(session));
}

function buildUrl(path: string) {
  const normalizedPath = path.startsWith("/") ? path : `${CONTROL_PLANE_API_PREFIX}/${path}`;
  return new URL(normalizedPath, `${resolveControlPlaneBaseUrl()}/`).toString();
}

async function toApiError(response: Response) {
  try {
    const payload = await response.json() as {
      details?: string[];
      error?: string;
      errorCode?: string;
      message?: string;
      policy?: string | null;
    };

    return new ControlPlaneApiError(
      payload.error ?? payload.message ?? response.statusText,
      response.status,
      payload.errorCode,
      payload.details,
      payload.policy
    );
  } catch {
    return new ControlPlaneApiError(response.statusText || "Request failed.", response.status);
  }
}

function toSocketBaseUrl(baseUrl: string) {
  const url = new URL(baseUrl);
  url.protocol = url.protocol === "https:" ? "wss:" : "ws:";
  return url.toString();
}
