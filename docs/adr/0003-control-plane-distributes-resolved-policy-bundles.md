# ADR 0003: The control plane distributes resolved policy bundles to clients and gateways

- Status: Accepted
- Date: 2026-03-08
- Deciders: Repository maintainers
- Technical Story: Define where policy evaluation happens and how policy reaches runtime components.

## Context

Policy inputs will eventually include users, groups, devices, posture, routes, DNS scopes, and gateway placement. Re-implementing policy resolution independently in the admin portal, gateways, and Windows clients would create drift and inconsistent enforcement.

## Decision

Perform policy resolution in the control plane and distribute resolved policy bundles to gateways and Windows clients. Runtime components may validate bundle freshness and local applicability, but the control plane remains the single place where global policy membership and bundle compilation are decided.

## Consequences

- Positive:
  - Single policy decision point with consistent operator semantics.
  - Smaller and simpler gateway and client implementations.
  - Easier auditability for policy outcomes.
- Negative:
  - Requires policy compilation and delivery infrastructure in the control plane.
  - Increases importance of control-plane availability and caching strategy.
- Follow-up work:
  - Implement policy resolution and bundle compilation.
  - Add revocation and periodic revalidation flows.
  - Define cache and refresh behavior for disconnected clients.
