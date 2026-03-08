# Backup, Restore, and Upgrades

This document defines the supported operational workflows for stateful self-hosted OWLProtect deployments.

## Authoritative State

- PostgreSQL is the durable source of truth for control-plane entities, sessions, audit checkpoints, and trust material metadata.
- Redis is a hot-path cache for platform sessions and can be rebuilt from PostgreSQL after restore.
- `OWLP_AUDIT_EXPORT_DIRECTORY` contains retained audit export envelopes and must be preserved.
- `OWLP_GATEWAY_STATE_PATH` contains the gateway trust-bundle file and must be preserved.

## Backup Workflow

Use [scripts/backup-selfhosted.ps1](/C:/Users/nchester/Documents/GitHub/OWLProtect/scripts/backup-selfhosted.ps1).

The backup script:

- creates a timestamped backup directory
- runs `pg_dump --clean --if-exists` inside the PostgreSQL container
- copies the host-backed audit export directory
- copies the host-backed gateway state directory
- writes `metadata.json` with the timestamp and current git commit

Example:

```powershell
./scripts/backup-selfhosted.ps1 -EnvFile .env -OutputDirectory ./backups
```

## Restore Workflow

Use [scripts/restore-selfhosted.ps1](/C:/Users/nchester/Documents/GitHub/OWLProtect/scripts/restore-selfhosted.ps1).

The restore script:

- stops OWLProtect application containers
- restores `postgres.sql` into the running PostgreSQL container
- replaces the host-backed audit export and gateway state directories from the selected backup
- starts the OWLProtect services again

Example:

```powershell
./scripts/restore-selfhosted.ps1 -EnvFile .env -BackupPath ./backups/owlprotect-backup-20260308-220000.zip
```

## Upgrade Validation Workflow

Use [scripts/validate-upgrade.ps1](/C:/Users/nchester/Documents/GitHub/OWLProtect/scripts/validate-upgrade.ps1).

The upgrade validation script:

- optionally takes a fresh backup before deployment
- rebuilds and restarts the compose services
- waits for `/health/ready` on the control plane, gateway, and scheduler
- verifies `/metrics` on each service

Example:

```powershell
./scripts/validate-upgrade.ps1 -EnvFile .env
```

## Rollback and Forward Fix

- Always take a fresh backup immediately before a production-like upgrade.
- If the new release fails readiness checks, stop rollout and inspect container logs plus `/health/ready` payloads before changing state.
- If the failure is binary-only and the schema remains compatible, redeploy the previous images and rerun readiness validation.
- If the failure changed persistent state incompatibly, restore the last known-good backup and then redeploy the last known-good images.
- If restore would exceed the recovery objective or data drift is limited, prefer a forward fix that preserves the latest durable data.

## Release Gate

Before promotion to a shared or production-like environment, the operator must prove:

- backup script completed successfully within the last 24 hours
- restore has been rehearsed against a disposable environment within the current release wave
- upgrade validation passed against the candidate images and compose inputs
- readiness, metrics, and alerting endpoints scrape correctly after upgrade
