# Self-Hosted Runbooks

This document is the operator playbook for deployment promotion, incident handling, and support escalation.

## Environment Promotion

Promotion stages:

1. Local:
   - use `.env.local.example`
   - allow seeded data and disposable state
2. Staging:
   - use `.env.selfhosted.example` as the baseline
   - disable `OWLP_PERSISTENCE_SEED_ON_STARTUP`
   - validate backup, restore, and upgrade scripts before promotion
3. Production-like:
   - require a fresh backup
   - require successful `/health/ready` and `/metrics` checks for all three services
   - require alert-rule loading and dashboard verification

## Deployment Runbook

1. Pull the target branch or tag and confirm the expected git commit.
2. Review changed environment inputs and secrets.
3. Run `./scripts/backup-selfhosted.ps1`.
4. Run `./scripts/validate-upgrade.ps1`.
5. Review the current [release checklist](/C:/Users/nchester/Documents/GitHub/OWLProtect/docs/operations/release-checklist.md) and attach the latest recovery-rehearsal evidence for the candidate.
6. Verify:
   - control plane `/health/ready`
   - gateway `/health/ready`
   - scheduler `/health/ready`
   - control plane `/api/v1/operations/diagnostics`
   - gateway `/diagnostics`
   - scheduler `/diagnostics`
7. Confirm Prometheus is scraping all `/metrics` endpoints.

## Common Incidents

### Auth Failures Rising

- Check `owlprotect_auth_attempts_total` by `flow` and `outcome`.
- Review control-plane logs filtered by `CorrelationId` and session correlation ID.
- Validate identity-provider reachability and current auth-provider configuration.
- If only provider login fails, keep local admin access available for remediation.

### Gateway Heartbeats Missing

- Check gateway `/health/ready` and `/diagnostics`.
- Confirm the trust bundle file exists at `OWLP_GATEWAY_TRUST_BUNDLE_FILE`.
- Confirm the control-plane `/api/v1/gateways` view still shows recent heartbeats.
- If the gateway cannot recover quickly, remove it from service and redeploy with preserved gateway state.

### Scheduler Not Ready

- Check scheduler `/health/ready` and `/diagnostics`.
- Inspect connectivity from scheduler to control plane.
- Confirm the control plane still authorizes privileged admin operations required for seeded-user cleanup.

### Storage Pressure

- Check PostgreSQL volume usage and `OWLP_AUDIT_EXPORT_DIRECTORY`.
- Export and archive older audit retention bundles if policy allows.
- Increase storage before the volume drops below the alert threshold.

## Support Escalation Evidence

Collect this before escalating:

- current git commit and compose revision
- `.env` values with secrets redacted
- `/health/ready` payloads for control plane, gateway, and scheduler
- `/diagnostics` payloads for gateway and scheduler
- `/api/v1/operations/diagnostics` payload for the control plane
- relevant log lines grouped by `X-Correlation-ID` and session correlation ID
- timestamps of the last successful backup and last successful upgrade validation

## Escalation Thresholds

- Escalate immediately if a restore is required and the last successful backup is unknown.
- Escalate after 15 minutes for a production-like auth outage with no working admin path.
- Escalate after 10 minutes for simultaneous control-plane and gateway readiness failures.
- Escalate after any incident that spends more than 25 percent of the weekly error budget.
