using OWLProtect.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient<GatewayControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5180");
});
builder.Services.AddHostedService<GatewayHeartbeatService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "gateway" }));
app.MapGet("/diagnostics", (GatewayHeartbeatService service) => Results.Ok(service.LastHeartbeat));

app.Run();

public sealed class GatewayControlPlaneClient(HttpClient httpClient)
{
    public async Task PublishHeartbeatAsync(Gateway heartbeat, CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsJsonAsync(ControlPlaneApiConventions.Path("/gateways/heartbeat"), heartbeat, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}

public sealed class GatewayHeartbeatService(
    ILogger<GatewayHeartbeatService> logger,
    IConfiguration configuration,
    GatewayControlPlaneClient client) : BackgroundService
{
    private readonly string _gatewayId = configuration["Gateway:Id"] ?? "gw-1";
    private readonly string _gatewayName = configuration["Gateway:Name"] ?? "us-east-core-1";
    private readonly string _region = configuration["Gateway:Region"] ?? "us-east";
    private int _tick;

    public Gateway LastHeartbeat { get; private set; } =
        new("gw-1", "us-east-core-1", "us-east", HealthSeverity.Green, 20, 100, 20, 40, 15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _tick++;
            LastHeartbeat = BuildHeartbeat();

            try
            {
                await client.PublishHeartbeatAsync(LastHeartbeat, stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to publish gateway heartbeat.");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private Gateway BuildHeartbeat()
    {
        var load = 25 + ((_tick * 7) % 55);
        var latency = 16 + ((_tick * 5) % 28);
        var health = load > 75 || latency > 40 ? HealthSeverity.Yellow : HealthSeverity.Green;

        return new Gateway(
            _gatewayId,
            _gatewayName,
            _region,
            health,
            load,
            120 + (_tick % 35),
            25 + (_tick % 40),
            45 + (_tick % 30),
            latency);
    }
}
