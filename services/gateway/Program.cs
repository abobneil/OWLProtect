using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OWLProtect.Core;
using OWLProtect.Gateway;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOwlProtectObservability(builder.Configuration, builder.Environment, "gateway", includeAspNetCoreInstrumentation: true);
builder.Services.AddSingleton<GatewayHeartbeatState>();
builder.Services.AddHealthChecks()
    .AddCheck<GatewayTrustBundleHealthCheck>("trust_bundle", tags: ["ready"])
    .AddCheck<GatewayHeartbeatHealthCheck>("heartbeat_publisher", tags: ["ready"]);
builder.Services.AddHttpClient<GatewayControlPlaneClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["ControlPlane:BaseUrl"] ?? "http://localhost:5180");
});
builder.Services.AddSingleton<GatewayTrustBundleStore>();
builder.Services.AddSingleton<GatewayHeartbeatService>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<GatewayHeartbeatService>());

var app = builder.Build();

app.UseOwlProtectRequestCorrelation();
app.MapOwlProtectOperationalEndpoints();
app.MapGet("/diagnostics", ([FromServices] GatewayHeartbeatService service, GatewayTrustBundleStore trustBundleStore, GatewayHeartbeatState heartbeatState) => Results.Ok(new
{
    heartbeat = service.LastHeartbeat,
    heartbeatPublisher = heartbeatState.Snapshot(),
    scorecard = GatewayDiagnostics.ScoreGateway(service.LastHeartbeat),
    trustMaterial = trustBundleStore.Current?.Material,
    metrics = "/metrics"
}));

app.Run();

public sealed class GatewayControlPlaneClient(HttpClient httpClient)
{
    public async Task PublishHeartbeatAsync(Gateway heartbeat, IssuedMachineTrustMaterial trustBundle, CancellationToken cancellationToken)
    {
        var path = ControlPlaneApiConventions.Path("/gateways/heartbeat");
        var body = JsonSerializer.SerializeToUtf8Bytes(heartbeat);
        using var request = CreateSignedRequest(HttpMethod.Post, path, body, trustBundle);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IssuedMachineTrustMaterial> RotateTrustMaterialAsync(IssuedMachineTrustMaterial trustBundle, CancellationToken cancellationToken)
    {
        var path = ControlPlaneApiConventions.Path("/gateways/trust-material/rotate");
        using var request = CreateSignedRequest(HttpMethod.Post, path, [], trustBundle);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rotated = await JsonSerializer.DeserializeAsync<IssuedMachineTrustMaterial>(stream, cancellationToken: cancellationToken);
        return rotated ?? throw new InvalidOperationException("Control plane returned an empty trust-material rotation payload.");
    }

    private static HttpRequestMessage CreateSignedRequest(HttpMethod method, string path, byte[] body, IssuedMachineTrustMaterial trustBundle)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("O");
        var nonce = MachineTrustProofCodec.CreateNonce();
        var proof = MachineTrustProofCodec.Sign(
            trustBundle.Material.Id,
            trustBundle.PrivateKeyPem,
            method.Method,
            path,
            timestamp,
            nonce,
            body);

        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(MachineTrustProofCodec.TrustIdHeaderName, proof.TrustId);
        request.Headers.Add(MachineTrustProofCodec.TimestampHeaderName, proof.Timestamp);
        request.Headers.Add(MachineTrustProofCodec.NonceHeaderName, proof.Nonce);
        request.Headers.Add(MachineTrustProofCodec.SignatureHeaderName, proof.Signature);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return request;
    }
}

