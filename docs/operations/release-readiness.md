# Release Readiness

This document defines the release gate for OWLProtect container and Windows client deliverables.

## Automated Gates

GitHub Actions now enforces the release-readiness workflow in `.github/workflows/release-readiness.yml`.

The workflow covers:

- TypeScript type-check and build validation.
- .NET service build validation.
- Dependency and filesystem vulnerability scanning.
- Docker-based upgrade validation against `.env.local.example`.
- API smoke tests for auth, policy, diagnostics, and privileged step-up flows.
- Container packaging and Windows client bundle packaging.
- SBOM generation for packaged artifacts.
- Release checksum signing with Cosign keyless signing on non-PR runs.

## Local Commands

Run the same release checks locally when preparing a candidate:

```powershell
npm ci
npm run typecheck
npm run build
npm run validate:foundation
npm run validate:services
npm run validate:security
pwsh -File ./scripts/validate-upgrade.ps1 -EnvFile ./.env.local.example -TakeBackup:$false
npm run validate:release-smoke
npm run rehearse:recovery
npm run package:containers
npm run package:windows
```

The smoke and recovery rehearsal commands mutate state and should run against a disposable environment.

## Packaging Outputs

Container packaging uses `scripts/package-containers.ps1` and emits:

- one OCI archive per service under `artifacts/containers`
- `artifacts/containers/manifest.json`
- `artifacts/containers/SHA256SUMS`

Windows packaging uses `scripts/package-windows-client.ps1` and emits:

- `artifacts/windows-client/OWLProtect-windows-client-<version>.zip`
- `artifacts/windows-client/manifest.json`
- `artifacts/windows-client/SHA256SUMS`

The Windows client bundle contains:

- published Windows service output
- published WinUI client output
- `scripts/install.ps1` for service installation or update
- `scripts/uninstall.ps1` for bundle removal

## Provenance Verification

The provenance job uploads:

- CycloneDX SBOMs under `artifacts/sbom`
- a release checksum manifest at `artifacts/release/SHA256SUMS`
- `SHA256SUMS.sig` and `SHA256SUMS.pem` on push and workflow-dispatch runs

Operators should verify a candidate by:

1. Download the packaged artifacts plus the provenance bundle.
2. Run `sha256sum -c SHA256SUMS` from the provenance bundle directory.
3. Verify the checksum signature:

```bash
cosign verify-blob \
  --certificate SHA256SUMS.pem \
  --signature SHA256SUMS.sig \
  SHA256SUMS
```

4. Review the SBOM entries for the container tarball or Windows bundle being promoted.

## Release Evidence

Every candidate must retain evidence for:

- the successful `release-readiness` workflow run
- the latest `recovery-rehearsal-report.json`
- the packaged artifact manifests and checksum files
- the exact git commit, image tags, and Windows bundle version being promoted
