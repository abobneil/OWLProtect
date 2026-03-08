using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OWLProtect.Core;

public static class OwlProtectTelemetry
{
    public const string CorrelationIdHeaderName = "X-Correlation-ID";
    public const string SessionCorrelationItemKey = "owlprotect.session.correlation";

    public static readonly ActivitySource ActivitySource = new("OWLProtect");
    public static readonly Meter Meter = new("OWLProtect");

    public static readonly Counter<long> AuthAttempts = Meter.CreateCounter<long>("owlprotect.auth.attempts");
    public static readonly Counter<long> SessionsIssued = Meter.CreateCounter<long>("owlprotect.sessions.issued");
    public static readonly Histogram<double> PolicyAuthorizationDuration = Meter.CreateHistogram<double>("owlprotect.policy.authorization.duration", unit: "ms");
    public static readonly Counter<long> SessionRevalidationRuns = Meter.CreateCounter<long>("owlprotect.session_revalidation.runs");
    public static readonly Counter<long> SessionRevalidationAffectedSessions = Meter.CreateCounter<long>("owlprotect.session_revalidation.affected_sessions");
    public static readonly Counter<long> DiagnosticsSamplesRecorded = Meter.CreateCounter<long>("owlprotect.diagnostics.samples_recorded");
    public static readonly Counter<long> AuditRetentionRuns = Meter.CreateCounter<long>("owlprotect.audit_retention.runs");
    public static readonly Counter<long> AuditRetentionExportedEvents = Meter.CreateCounter<long>("owlprotect.audit_retention.exported_events");
    public static readonly Counter<long> GatewayHeartbeatPublishes = Meter.CreateCounter<long>("owlprotect.gateway.heartbeats");
    public static readonly Histogram<double> GatewayHeartbeatPublishDuration = Meter.CreateHistogram<double>("owlprotect.gateway.heartbeat_publish.duration", unit: "ms");
    public static readonly Counter<long> SchedulerCycles = Meter.CreateCounter<long>("owlprotect.scheduler.cycles");
    public static readonly UpDownCounter<long> EventStreamConnections = Meter.CreateUpDownCounter<long>("owlprotect.eventstream.connections");
}
