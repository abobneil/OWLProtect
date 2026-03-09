# OWLProtect

OWLProtect is a Windows-first enterprise VPN platform with:

- a React admin portal
- an ASP.NET Core control plane, gateway service, and scheduler
- a native Windows client UI and service
- shared contracts and design tokens

## Workspace layout

- `apps/admin-portal`: React admin portal
- `packages/contracts`: shared TypeScript contracts and transport helpers
- `packages/theme`: shared design tokens and theme helpers
- `services/control-plane-api`: ASP.NET Core control plane API
- `services/gateway`: ASP.NET Core gateway service
- `services/scheduler`: ASP.NET Core background scheduler
- `windows/windows-client-service`: Windows service and named-pipe host
- `windows/windows-client-ui`: WinUI 3 client shell

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for local validation commands, pull request expectations, CODEOWNERS review guidance, and the backlog label taxonomy.

Repository-managed git hooks under `.githooks` run a secret scan before commit and push after `npm ci` or `npm run setup:hooks`.

See [docs/foundation/contracts-versioning-and-config.md](docs/foundation/contracts-versioning-and-config.md) for the current shared contract, API versioning, and environment configuration conventions.

See [docs/security/secret-management-and-rotation.md](docs/security/secret-management-and-rotation.md) for the current bootstrap secret-loading and rotation guidance.

See [docs/security/audit-retention-and-export.md](docs/security/audit-retention-and-export.md) for the current audit durability, retention, and export guidance.

See [docs/operations/observability-and-slos.md](docs/operations/observability-and-slos.md) for service health endpoints, metrics, traces, alert rules, redaction policy, dashboards, SLOs, and performance baselines.

See [docs/operations/backup-restore-and-upgrades.md](docs/operations/backup-restore-and-upgrades.md) for the supported backup, restore, migration, and upgrade validation workflows.

See [docs/operations/self-hosted-runbooks.md](docs/operations/self-hosted-runbooks.md) for self-hosted deployment promotion, incident response, and support escalation runbooks.

See [docs/operations/release-readiness.md](docs/operations/release-readiness.md) and [docs/operations/release-checklist.md](docs/operations/release-checklist.md) for release gates, packaging outputs, provenance verification, and ship-approval evidence.

See [docs/architecture/overview.md](docs/architecture/overview.md) for the current service-boundary overview and [docs/adr](docs/adr) for the accepted architecture decisions.

See [docs/windows/windows-client.md](docs/windows/windows-client.md) for the current Windows client IPC, auth configuration, posture collection, and offline recovery behavior.

See [.env.local.example](.env.local.example) and [.env.selfhosted.example](.env.selfhosted.example) for local and self-hosted deployment input examples.
