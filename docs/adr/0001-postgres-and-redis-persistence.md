# ADR 0001: Use PostgreSQL for system-of-record data and Redis for ephemeral coordination

- Status: Accepted
- Date: 2026-03-08
- Deciders: Repository maintainers
- Technical Story: Establish durable persistence and low-latency coordination for the control plane.

## Context

The scaffold currently keeps all state in memory. That is acceptable for seeded demos but cannot support real sessions, audit retention, migration discipline, or multi-process coordination. The platform needs durable relational storage for operators and runtime entities, plus a fast ephemeral store for revocation and fanout coordination.

## Decision

Use PostgreSQL as the durable system of record for control-plane entities and audit history. Use Redis for revocation state, transient caches, short-lived coordination, and websocket or worker fanout support. Keep the responsibility split explicit so relational truth stays in PostgreSQL and ephemeral coordination stays in Redis.

## Consequences

- Positive:
  - Durable relational storage with migration support and transactional integrity.
  - Clear separation between durable state and ephemeral coordination concerns.
  - Fits the existing local Docker compose stack.
- Negative:
  - Introduces two data stores and operational complexity.
  - Requires repository boundaries and migration discipline before feature delivery can continue safely.
- Follow-up work:
  - Add relational schema and migrations.
  - Replace the in-memory scaffold with PostgreSQL-backed repositories.
  - Introduce Redis-backed revocation and cache flows.
