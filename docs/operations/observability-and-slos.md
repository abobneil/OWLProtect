# Observability and SLOs

This document defines the current observability contract for OWLProtect self-hosted environments.

## Service Signals

- Control plane:
  - `GET /health/live`
  - `GET /health/ready`
  - `GET /metrics`
  - `GET /api/v1/operations/diagnostics`
- Gateway:
  - `GET /health/live`
  - `GET /health/ready`
  - `GET /metrics`
  - `GET /diagnostics`
- Scheduler:
  - `GET /health/live`
  - `GET /health/ready`
  - `GET /metrics`
  - `GET /diagnostics`
- Windows client service:
  - OTLP metrics and traces when `Observability__OtlpEndpoint` is configured
  - `status` over the `owlprotect-client` named pipe for the current local session and recovery state
  - `support-bundle` over the `owlprotect-client` named pipe for operator-facing diagnostic capture

Every HTTP service also returns `X-Correlation-ID` on responses. The control plane derives a separate non-secret session correlation ID from the platform session and adds it to request logs and traces.
The Windows client service now propagates `X-Correlation-ID` on control-plane calls so client-side traces can be joined with control-plane request logs.

## Logging Model and Redaction

- Request-completion logs are structured around method, path, status code, duration, correlation ID, session correlation ID, and redacted remote IP.
- Tokens, passwords, secrets, signatures, nonce values, serial numbers, and private-key material must never be logged verbatim.
- IP addresses must be logged only in redacted form unless an operator is collecting a one-off diagnostic capture in a controlled environment.
- Use the session correlation ID, not the raw platform session ID or bearer token, when joining admin support evidence across logs.
- Error responses may include safe diagnostic codes. Sensitive upstream provider payloads must stay in provider-side audit trails, not OWLProtect logs.

## Metrics and Traces

Custom service metrics now cover:

- `owlprotect.auth.attempts`
- `owlprotect.sessions.issued`
- `owlprotect.policy.authorization.duration`
- `owlprotect.session_revalidation.runs`
- `owlprotect.session_revalidation.affected_sessions`
- `owlprotect.diagnostics.samples_recorded`
- `owlprotect.audit_retention.runs`
- `owlprotect.audit_retention.exported_events`
- `owlprotect.gateway.heartbeats`
- `owlprotect.gateway.heartbeat_publish.duration`
- `owlprotect.scheduler.cycles`
- `owlprotect.eventstream.connections`
- `owlprotect.client.connect.attempts`
- `owlprotect.client.connect.duration`
- `owlprotect.client.controlplane.calls`
- `owlprotect.client.controlplane.call.duration`
- `owlprotect.client.posture.collections`
- `owlprotect.client.posture.score`
- `owlprotect.client.diagnostics.samples`
- `owlprotect.client.network.latency`
- `owlprotect.client.network.packet_loss`
- `owlprotect.client.ipc.requests`
- `owlprotect.client.ipc.request.duration`
- `owlprotect.client.support_bundle.exports`

The HTTP services also emit standard ASP.NET Core telemetry, and all instrumented services emit HttpClient, process, and runtime telemetry through OpenTelemetry. Configure OTLP export with:

- `Observability__OtlpEndpoint`
- `Observability__OtlpProtocol`
- `Observability__ServiceNamespace`

Prometheus scraping is enabled by default on `/metrics`.
The Windows client service is a worker process rather than an HTTP endpoint, so its telemetry is exported through OTLP rather than a local Prometheus scrape endpoint.

## Alert Rules

Sample Prometheus alert rules live in [ops/observability/prometheus-alerts.yml](/C:/Users/nchester/Documents/GitHub/OWLProtect/ops/observability/prometheus-alerts.yml).

Minimum alert coverage:

- auth failure burst on the control plane
- gateway heartbeat loss
- operator event-stream drop
- service scrape or readiness failure
- storage pressure on PostgreSQL and `/var/lib/owlprotect`

## Dashboard Query Set

Recommended dashboard panels:

