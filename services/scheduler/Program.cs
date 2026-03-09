using System.Diagnostics;
using System.Net.Http.Json;
using OWLProtect.Core;
using OWLProtect.Scheduler;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOwlProtectObservability(builder.Configuration, builder.Environment, "scheduler", includeAspNetCoreInstrumentation: true);
builder.Services.AddSingleton<SchedulerCycleState>();
builder.Services.AddHealthChecks()
    .AddCheck<SchedulerCycleHealthCheck>("scheduler_cycle", tags: ["ready"]);
builder.Services.AddHttpClient<TestUserControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5180");
});
builder.Services.AddHostedService<TestUserDisableWorker>();

var app = builder.Build();
app.UseOwlProtectRequestCorrelation();
app.MapOwlProtectOperationalEndpoints();
app.MapGet("/diagnostics", (SchedulerCycleState cycleState) => Results.Ok(new
{
    cycle = cycleState.Snapshot(),
    metrics = "/metrics"
}));

app.Run();

public sealed record BootstrapStatus(bool RequiresPasswordChange, bool RequiresMfaEnrollment, bool TestUserEnabled, DateTimeOffset? TestUserAutoDisableAtUtc);
public sealed record UserView(string Id, string Username, bool Enabled);

public sealed class TestUserControlPlaneClient(HttpClient httpClient)
{
    public async Task<BootstrapStatus?> GetBootstrapStatusAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<BootstrapStatus>(ControlPlaneApiConventions.Path("/bootstrap"), cancellationToken);

    public async Task<IReadOnlyList<UserView>> GetUsersAsync(CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync<List<UserView>>(ControlPlaneApiConventions.Path("/users"), cancellationToken) ?? [];

    public async Task DisableUserAsync(string userId, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(ControlPlaneApiConventions.Path($"/users/{userId}/disable"), content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed class TestUserDisableWorker(
    ILogger<TestUserDisableWorker> logger,
    TestUserControlPlaneClient controlPlaneClient,
    SchedulerCycleState cycleState) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAtUtc = DateTimeOffset.UtcNow;
            var start = Stopwatch.GetTimestamp();
            cycleState.RecordStart(startedAtUtc);
            using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("scheduler.disable_test_user");
            var outcome = "success";
            var affectedUsers = 0;
            var nextDelay = TimeSpan.FromHours(1);

            try
            {
                affectedUsers = await RunCycleAsync(stoppingToken);
                cycleState.RecordSuccess(
                    DateTimeOffset.UtcNow,
                    Stopwatch.GetElapsedTime(start),
                    affectedUsers > 0 ? "Disabled the seeded test user." : "No scheduler action was required.",
                    affectedUsers);
            }
            catch (Exception exception)
            {
                outcome = "failure";
                nextDelay = TimeSpan.FromSeconds(15);
                activity?.SetStatus(ActivityStatusCode.Error);
                cycleState.RecordFailure(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start), exception.Message);
                logger.LogWarning(exception, "Scheduler cycle failed.");
            }
            finally
            {
                activity?.SetTag("owlprotect.scheduler.outcome", outcome);
                activity?.SetTag("owlprotect.scheduler.affected_users", affectedUsers);
                OwlProtectTelemetry.SchedulerCycles.Add(1, new TagList { { "outcome", outcome } });
            }

            await Task.Delay(nextDelay, stoppingToken);
        }
    }

    private async Task<int> RunCycleAsync(CancellationToken cancellationToken)
    {
        var bootstrap = await controlPlaneClient.GetBootstrapStatusAsync(cancellationToken);
        if (bootstrap is null || !bootstrap.TestUserEnabled || bootstrap.TestUserAutoDisableAtUtc is null)
        {
            return 0;
        }

        if (bootstrap.TestUserAutoDisableAtUtc.Value > DateTimeOffset.UtcNow)
        {
            return 0;
        }

        var users = await controlPlaneClient.GetUsersAsync(cancellationToken);
        var testUser = users.SingleOrDefault(user => string.Equals(user.Username, "user", StringComparison.OrdinalIgnoreCase));
        if (testUser is null || !testUser.Enabled)
        {
            return 0;
        }

        await controlPlaneClient.DisableUserAsync(testUser.Id, cancellationToken);
        logger.LogInformation("Auto-disabled seeded test user {UserId}.", testUser.Id);
        return 1;
    }
}
