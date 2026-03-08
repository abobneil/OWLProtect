# ADR 0002: The control plane issues platform sessions after external token validation

- Status: Accepted
- Date: 2026-03-08
- Deciders: Repository maintainers
- Technical Story: Define the trust boundary between external identity providers and OWLProtect runtime components.

## Context

The platform accepts identity from Entra ID and generic OIDC providers, but downstream services and clients need a stable session model owned by OWLProtect. Gateways and Windows clients should not directly trust arbitrary external tokens because policy enforcement, revocation, role checks, and audit must remain centralized.

## Decision

Validate external tokens in the control plane, then issue OWLProtect-managed sessions for admins, users, and clients. Use those platform sessions for downstream authorization, revocation, and policy delivery. External provider claims remain inputs to session issuance, not long-lived authorization artifacts propagated throughout the platform.

## Consequences

- Positive:
  - Centralized revocation, audit, and role enforcement.
  - Stable downstream trust model across Entra ID and generic OIDC.
  - Clear separation between provider validation and platform authorization.
- Negative:
  - Requires a session store and token lifecycle implementation in the control plane.
  - Adds a translation layer from provider claims to platform identity.
- Follow-up work:
  - Implement token validation, session issuance, and refresh flows.
  - Add RBAC and privileged-action checks to the control plane.
  - Define client and gateway trust material rotation.

## Current Implementation Notes

- Admin and end-user platform sessions are issued directly by the control plane.
- Client platform sessions are issued as device-bound sessions after an authenticated end-user session exchanges for a managed device.
- This user-to-client exchange is a transitional trust bridge until device-specific trust material and rotation workflows are implemented.
