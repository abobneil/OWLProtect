# Audit Retention and Export

This document defines the current P0 expectations for control-plane audit durability, retention, and export.

## Design

- Sensitive control-plane actions append audit records with a monotonic `sequence`, `previousEventHash`, and `eventHash`.
- The chain is append-only during normal operation. PostgreSQL blocks direct audit row updates and deletes unless the application explicitly enters controlled audit maintenance mode.
- The control plane exports aged audit records to JSON before pruning them from the live table.
- Each prune writes a durable retention checkpoint that records the exported path, the cutoff used, and the last sequence and hash removed.

## Export

- Operators can query current audit records at `GET /audit`.
- Operators can export a bounded batch at `GET /audit/export?before=<utc>&limit=<n>`.
- Automated retention exports are written to `AuditRetention__ExportDirectory` as JSON envelopes named `audit-export-<first-sequence>-<last-sequence>-<timestamp>.json`.
- Export files are written to a temporary path first and then moved into place to avoid partial files being mistaken for complete exports.

## Retention

- `AuditRetention__Enabled` controls whether automated retention runs.
- `AuditRetention__RetentionDays` defines how old audit records must be before export and pruning are allowed.
- `AuditRetention__ExportBatchSize` caps each retention batch so large histories are exported in deterministic slices.
- `AuditRetention__CheckIntervalHours` controls how often the control plane checks for eligible audit data.
- Operators can trigger an immediate privileged retention cycle with `POST /audit/retention/run`.

## Operational expectations

- Use a persistent volume or durable host path for `AuditRetention__ExportDirectory` in self-hosted environments.
- Treat exported audit files as security records. Back them up or ship them to the organization log archive before deleting them.
- Review `GET /audit/checkpoints` to confirm what has been exported and pruned from the hot store.
