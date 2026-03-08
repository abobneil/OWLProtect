namespace OWLProtect.Core;

public class MonitoredOperationState(string name)
{
    private readonly Lock _gate = new();
    private DateTimeOffset? _lastStartedAtUtc;
    private DateTimeOffset? _lastCompletedAtUtc;
    private DateTimeOffset? _lastSucceededAtUtc;
    private DateTimeOffset? _lastFailedAtUtc;
    private TimeSpan? _lastDuration;
    private string? _lastDetail;
    private string? _lastError;
    private int _lastAffectedItemCount;
    private int _consecutiveFailures;

    public string Name { get; } = name;

    public void RecordStart(DateTimeOffset startedAtUtc)
    {
        lock (_gate)
        {
            _lastStartedAtUtc = startedAtUtc;
        }
    }

    public void RecordSuccess(DateTimeOffset completedAtUtc, TimeSpan duration, string? detail = null, int affectedItemCount = 0)
    {
        lock (_gate)
        {
            _lastCompletedAtUtc = completedAtUtc;
            _lastSucceededAtUtc = completedAtUtc;
            _lastDuration = duration;
            _lastDetail = detail;
            _lastError = null;
            _lastAffectedItemCount = affectedItemCount;
            _consecutiveFailures = 0;
        }
    }

    public void RecordFailure(DateTimeOffset completedAtUtc, TimeSpan duration, string error, string? detail = null)
    {
        lock (_gate)
        {
            _lastCompletedAtUtc = completedAtUtc;
            _lastFailedAtUtc = completedAtUtc;
            _lastDuration = duration;
            _lastDetail = detail;
            _lastError = error;
            _consecutiveFailures++;
        }
    }

    public OperationStatusSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new OperationStatusSnapshot(
                Name,
                _lastStartedAtUtc,
                _lastCompletedAtUtc,
                _lastSucceededAtUtc,
                _lastFailedAtUtc,
                _lastDuration,
                _lastDetail,
                _lastError,
                _lastAffectedItemCount,
                _consecutiveFailures);
        }
    }
}

public sealed record OperationStatusSnapshot(
    string Name,
    DateTimeOffset? LastStartedAtUtc,
    DateTimeOffset? LastCompletedAtUtc,
    DateTimeOffset? LastSucceededAtUtc,
    DateTimeOffset? LastFailedAtUtc,
    TimeSpan? LastDuration,
    string? LastDetail,
    string? LastError,
    int LastAffectedItemCount,
    int ConsecutiveFailures);
