using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

internal sealed record AuditExportEnvelope(
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset CutoffUtc,
    int EventCount,
    long FirstSequence,
    long LastSequence,
    string LastEventHash,
    IReadOnlyList<AuditEvent> Events);

public sealed record AuditRetentionRunResult(int ExportedEventCount, AuditRetentionCheckpoint? Checkpoint);

public sealed class AuditRetentionService(
    IAuditRepository auditRepository,
    IAuditRetentionRepository auditRetentionRepository,
    IAuditWriter auditWriter,
    IOptions<AuditRetentionOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<AuditRetentionService> logger)
{
    private readonly AuditRetentionOptions _options = options.Value;

    public async Task<AuditRetentionRunResult> RunRetentionAsync(CancellationToken cancellationToken)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("controlplane.audit_retention.run");
        var outcome = "success";
        var exportedEventCount = 0;

        try
        {
            if (!_options.Enabled)
            {
                outcome = "disabled";
                return new AuditRetentionRunResult(0, null);
            }

            if (_options.RetentionDays <= 0)
            {
                throw new InvalidOperationException("AuditRetention:RetentionDays must be greater than zero.");
            }

            var exportBatchSize = Math.Clamp(_options.ExportBatchSize, 1, 50_000);
            var cutoffUtc = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
            var events = auditRepository.ListAuditEventsForExport(cutoffUtc, exportBatchSize);
            if (events.Count == 0)
            {
                outcome = "no_data";
                return new AuditRetentionRunResult(0, null);
            }

            var exportRoot = ResolveExportDirectory(hostEnvironment.ContentRootPath, _options.ExportDirectory);
            Directory.CreateDirectory(exportRoot);

            var exportedAtUtc = DateTimeOffset.UtcNow;
            var first = events[0];
            var last = events[^1];
            var exportPath = Path.Combine(exportRoot, $"audit-export-{first.Sequence:D12}-{last.Sequence:D12}-{exportedAtUtc:yyyyMMddTHHmmssZ}.json");
            var envelope = new AuditExportEnvelope(
                exportedAtUtc,
                cutoffUtc,
                events.Count,
                first.Sequence,
                last.Sequence,
                last.EventHash,
                events);

            var tempPath = $"{exportPath}.tmp";
            await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(envelope), cancellationToken);
            File.Move(tempPath, exportPath, overwrite: true);

            var checkpoint = auditRetentionRepository.ApplyRetention(new AuditRetentionOperation(
                cutoffUtc,
                exportedAtUtc,
                exportPath,
                last.Sequence,
                last.CreatedAtUtc,
                last.EventHash,
                events.Count));

            exportedEventCount = events.Count;
            activity?.SetTag("owlprotect.audit_retention.checkpoint_id", checkpoint.Id);
            auditWriter.WriteAudit("system", "audit-retention", "audit-retention", checkpoint.Id, "success", $"Exported and pruned {events.Count} audit events through sequence {last.Sequence}.");
            logger.LogInformation("Exported and pruned {AuditEventCount} audit events to {ExportPath}.", events.Count, exportPath);
            return new AuditRetentionRunResult(events.Count, checkpoint);
        }
        catch
        {
            outcome = "failure";
            activity?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
        finally
        {
            activity?.SetTag("owlprotect.audit_retention.outcome", outcome);
            activity?.SetTag("owlprotect.audit_retention.exported_events", exportedEventCount);
            OwlProtectTelemetry.AuditRetentionRuns.Add(1, new TagList { { "outcome", outcome } });
            if (exportedEventCount > 0)
            {
                OwlProtectTelemetry.AuditRetentionExportedEvents.Add(exportedEventCount, new TagList { { "outcome", outcome } });
            }
        }
    }

    private static string ResolveExportDirectory(string contentRootPath, string configuredDirectory) =>
        Path.IsPathRooted(configuredDirectory)
            ? configuredDirectory
            : Path.GetFullPath(Path.Combine(contentRootPath, configuredDirectory));
}

internal sealed class AuditRetentionWorker(
    IServiceProvider serviceProvider,
    IOptions<AuditRetentionOptions> options,
    AuditRetentionWorkerState state,
    ILogger<AuditRetentionWorker> logger) : BackgroundService
{
    private readonly AuditRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        var checkInterval = TimeSpan.FromHours(Math.Clamp(_options.CheckIntervalHours, 1, 168));
        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var start = Stopwatch.GetTimestamp();
            state.RecordStart(startedAtUtc);
            try
            {
                using var scope = serviceProvider.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<AuditRetentionService>().RunRetentionAsync(stoppingToken);
                state.RecordSuccess(
                    DateTimeOffset.UtcNow,
                    Stopwatch.GetElapsedTime(start),
                    result.Checkpoint is null ? "No audit events were eligible for export." : $"Exported through checkpoint {result.Checkpoint.Id}.",
                    result.ExportedEventCount);
            }
            catch (Exception exception)
            {
                state.RecordFailure(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start), exception.Message);
                logger.LogWarning(exception, "Audit retention cycle failed.");
            }

            await Task.Delay(checkInterval, stoppingToken);
        }
    }
}
