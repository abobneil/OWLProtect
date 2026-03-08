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
    username_claim_paths TEXT[] NOT NULL DEFAULT '{}',
    group_claim_paths TEXT[] NOT NULL DEFAULT '{}',
    mfa_claim_paths TEXT[] NOT NULL DEFAULT '{}',
    require_mfa BOOLEAN NOT NULL DEFAULT TRUE,
    silent_sso_enabled BOOLEAN NOT NULL DEFAULT FALSE,
    created_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE auth_providers ADD COLUMN IF NOT EXISTS username_claim_paths TEXT[] NOT NULL DEFAULT '{}';
ALTER TABLE auth_providers ADD COLUMN IF NOT EXISTS group_claim_paths TEXT[] NOT NULL DEFAULT '{}';
ALTER TABLE auth_providers ADD COLUMN IF NOT EXISTS require_mfa BOOLEAN NOT NULL DEFAULT TRUE;

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
    sequence_number BIGINT NULL,
    actor TEXT NOT NULL,
    action TEXT NOT NULL,
    target_type TEXT NOT NULL,
    target_id TEXT NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    outcome TEXT NOT NULL,
    detail TEXT NOT NULL,
    previous_event_hash TEXT NULL,
    event_hash TEXT NULL
);

ALTER TABLE audit_events ADD COLUMN IF NOT EXISTS sequence_number BIGINT NULL;
ALTER TABLE audit_events ADD COLUMN IF NOT EXISTS previous_event_hash TEXT NULL;
ALTER TABLE audit_events ADD COLUMN IF NOT EXISTS event_hash TEXT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ix_audit_events_sequence_number
    ON audit_events (sequence_number)
    WHERE sequence_number IS NOT NULL;

CREATE TABLE IF NOT EXISTS audit_retention_checkpoints (
    id TEXT PRIMARY KEY,
    cutoff_utc TIMESTAMPTZ NOT NULL,
    exported_at_utc TIMESTAMPTZ NOT NULL,
    export_path TEXT NOT NULL,
    removed_through_sequence BIGINT NOT NULL,
    removed_through_created_at_utc TIMESTAMPTZ NOT NULL,
    removed_through_event_hash TEXT NOT NULL,
    exported_event_count INTEGER NOT NULL
);

CREATE OR REPLACE FUNCTION owlprotect_allow_audit_maintenance()
RETURNS BOOLEAN AS $$
BEGIN
    RETURN current_setting('owlprotect.allow_audit_maintenance', true) = 'on';
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION owlprotect_block_audit_event_mutation()
RETURNS TRIGGER AS $$
BEGIN
    IF owlprotect_allow_audit_maintenance() THEN
        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    RAISE EXCEPTION 'audit_events is append-only outside controlled maintenance';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tr_audit_events_block_update ON audit_events;
CREATE TRIGGER tr_audit_events_block_update
    BEFORE UPDATE ON audit_events
    FOR EACH ROW
    EXECUTE FUNCTION owlprotect_block_audit_event_mutation();

DROP TRIGGER IF EXISTS tr_audit_events_block_delete ON audit_events;
CREATE TRIGGER tr_audit_events_block_delete
    BEFORE DELETE ON audit_events
    FOR EACH ROW
    EXECUTE FUNCTION owlprotect_block_audit_event_mutation();
