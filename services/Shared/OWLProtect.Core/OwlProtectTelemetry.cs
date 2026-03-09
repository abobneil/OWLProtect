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
    public static readonly Counter<long> ClientConnectAttempts = Meter.CreateCounter<long>("owlprotect.client.connect.attempts");
    public static readonly Histogram<double> ClientConnectDuration = Meter.CreateHistogram<double>("owlprotect.client.connect.duration", unit: "ms");
    public static readonly Counter<long> ClientControlPlaneCalls = Meter.CreateCounter<long>("owlprotect.client.controlplane.calls");
    public static readonly Histogram<double> ClientControlPlaneCallDuration = Meter.CreateHistogram<double>("owlprotect.client.controlplane.call.duration", unit: "ms");
    public static readonly Counter<long> ClientPostureCollections = Meter.CreateCounter<long>("owlprotect.client.posture.collections");
    public static readonly Histogram<double> ClientPostureScore = Meter.CreateHistogram<double>("owlprotect.client.posture.score");
    public static readonly Counter<long> ClientDiagnosticsSamples = Meter.CreateCounter<long>("owlprotect.client.diagnostics.samples");
    public static readonly Histogram<double> ClientNetworkLatency = Meter.CreateHistogram<double>("owlprotect.client.network.latency", unit: "ms");
    public static readonly Histogram<double> ClientNetworkPacketLoss = Meter.CreateHistogram<double>("owlprotect.client.network.packet_loss", unit: "%");
    public static readonly Counter<long> ClientIpcRequests = Meter.CreateCounter<long>("owlprotect.client.ipc.requests");
    public static readonly Histogram<double> ClientIpcRequestDuration = Meter.CreateHistogram<double>("owlprotect.client.ipc.request.duration", unit: "ms");
    public static readonly Counter<long> ClientSupportBundleExports = Meter.CreateCounter<long>("owlprotect.client.support_bundle.exports");
}
