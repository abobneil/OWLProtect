# Control Plane Persistence and Migration Plan

This document translates the seeded in-memory model into a relational design, repository boundaries, and migration expectations for the control plane.

## Repository Boundaries

- `IAdminRepository`: bootstrap admin account state and operator role records
- `IUserRepository`: user identities, group links, and enable/disable lifecycle
- `IDeviceRepository`: device registration, posture status, and map/diagnostic identity
- `IGatewayRepository`: gateway inventory, heartbeat state, and pool membership reads
- `IPolicyRepository`: policy definitions and resolved assignment inputs
- `ISessionRepository`: admin, user, and client sessions with revocation support
- `IAlertRepository`: operator-visible alerts and retention boundaries
- `IAuditRepository`: append-only audit history for sensitive and operator actions
- `IAuthProviderConfigRepository`: Entra and OIDC provider configuration

The current in-memory implementation is a transitional adapter behind these boundaries. The PostgreSQL-backed implementation should replace it without changing the API handler intent.

## Aggregate and Table Plan

### Identity and Access

- `admins`
- `users`
- `groups`
- `user_groups`
- `auth_providers`
- `admin_sessions`
- `user_sessions`
- `client_sessions`

### Device and Policy

- `devices`
- `device_posture_reports`
- `policies`
- `policy_targets`
- `policy_routes`
- `policy_dns_zones`
- `policy_ports`

### Gateway and Runtime

- `gateway_pools`
- `gateways`
- `gateway_pool_members`
- `health_samples`
- `alerts`

### Audit and Operations

- `audit_events`
- `schema_migrations`

## Migration Strategy

- Use append-only numbered SQL migrations for the first persistence phase.
- Each migration should be safe to run exactly once and recorded in `schema_migrations`.
- Schema changes that rewrite large tables should be split into additive and cleanup phases when possible.
- Application startup must fail fast when required migrations have not been applied in the target environment.

## Startup and Deployment Expectations

- Local development may run migrations automatically against disposable environments.
- Shared or production-like environments should run migrations as an explicit deployment step before new application instances receive traffic.
- Gateway and scheduler deployments should assume the control-plane schema is already current.
- Rollback plans must consider both binary rollback and compatible schema state.
- When `Persistence__RedisConnectionString` is configured, the control plane uses Redis as the hot-path cache for active platform session state while PostgreSQL remains the durable source of truth.

## Auth Provider Validation Expectations

- `auth_providers.issuer` must be the provider's OpenID Connect issuer URI and must publish a reachable `/.well-known/openid-configuration` document.
- `auth_providers.client_id` is the expected token audience for provider login validation.
- `auth_providers.username_claim_paths` defines the preferred claim lookup order for the OWLProtect `user.username` field.
- `auth_providers.group_claim_paths` defines which token claims should be flattened into `user.group_ids`.
- `auth_providers.mfa_claim_paths` identifies claim names such as `amr` or `acr` that the control plane inspects for upstream MFA evidence.
- `auth_providers.require_mfa` blocks provider-backed login when the configured MFA evidence is missing.
- Successful `/api/v1/auth/provider/login` requests now provision or update a local OWLProtect user record and issue a first-party user platform session.
- Seeded provider rows remain scaffolding only; provider-token validation will fail until operators replace the placeholder issuer and client values with real provider metadata.

## Near-Term Transition Plan

1. Introduce repository interfaces and keep the in-memory adapter behind them.
2. Add the initial relational schema and migration assets.
3. Implement PostgreSQL-backed repositories for read and write paths.
4. Move ephemeral session and revocation coordination into Redis where appropriate.
5. Remove seeded in-memory state from default startup once persistence is feature-complete.
