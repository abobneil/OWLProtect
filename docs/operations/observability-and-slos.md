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

Every HTTP service also returns `X-Correlation-ID` on responses. The control plane derives a separate non-secret session correlation ID from the platform session and adds it to request logs and traces.

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

The services also emit standard ASP.NET Core, HttpClient, process, and runtime telemetry through OpenTelemetry. Configure OTLP export with:

- `Observability__OtlpEndpoint`
- `Observability__OtlpProtocol`
- `Observability__ServiceNamespace`

Prometheus scraping is enabled by default on `/metrics`.

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

## Error Budgets

- 99.9 percent monthly success leaves 43.2 minutes of budget per month.
- 99.5 percent monthly success leaves 3 hours 39 minutes of budget per month.
- Consuming more than 25 percent of a monthly budget in seven days should pause non-emergency feature releases until the cause is understood.
- Consuming more than 50 percent of a monthly budget in seven days should require rollback or forward-fix approval from the on-call operator and repository maintainer.

## Performance Baselines

These baselines guide upgrade validation:

- Control plane:
  - sustain 50 admin requests per second on a single node without p95 latency exceeding 750 ms
  - sustain 5 websocket streams per operator session with reconnect recovery inside 10 seconds
- Gateway:
  - publish a heartbeat every 10 seconds with p95 publish latency below 250 ms to the control plane
- Scheduler:
  - complete the seeded test-user maintenance cycle in under 5 seconds when no action is required
- PostgreSQL:
  - health and bootstrap queries should stay below 100 ms p95 under normal single-node load

Treat these as release gates until production telemetry provides stricter real-world baselines.
