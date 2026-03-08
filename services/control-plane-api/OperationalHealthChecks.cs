using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using OWLProtect.Core;
using StackExchange.Redis;

namespace OWLProtect.ControlPlane.Api;

internal sealed class AuditRetentionWorkerState() : MonitoredOperationState("audit-retention-worker");

internal sealed class SessionRevalidationWorkerState() : MonitoredOperationState("session-revalidation-worker");

internal sealed class TestUserDisableWorkerState() : MonitoredOperationState("test-user-disable-worker");

internal sealed class PersistenceHealthCheck(IOptions<PersistenceOptions> options) : IHealthCheck
{
    private readonly PersistenceOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_options.Provider, "postgres", StringComparison.OrdinalIgnoreCase))
        {
            return HealthCheckResult.Healthy("The control plane is using in-memory persistence for this environment.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(_options.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using (var command = new NpgsqlCommand("SELECT 1", connection))
            {
                await command.ExecuteScalarAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(_options.RedisConnectionString))
            {
                return HealthCheckResult.Healthy("PostgreSQL is reachable.");
            }

            var configuration = ConfigurationOptions.Parse(_options.RedisConnectionString);
            configuration.AbortOnConnectFail = false;
            configuration.ConnectTimeout = 3000;
            await using var redis = await ConnectionMultiplexer.ConnectAsync(configuration);
            var ping = await redis.GetDatabase().PingAsync();

            return HealthCheckResult.Healthy(
                "PostgreSQL and Redis are reachable.",
                data: new Dictionary<string, object>
                {
                    ["redisPingMs"] = Math.Round(ping.TotalMilliseconds, 2)
                });
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("One or more persistence dependencies are unavailable.", exception);
        }
    }
}

internal sealed class AuditExportDirectoryHealthCheck(
    IOptions<AuditRetentionOptions> options,
    IHostEnvironment hostEnvironment) : IHealthCheck
{
    private readonly AuditRetentionOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var exportRoot = Path.IsPathRooted(_options.ExportDirectory)
                ? _options.ExportDirectory
                : Path.GetFullPath(Path.Combine(hostEnvironment.ContentRootPath, _options.ExportDirectory));

            Directory.CreateDirectory(exportRoot);
            var probePath = Path.Combine(exportRoot, $".healthcheck-{Guid.NewGuid():n}");
            await File.WriteAllTextAsync(probePath, string.Empty, cancellationToken);
            File.Delete(probePath);

            return HealthCheckResult.Healthy(
                "Audit export directory is writable.",
                data: new Dictionary<string, object> { ["path"] = exportRoot });
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("Audit export directory is unavailable or not writable.", exception);
        }
    }
}

internal sealed class AuditRetentionWorkerHealthCheck(
    AuditRetentionWorkerState state,
    IOptions<AuditRetentionOptions> options) : IHealthCheck
{
    private readonly AuditRetentionOptions _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Audit retention is disabled."));
        }

        var threshold = TimeSpan.FromHours(Math.Clamp(_options.CheckIntervalHours, 1, 168) * 2);
        return Task.FromResult(OperationalHealthCheckEvaluator.EvaluateState(state.Snapshot(), threshold));
    }
}

internal sealed class SessionRevalidationWorkerHealthCheck(
    SessionRevalidationWorkerState state,
    IPlatformBootstrapSettingsProvider bootstrapSettingsProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var revalidationSeconds = Math.Max(bootstrapSettingsProvider.GetSettings().SessionRevalidationSeconds, 30);
        var threshold = TimeSpan.FromSeconds(revalidationSeconds * 3);
        return Task.FromResult(OperationalHealthCheckEvaluator.EvaluateState(state.Snapshot(), threshold));
    }
}

internal sealed class TestUserDisableWorkerHealthCheck(TestUserDisableWorkerState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationalHealthCheckEvaluator.EvaluateState(state.Snapshot(), TimeSpan.FromMinutes(5)));
}

internal static class OperationalHealthCheckEvaluator
{
    public static HealthCheckResult EvaluateState(OperationStatusSnapshot snapshot, TimeSpan maxSuccessAge)
    {
        if (snapshot.LastSucceededAtUtc is null && snapshot.LastFailedAtUtc is null)
        {
            return HealthCheckResult.Healthy($"{snapshot.Name} has not completed its first cycle yet.");
        }

        if (snapshot.LastSucceededAtUtc is null && snapshot.LastFailedAtUtc is not null)
        {
            return HealthCheckResult.Unhealthy(
                $"{snapshot.Name} has not completed a successful cycle.",
                data: BuildData(snapshot));
        }

        var age = DateTimeOffset.UtcNow - snapshot.LastSucceededAtUtc!.Value;
        if (age > maxSuccessAge)
        {
            return HealthCheckResult.Unhealthy(
                $"{snapshot.Name} has not succeeded within the expected freshness window.",
                data: BuildData(snapshot));
        }

        return HealthCheckResult.Healthy(
            $"{snapshot.Name} is current.",
            data: BuildData(snapshot));
    }

    private static IReadOnlyDictionary<string, object> BuildData(OperationStatusSnapshot snapshot) =>
        new Dictionary<string, object>
        {
            ["lastStartedAtUtc"] = snapshot.LastStartedAtUtc?.ToString("O") ?? string.Empty,
            ["lastCompletedAtUtc"] = snapshot.LastCompletedAtUtc?.ToString("O") ?? string.Empty,
            ["lastSucceededAtUtc"] = snapshot.LastSucceededAtUtc?.ToString("O") ?? string.Empty,
            ["lastFailedAtUtc"] = snapshot.LastFailedAtUtc?.ToString("O") ?? string.Empty,
            ["lastDurationMs"] = snapshot.LastDuration?.TotalMilliseconds ?? 0d,
            ["lastDetail"] = snapshot.LastDetail ?? string.Empty,
            ["lastError"] = snapshot.LastError ?? string.Empty,
            ["consecutiveFailures"] = snapshot.ConsecutiveFailures
        };
}
