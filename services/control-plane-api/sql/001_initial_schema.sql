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

CREATE TABLE IF NOT EXISTS auth_providers (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    type TEXT NOT NULL,
    issuer TEXT NOT NULL,
    client_id TEXT NOT NULL,
    mfa_claim_paths TEXT NOT NULL,
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
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS groups (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS user_groups (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    group_id TEXT NOT NULL REFERENCES groups(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, group_id)
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

CREATE TABLE IF NOT EXISTS gateway_pool_members (
    gateway_pool_id TEXT NOT NULL REFERENCES gateway_pools(id) ON DELETE CASCADE,
    gateway_id TEXT NOT NULL REFERENCES gateways(id) ON DELETE CASCADE,
    PRIMARY KEY (gateway_pool_id, gateway_id)
);

CREATE TABLE IF NOT EXISTS policies (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL UNIQUE,
    mode TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS policy_routes (
    policy_id TEXT NOT NULL REFERENCES policies(id) ON DELETE CASCADE,
    cidr TEXT NOT NULL,
    PRIMARY KEY (policy_id, cidr)
);

CREATE TABLE IF NOT EXISTS policy_dns_zones (
    policy_id TEXT NOT NULL REFERENCES policies(id) ON DELETE CASCADE,
    dns_zone TEXT NOT NULL,
    PRIMARY KEY (policy_id, dns_zone)
);

CREATE TABLE IF NOT EXISTS policy_ports (
    policy_id TEXT NOT NULL REFERENCES policies(id) ON DELETE CASCADE,
    port INTEGER NOT NULL,
    PRIMARY KEY (policy_id, port)
);

CREATE TABLE IF NOT EXISTS policy_targets (
    policy_id TEXT NOT NULL REFERENCES policies(id) ON DELETE CASCADE,
    target_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    PRIMARY KEY (policy_id, target_type, target_id)
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
