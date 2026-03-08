# OWLProtect Architecture Overview

OWLProtect is a Windows-first enterprise VPN platform built as a monorepo with separate delivery surfaces for operators, services, and end-user clients.

## Core Responsibilities

### Admin Portal

- React-based operator experience for bootstrap, dashboard, fleet health, user management, policy management, alerts, and audit review.
- Consumes control-plane HTTP APIs and WebSocket streams.
- Depends on `packages/contracts` for transport shapes and `packages/theme` for shared design tokens.

### Control Plane API

- Source of truth for tenants, admins, users, devices, gateways, policies, sessions, alerts, auth provider configuration, and audit.
- Owns external API versioning, persistence orchestration, and session/policy decisions delivered to other components.
- Publishes near-real-time views to the admin portal and other trusted consumers.

### Gateway Service

- Represents a WireGuard-capable edge gateway instance.
- Reports health, capacity, and diagnostics to the control plane.
- Receives routing, DNS, certificate, and policy material from the control plane over trusted service-to-service channels.

### Scheduler

- Runs background maintenance workflows such as session revalidation, time-based policy enforcement, test-account cleanup, audit retention, and migration-adjacent maintenance tasks.
- Should stay stateless and coordinate with the control plane rather than owning source-of-truth data.

### Windows Client Service

- Privileged local component that owns tunnel lifecycle, device posture collection, and secure local IPC.
- Integrates with the VPN stack and exposes a narrow named-pipe control surface to the UI shell.

### Windows Client UI

- User-facing shell for connect/disconnect, SSO prompts, posture/diagnostics visibility, and support actions.
- Talks only to the local Windows service and never owns trust material directly.

## Service Boundaries

- The control plane is the system of record.
- The gateway and scheduler are stateless workers around control-plane decisions.
- The admin portal is an operator console, not a policy source of truth.
- The Windows client consumes issued sessions and policy bundles; it does not evaluate global policy membership rules independently.

## Persistence Shape

- PostgreSQL is the durable relational system of record for control-plane entities and audit history.
- Redis is the ephemeral coordination layer for revocation state, websocket fanout hints, short-lived caches, and scheduler coordination.
- In-memory seeded data remains acceptable only as a development scaffold until the persistence migration work is complete.

## Trust and Auth Model

- External identity enters through configured Entra ID or generic OIDC providers.
- The control plane validates tokens, issues platform sessions, enforces MFA and role boundaries, and records audit events for privileged operations.
- Gateways and Windows clients consume issued trust material and revocation outcomes rather than validating arbitrary external identity tokens directly.

## Delivery Flow

1. Operators configure providers, policies, and gateway inventory in the admin portal.
2. The control plane stores the source-of-truth data and emits operator-facing views.
3. Gateways publish health and receive resolved policy or trust material.
4. Windows clients authenticate, receive platform sessions and policy bundles, and establish tunnels through assigned gateways.

## Repo Mapping

- `apps/admin-portal`: operator UI
- `packages/contracts`: shared frontend transport contracts and seeded mock data
- `packages/theme`: shared visual tokens
- `services/control-plane-api`: HTTP and WebSocket control plane
- `services/gateway`: edge gateway reporting and orchestration surface
- `services/scheduler`: background maintenance workflows
- `windows/windows-client-service`: privileged Windows-side agent
- `windows/windows-client-ui`: WinUI desktop shell
