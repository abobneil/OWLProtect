using System.Net.Http.Json;
using OWLProtect.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHttpClient<TestUserControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5180");
});
builder.Services.AddHostedService<TestUserDisableWorker>();

var host = builder.Build();
host.Run();

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

public sealed class TestUserDisableWorker(
    ILogger<TestUserDisableWorker> logger,
    TestUserControlPlaneClient controlPlaneClient) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Scheduler cycle failed.");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var bootstrap = await controlPlaneClient.GetBootstrapStatusAsync(cancellationToken);
        if (bootstrap is null || !bootstrap.TestUserEnabled || bootstrap.TestUserAutoDisableAtUtc is null)
        {
            return;
        }

        if (bootstrap.TestUserAutoDisableAtUtc.Value > DateTimeOffset.UtcNow)
        {
            return;
        }

        var users = await controlPlaneClient.GetUsersAsync(cancellationToken);
        var testUser = users.SingleOrDefault(user => string.Equals(user.Username, "user", StringComparison.OrdinalIgnoreCase));
        if (testUser is null || !testUser.Enabled)
        {
            return;
        }

        await controlPlaneClient.DisableUserAsync(testUser.Id, cancellationToken);
        logger.LogInformation("Auto-disabled seeded test user {UserId}.", testUser.Id);
    }
}
