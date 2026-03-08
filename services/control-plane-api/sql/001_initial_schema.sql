CREATE TABLE IF NOT EXISTS schema_migrations (
    id TEXT PRIMARY KEY,
    applied_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS admins (
    id TEXT PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    role TEXT NOT NULL,
    must_change_password BOOLEAN NOT NULL DEFAULT TRUE,
    mfa_enrolled BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS platform_sessions (
    id TEXT PRIMARY KEY,
    kind TEXT NOT NULL,
    subject_id TEXT NOT NULL,
    subject_name TEXT NOT NULL,
    role TEXT NULL,
    access_token_hash TEXT NOT NULL,
    refresh_token_hash TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    access_token_expires_at_utc TIMESTAMPTZ NOT NULL,
    refresh_token_expires_at_utc TIMESTAMPTZ NOT NULL,
    last_authenticated_at_utc TIMESTAMPTZ NOT NULL,
    step_up_expires_at_utc TIMESTAMPTZ NULL,
    revoked_at_utc TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS auth_providers (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    type TEXT NOT NULL,
    issuer TEXT NOT NULL,
    client_id TEXT NOT NULL,
    mfa_claim_paths TEXT[] NOT NULL DEFAULT '{}',
    silent_sso_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS users (
    id TEXT PRIMARY KEY,
    username TEXT NOT NULL UNIQUE,
    display_name TEXT NOT NULL,
    enabled BOOLEAN NOT NULL DEFAULT TRUE,
    test_account BOOLEAN NOT NULL DEFAULT FALSE,
    provider_type TEXT NOT NULL,
    group_ids TEXT[] NOT NULL DEFAULT '{}',
    policy_ids TEXT[] NOT NULL DEFAULT '{}',
    enabled_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS devices (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name TEXT NOT NULL,
    city TEXT NOT NULL,
    country TEXT NOT NULL,
    public_ip TEXT NOT NULL,
    managed BOOLEAN NOT NULL DEFAULT FALSE,
    compliant BOOLEAN NOT NULL DEFAULT FALSE,
    posture_score INTEGER NOT NULL,
    connection_state TEXT NOT NULL,
    last_seen_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS gateway_pools (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    regions TEXT[] NOT NULL DEFAULT '{}',
    gateway_ids TEXT[] NOT NULL DEFAULT '{}',
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS gateways (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    region TEXT NOT NULL,
    health TEXT NOT NULL,
    load_percent INTEGER NOT NULL,
    peer_count INTEGER NOT NULL,
    cpu_percent INTEGER NOT NULL,
    memory_percent INTEGER NOT NULL,
    latency_ms INTEGER NOT NULL,
    last_heartbeat_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS policies (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    cidrs TEXT[] NOT NULL DEFAULT '{}',
    dns_zones TEXT[] NOT NULL DEFAULT '{}',
    ports INTEGER[] NOT NULL DEFAULT '{}',
    mode TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS user_sessions (
    id TEXT PRIMARY KEY,
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    gateway_id TEXT NOT NULL REFERENCES gateways(id) ON DELETE CASCADE,
    connected_at_utc TIMESTAMPTZ NOT NULL,
    handshake_age_seconds INTEGER NOT NULL,
    throughput_mbps INTEGER NOT NULL,
    revoked_at_utc TIMESTAMPTZ NULL
);

CREATE TABLE IF NOT EXISTS health_samples (
    id TEXT PRIMARY KEY,
    device_id TEXT NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    state TEXT NOT NULL,
    severity TEXT NOT NULL,
    latency_ms INTEGER NOT NULL,
    jitter_ms INTEGER NOT NULL,
    packet_loss_percent NUMERIC(5,2) NOT NULL,
    throughput_mbps INTEGER NOT NULL,
    signal_strength_percent INTEGER NOT NULL,
    dns_reachable BOOLEAN NOT NULL,
    route_healthy BOOLEAN NOT NULL,
    sampled_at_utc TIMESTAMPTZ NOT NULL,
    message TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS alerts (
    id TEXT PRIMARY KEY,
    severity TEXT NOT NULL,
    title TEXT NOT NULL,
    description TEXT NOT NULL,
    target_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS audit_events (
    id TEXT PRIMARY KEY,
    actor TEXT NOT NULL,
    action TEXT NOT NULL,
    target_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    outcome TEXT NOT NULL,
    detail TEXT NOT NULL
);
