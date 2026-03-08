using Microsoft.Extensions.Diagnostics.HealthChecks;
using OWLProtect.Core;

namespace OWLProtect.Scheduler;

internal sealed class SchedulerCycleState() : MonitoredOperationState("scheduler-cycle");

internal sealed class SchedulerCycleHealthCheck(SchedulerCycleState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationalHealthCheckEvaluator.EvaluateState(state.Snapshot(), TimeSpan.FromHours(2)));
}
