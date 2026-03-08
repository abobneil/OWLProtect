using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OWLProtect.Core;

public static class OperationalHealthCheckEvaluator
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