1. Availability:
   - `up{job=~"owlprotect-control-plane|owlprotect-gateway|owlprotect-scheduler"}`
   - `http_server_request_duration_seconds_count`
2. Auth:
   - `sum by (flow, outcome) (increase(owlprotect_auth_attempts_total[15m]))`
3. Session policy:
   - `sum by (flow, authorized) (rate(owlprotect_policy_authorization_duration_milliseconds_count[5m]))`
   - `sum(rate(owlprotect_session_revalidation_runs_total[15m]))`
4. Gateway:
   - `increase(owlprotect_gateway_heartbeats_total[15m])`
   - `histogram_quantile(0.95, sum by (le) (rate(owlprotect_gateway_heartbeat_publish_duration_milliseconds_bucket[5m])))`
5. Operations:
   - `increase(owlprotect_audit_retention_runs_total[24h])`
   - `sum(increase(owlprotect_audit_retention_exported_events_total[24h]))`
6. Streaming:
   - `max_over_time(owlprotect_eventstream_connections[15m])`
7. Windows client:
   - `sum by (outcome, auth_mode) (increase(owlprotect_client_connect_attempts_total[15m]))`
   - `histogram_quantile(0.95, sum by (le) (rate(owlprotect_client_connect_duration_milliseconds_bucket[15m])))`
   - `sum by (operation, outcome) (increase(owlprotect_client_controlplane_calls_total[15m]))`
   - `histogram_quantile(0.95, sum by (le) (rate(owlprotect_client_network_latency_milliseconds_bucket[15m])))`

## Service-Level Objectives

Current SLO targets:

- Admin sign-in and dashboard reads:
  - 99.9 percent monthly success
  - p95 end-to-end latency under 750 ms for control-plane reads in a single-region deployment
- Provider-backed user sign-in:
  - 99.5 percent monthly success, excluding upstream identity-provider outages explicitly declared outside OWLProtect
  - p95 control-plane processing under 1.5 s after the token reaches OWLProtect
- Gateway heartbeat publication:
  - 99.95 percent of heartbeat intervals delivered within 30 seconds
- Session revalidation:
  - 99.9 percent of scheduled revalidation cycles complete within 2 minutes
- Audit retention:
  - 99 percent of daily retention jobs finish within the configured check interval plus 1 hour
- Windows client connect workflow:
  - 99.5 percent of connect attempts complete successfully when the configured control plane and identity provider are healthy
  - p95 end-to-end connect latency under 5 seconds in a single-region deployment with a reachable control plane

## Error Budgets

- 99.9 percent monthly success leaves 43.2 minutes of budget per month.
- 99.5 percent monthly success leaves 3 hours 39 minutes of budget per month.
- Consuming more than 25 percent of a monthly budget in seven days should pause non-emergency feature releases until the cause is understood.
- Consuming more than 50 percent of a monthly budget in seven days should require rollback or forward-fix approval from the on-call operator and repository maintainer.
- Apply the 99.5 percent budget to Windows client connect failures only when the control plane and identity provider are healthy enough to attribute the fault to OWLProtect.

## Performance Baselines

These baselines guide upgrade validation:

- Control plane:
  - sustain 50 admin requests per second on a single node without p95 latency exceeding 750 ms
  - sustain 5 websocket streams per operator session with reconnect recovery inside 10 seconds
- Gateway:
  - publish a heartbeat every 10 seconds with p95 publish latency below 250 ms to the control plane
- Scheduler:
  - complete the seeded test-user maintenance cycle in under 5 seconds when no action is required
- Windows client service:
  - complete the connect workflow, including auth, enrollment, posture upload, and client-session issue, within 5 seconds p95 on a healthy local network
  - answer `status` over the named pipe in under 250 ms p95 and export a support bundle in under 2 seconds p95
- PostgreSQL:
  - health and bootstrap queries should stay below 100 ms p95 under normal single-node load

Treat these as release gates until production telemetry provides stricter real-world baselines.
