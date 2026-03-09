# Release Checklist

Use this checklist before shipping the first supported self-hosted release and for every subsequent candidate.

## Ship Gate

1. Confirm the candidate commit or tag is final and reproducible.
2. Confirm `.github/workflows/release-readiness.yml` passed on the candidate commit.
3. Confirm `artifacts/containers/manifest.json` and `artifacts/windows-client/manifest.json` were produced from the candidate commit.
4. Confirm the provenance bundle contains SBOMs plus a signed `SHA256SUMS` manifest.
5. Confirm the self-hosted deployment inputs match the intended environment and all placeholder secrets were replaced.

## Recovery Evidence

1. Run `npm run rehearse:recovery` against a disposable environment on the candidate release wave.
2. Attach the resulting `artifacts/recovery-rehearsal/recovery-rehearsal-report.json` to the release record.
3. Confirm the evidence shows:
   - original bootstrap-admin credentials were restored successfully
   - the seeded test user returned to the disabled state after restore
   - post-backup filesystem markers were removed by restore
4. Confirm a fresh production-like backup exists within the last 24 hours before final promotion.

## Rollback Expectations

- Binary-only failure with compatible schema:
  redeploy the previous container archives, rerun readiness checks, and keep the latest durable data.
- Schema or persistent-state incompatibility:
  restore the last known-good backup, redeploy the previous candidate, and revalidate `/health/ready` plus `/metrics`.
- Windows client rollout regression:
  redeploy the previous Windows client bundle with `scripts/install.ps1` and remove the rejected build with `scripts/uninstall.ps1` if necessary.

## Approval Record

Record the following in the release ticket or change log:

- candidate commit SHA
- workflow run URL
- backup timestamp
- recovery rehearsal evidence path
- container manifest path
- Windows bundle manifest path
- provenance bundle path
- operator sign-off
