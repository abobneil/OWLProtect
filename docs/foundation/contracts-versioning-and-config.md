# Contracts, Versioning, and Configuration Conventions

This document defines the repository-level conventions for shared contracts, external API versioning, and environment configuration. It is intended to keep the TypeScript workspaces, .NET services, Docker-based local development, and future self-hosted deployments aligned.

## Shared Contracts

- `packages/contracts` is the source of truth for TypeScript contracts used by the admin portal and any future TypeScript clients or tooling.
- `packages/contracts` also exports the current control-plane API and WebSocket prefixes so TypeScript consumers can follow the same versioned route contract.
- The .NET services keep explicit C# models and DTOs instead of generated code. Producers and consumers must map at the API boundary rather than sharing runtime-specific implementation types across languages.
- Any HTTP or WebSocket shape consumed by the admin portal should be added to `packages/contracts` before or alongside the service change that produces it.
- Breaking contract changes must update all producers and consumers in the same pull request until a separately published contract package is introduced.
- Seed data may live in `packages/contracts` for scaffold and demo flows, but persistence models and database schema should evolve independently inside the services layer.

## API Versioning

- Public control-plane endpoints should version by path using `/api/v1/...`.
- Unversioned infrastructure endpoints such as `/health` may remain unversioned for liveness and container orchestration.
- Additive response changes are allowed within `v1` when existing fields keep their meaning and compatibility.
- Breaking route, field, or semantics changes require a new version path rather than silent mutation of `v1`.
- Control-plane HTTP and WebSocket application surfaces now live under `/api/v1`, including `/api/v1/ws/...` for streaming endpoints.

## Environment Configuration Layout

There are two configuration layers in this repository:

1. Root deployment inputs for Docker Compose use the `OWLP_` prefix in `.env`.
2. Application runtime configuration uses framework-native keys inside each container.

### Compose Input Convention

- Root `.env` files use `OWLP_<SUBSYSTEM>_<SETTING>` names.
- These variables are deployment-facing inputs for local and self-hosted environments.
- `.env.example` documents the supported root inputs and their defaults.
- Secrets and deployment-specific overrides belong in untracked `.env` files or the deployment platform secret store.

### Runtime Key Convention

- ASP.NET Core services use hierarchical section keys with double underscores in environment variables, for example `ControlPlane__BaseUrl` and `Gateway__Region`.
- Browser-exposed frontend build variables must use the `VITE_` prefix when introduced.
- New .NET settings should follow `<Section>__<Setting>` and group related keys under a stable top-level section instead of using ad hoc flat names.

## Current Compose-to-Runtime Mapping

| Root input | Runtime key | Used by |
| --- | --- | --- |
| `OWLP_CONTROL_PLANE_URLS` | `ASPNETCORE_URLS` | `control-plane-api` |
| `OWLP_CONTROL_PLANE_BASE_URL` | `ControlPlane__BaseUrl` | `gateway`, `scheduler` |
| `OWLP_REDIS_CONNECTION_STRING` | `Persistence__RedisConnectionString` | `control-plane-api` |
| `OWLP_BOOTSTRAP_ADMIN_USERNAME` | `SecretManagement__BootstrapAdminUsername` | `control-plane-api` |
| `OWLP_BOOTSTRAP_ADMIN_PASSWORD` | `SecretManagement__BootstrapAdminPassword` | `control-plane-api` |
| `OWLP_BOOTSTRAP_ADMIN_PASSWORD_FILE` | `SecretManagement__BootstrapAdminPasswordFile` | `control-plane-api` |
| `OWLP_BOOTSTRAP_ADMIN_PASSWORD_HASH` | `SecretManagement__BootstrapAdminPasswordHash` | `control-plane-api` |
| `OWLP_ALLOW_GENERATED_BOOTSTRAP_ADMIN_PASSWORD` | `SecretManagement__AllowGeneratedBootstrapAdminPassword` | `control-plane-api` |
| `OWLP_AUDIT_RETENTION_ENABLED` | `AuditRetention__Enabled` | `control-plane-api` |
| `OWLP_AUDIT_RETENTION_DAYS` | `AuditRetention__RetentionDays` | `control-plane-api` |
| `OWLP_AUDIT_EXPORT_BATCH_SIZE` | `AuditRetention__ExportBatchSize` | `control-plane-api` |
| `OWLP_AUDIT_RETENTION_CHECK_INTERVAL_HOURS` | `AuditRetention__CheckIntervalHours` | `control-plane-api` |
| `OWLP_AUDIT_EXPORT_DIRECTORY` | `AuditRetention__ExportDirectory` | `control-plane-api` |
| `OWLP_GATEWAY_ID` | `Gateway__Id` | `gateway` |
| `OWLP_GATEWAY_NAME` | `Gateway__Name` | `gateway` |
| `OWLP_GATEWAY_REGION` | `Gateway__Region` | `gateway` |

`OWLP_SECRET_MOUNT_PATH` is a deployment-only Docker Compose input used to mount file-backed secrets into `/run/owlprotect-secrets`.

The PostgreSQL, Redis, and admin portal port variables are deployment inputs only and are consumed by Docker Compose directly.

## Change Rules

- Add new root deployment inputs to `.env.example` and document them here.
- Do not commit environment-specific secrets or production values.
- When adding a new public API surface, define the transport shape in `packages/contracts` and place the route under the current versioned prefix.
- When changing an existing public contract, document whether the change is additive or breaking in the pull request.
- Run `npm run validate:foundation` after touching repository governance files, environment examples, or foundation documentation so CI and the local examples stay aligned.