internal sealed class GatewayHeartbeatService(
    ILogger<GatewayHeartbeatService> logger,
    IConfiguration configuration,
    GatewayControlPlaneClient client,
    GatewayTrustBundleStore trustBundleStore,
    GatewayHeartbeatState heartbeatState) : BackgroundService
{
    private readonly string _gatewayId = configuration["Gateway:Id"] ?? "gw-1";
    private readonly string _gatewayName = configuration["Gateway:Name"] ?? "us-east-core-1";
    private readonly string _region = configuration["Gateway:Region"] ?? "us-east";
    private int _tick;

    public Gateway LastHeartbeat { get; private set; } =
        new("gw-1", "us-east-core-1", "us-east", HealthSeverity.Green, 20, 100, 20, 40, 15, LastHeartbeatUtc: DateTimeOffset.UtcNow);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _tick++;
            LastHeartbeat = BuildHeartbeat();
            var startedAtUtc = DateTimeOffset.UtcNow;
            var start = Stopwatch.GetTimestamp();
            heartbeatState.RecordStart(startedAtUtc);

            using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("gateway.publish_heartbeat");
            activity?.SetTag("owlprotect.gateway.id", _gatewayId);
            activity?.SetTag("owlprotect.gateway.region", _region);

            var outcome = "success";
            var trustBundle = trustBundleStore.Current;
            if (trustBundle is null)
            {
                outcome = "missing_trust_bundle";
                heartbeatState.RecordFailure(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start), "Gateway trust bundle is not loaded.");
                logger.LogWarning("Skipping gateway heartbeat because no trust bundle is loaded. Configure Gateway:TrustBundleFile with issued trust material for gateway {GatewayId}.", _gatewayId);
                OwlProtectTelemetry.GatewayHeartbeatPublishes.Add(1, new TagList { { "outcome", outcome } });
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            try
            {
                if (ShouldRotate(trustBundle.Material))
                {
                    var rotated = await client.RotateTrustMaterialAsync(trustBundle, stoppingToken);
                    trustBundleStore.Save(rotated);
                    trustBundle = rotated;
                    logger.LogInformation("Rotated gateway trust material to {TrustMaterialId}.", rotated.Material.Id);
                }

                await client.PublishHeartbeatAsync(LastHeartbeat, trustBundle, stoppingToken);
                heartbeatState.RecordSuccess(
                    DateTimeOffset.UtcNow,
                    Stopwatch.GetElapsedTime(start),
                    $"Published heartbeat for gateway {_gatewayId}.",
                    affectedItemCount: 1);
            }
            catch (Exception exception)
            {
                outcome = "failure";
                activity?.SetStatus(ActivityStatusCode.Error);
                heartbeatState.RecordFailure(DateTimeOffset.UtcNow, Stopwatch.GetElapsedTime(start), exception.Message);
                logger.LogWarning(exception, "Failed to publish gateway heartbeat.");
            }
            finally
            {
                var durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                activity?.SetTag("owlprotect.gateway.heartbeat.outcome", outcome);
                OwlProtectTelemetry.GatewayHeartbeatPublishes.Add(1, new TagList { { "outcome", outcome } });
                OwlProtectTelemetry.GatewayHeartbeatPublishDuration.Record(durationMs, new TagList { { "outcome", outcome } });
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private Gateway BuildHeartbeat()
    {
        var load = 25 + ((_tick * 7) % 55);
        var latency = 16 + ((_tick * 5) % 28);
        var health = load > 85 || latency > 70
            ? HealthSeverity.Red
            : load > 75 || latency > 40
                ? HealthSeverity.Yellow
                : HealthSeverity.Green;

        return new Gateway(
            _gatewayId,
            _gatewayName,
            _region,
            health,
            load,
            120 + (_tick % 35),
            25 + (_tick % 40),
            45 + (_tick % 30),
            latency,
            LastHeartbeatUtc: DateTimeOffset.UtcNow);
    }

    private static bool ShouldRotate(MachineTrustMaterial material) =>
        material.RotateAfterUtc <= DateTimeOffset.UtcNow || material.ExpiresAtUtc <= DateTimeOffset.UtcNow.AddDays(1);
}

public sealed class GatewayTrustBundleStore(IConfiguration configuration, ILogger<GatewayTrustBundleStore> logger)
{
    private readonly string? _bundlePath = configuration["Gateway:TrustBundleFile"];
    private readonly Lock _gate = new();
    private IssuedMachineTrustMaterial? _current;
    private bool _loaded;
    private DateTimeOffset? _loadedFileTimestampUtc;

    public IssuedMachineTrustMaterial? Current
    {
        get
        {
            lock (_gate)
            {
                var currentFileTimestampUtc = GetCurrentFileTimestampUtc();
                var shouldReload =
                    !_loaded ||
                    _current is null ||
                    (currentFileTimestampUtc is not null && currentFileTimestampUtc != _loadedFileTimestampUtc);

                if (shouldReload)
                {
                    _current = LoadFromDisk();
                    _loaded = _current is not null;
                    _loadedFileTimestampUtc = currentFileTimestampUtc;
                }

                return _current;
            }
        }
    }

    public void Save(IssuedMachineTrustMaterial trustBundle)
    {
        if (string.IsNullOrWhiteSpace(_bundlePath))
        {
            throw new InvalidOperationException("Gateway:TrustBundleFile must be configured to persist rotated trust material.");
        }

        var directory = Path.GetDirectoryName(_bundlePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(trustBundle, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_bundlePath, payload);

        lock (_gate)
        {
            _current = trustBundle;
            _loaded = true;
            _loadedFileTimestampUtc = GetCurrentFileTimestampUtc();
        }
    }

    private DateTimeOffset? GetCurrentFileTimestampUtc()
    {
        if (string.IsNullOrWhiteSpace(_bundlePath) || !File.Exists(_bundlePath))
        {
            return null;
        }

        return File.GetLastWriteTimeUtc(_bundlePath);
    }

    private IssuedMachineTrustMaterial? LoadFromDisk()
    {
        if (string.IsNullOrWhiteSpace(_bundlePath))
        {
            logger.LogWarning("Gateway trust bundle file is not configured.");
            return null;
        }

        if (!File.Exists(_bundlePath))
        {
            logger.LogWarning("Gateway trust bundle file '{TrustBundleFile}' was not found.", _bundlePath);
            return null;
        }

        var payload = File.ReadAllText(_bundlePath);
        var trustBundle = JsonSerializer.Deserialize<IssuedMachineTrustMaterial>(payload, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new System.Text.Json.Serialization.JsonStringEnumConverter()
            }
        });
        if (trustBundle is null)
        {
            logger.LogWarning("Gateway trust bundle file '{TrustBundleFile}' could not be parsed.", _bundlePath);
            return null;
        }

        return trustBundle;
    }
}
