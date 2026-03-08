using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OWLProtect.ControlPlane.Api;
using OWLProtect.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, OwlProtectJsonContext.Default);
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin();
    });
});
builder.Services.AddSingleton<InMemoryState>();
builder.Services.AddSingleton<IAuthProvider, EntraAuthProvider>();
builder.Services.AddSingleton<IAuthProvider, GenericOidcAuthProvider>();
builder.Services.AddSingleton<AuthProviderResolver>();

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/bootstrap", (InMemoryState state) => Results.Ok(state.GetBootstrapStatus()));

app.MapPost("/auth/admin/login", (AdminLoginRequest request, InMemoryState state) =>
{
    try
    {
        return Results.Ok(state.LoginAdmin(request.Username, request.Password));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/auth/user/login", (UserLoginRequest request, InMemoryState state) =>
{
    try
    {
        return Results.Ok(state.LoginUser(request.Username));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/auth/provider/login", async (ProviderLoginRequest request, AuthProviderResolver resolver, CancellationToken cancellationToken) =>
{
    try
    {
        var provider = resolver.Resolve(request.ProviderId);
        var result = await provider.ValidateAsync(request.Token, cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/admins", (InMemoryState state) => Results.Ok(state.Snapshot().Admins));
app.MapPost("/admins/default/password", (PasswordChangeRequest request, InMemoryState state) =>
{
    try
    {
        return Results.Ok(state.UpdateAdminPassword(request.CurrentPassword, request.NewPassword));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
app.MapPost("/admins/default/mfa", (InMemoryState state) => Results.Ok(state.EnrollAdminMfa()));

app.MapGet("/users", (InMemoryState state) => Results.Ok(state.Snapshot().Users));
app.MapPost("/users/{userId}/enable", (string userId, InMemoryState state) =>
{
    try
    {
        return Results.Ok(state.EnableUser(userId, "admin"));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
app.MapPost("/users/{userId}/disable", (string userId, InMemoryState state) =>
{
    try
    {
        return Results.Ok(state.DisableUser(userId, "admin", "User disabled by admin."));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/devices", (InMemoryState state) => Results.Ok(state.Snapshot().Devices));
app.MapGet("/gateways", (InMemoryState state) => Results.Ok(state.Snapshot().Gateways));
app.MapPost("/gateways/heartbeat", (Gateway gateway, InMemoryState state) => Results.Ok(state.UpsertGatewayHeartbeat(gateway)));
app.MapGet("/gateway-pools", (InMemoryState state) => Results.Ok(state.Snapshot().GatewayPools));
app.MapGet("/policies", (InMemoryState state) => Results.Ok(state.Snapshot().Policies));
app.MapGet("/sessions", (InMemoryState state) => Results.Ok(state.Snapshot().Sessions));
app.MapGet("/alerts", (InMemoryState state) => Results.Ok(state.Snapshot().Alerts));
app.MapGet("/telemetry/query", (InMemoryState state) => Results.Ok(state.Snapshot().HealthSamples));
app.MapGet("/map/connections", (InMemoryState state) => Results.Ok(state.GetConnectionMap()));
app.MapGet("/auth/providers", (InMemoryState state) => Results.Ok(state.Snapshot().AuthProviders));
app.MapGet("/audit", (InMemoryState state) => Results.Ok(state.Snapshot().AuditEvents));

app.MapPost("/privileged/step-up", (HttpContext context, PrivilegedOperationRequest request, InMemoryState state) =>
{
    var stepUpSatisfied = string.Equals(context.Request.Headers["X-Step-Up"], "approved", StringComparison.OrdinalIgnoreCase);
    if (!state.ValidatePrivilegedOperation(stepUpSatisfied))
    {
        return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
    }

    return Results.Ok(new
    {
        operation = request.OperationName,
        status = "approved"
    });
});

MapSocket(app, "/ws/admin-dashboard", state => state.Snapshot());
MapSocket(app, "/ws/alert-stream", state => state.Snapshot().Alerts);
MapSocket(app, "/ws/gateway-health", state => state.Snapshot().Gateways);
MapSocket(app, "/ws/client-health", state => state.Snapshot().HealthSamples);
MapSocket(app, "/ws/client-session", state => state.Snapshot().Sessions);

app.Run();

static void MapSocket<T>(WebApplication app, string path, Func<InMemoryState, T> payloadFactory)
{
    app.Map(path, async (HttpContext context, InMemoryState state) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(payloadFactory(state));
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, context.RequestAborted);
            await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted);
        }
    });
}
