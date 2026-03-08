# OWLProtect Label Taxonomy

Use the GitHub labels in this repository to keep the backlog queryable and consistent across roadmap waves.

## Area Labels

- `area:foundation`: Repository foundation, tooling, and governance
- `area:control-plane`: Control plane APIs, persistence, and streaming
- `area:identity`: Identity providers, MFA, and auth flows
- `area:admin-portal`: Admin portal UI and workflows
- `area:gateways`: Gateway orchestration, failover, and diagnostics
- `area:windows-client`: Windows client service, UI, and IPC
- `area:policy-engine`: Device lifecycle, posture, and policy resolution
- `area:operations`: Observability, packaging, and operator workflows
- `area:security`: Security hardening, PKI, and trust boundaries

## Kind Labels

- `kind:defect`: Defect, regression, or broken behavior
- `kind:architecture`: Architecture or platform design work
- `kind:epic`: Cross-cutting epic issue
- `kind:feature`: Feature delivery work item
- `kind:docs`: Documentation or guidance
- `kind:security`: Security-sensitive implementation work
- `kind:test`: Validation, QA, or test coverage
- `kind:operations`: Operational readiness or runtime support work

## Priority Labels

- `priority:p0`: Critical path work
- `priority:p1`: Important but not first-critical-path
- `priority:p2`: Later-phase work

## Status Labels

- `status:blocked`: Blocked on another decision or dependency
- `status:needs-decision`: Needs a product or architecture decision

Use `status:blocked` when implementation is waiting on another issue, external system, or dependency. Use `status:needs-decision` when the work is understood but product or technical direction is unresolved.

## Wave Labels

- `wave:1-foundation-auth`: Foundation, persistence, and auth boundaries
- `wave:2-core-platform`: Core platform, policy, and integration seams
- `wave:3-product-workflows`: User-facing product workflows and diagnostics
- `wave:4-hardening-release`: Hardening, operations, and release readiness

## Triage Guidance

- Every issue should have one primary `area:*` label.
- Every issue should have one `kind:*` label.
- Every issue should have one `priority:*` label.
- Use at most one `wave:*` label.
- Add a `status:*` label only when the issue is blocked or needs a decision.
- Use `kind:security` for security-sensitive implementation work, but do not file private vulnerabilities in a public issue. Use GitHub Security Advisories for sensitive reports.

## Intake Labels

GitHub's default intake labels can still be useful for quick filtering:

- `bug`: Fast visual signal for defects, usually paired with `kind:defect`
- `enhancement`: Fast visual signal for new features or improvements
- `documentation`: Fast visual signal for docs-only work
