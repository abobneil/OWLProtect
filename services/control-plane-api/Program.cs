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
builder.Services.AddSingleton<IBootstrapService>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IAdminRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IUserRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IDeviceRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IGatewayRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IGatewayPoolRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IPolicyRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<ISessionRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IHealthSampleRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IAlertRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IAuthProviderConfigRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IAuditRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
builder.Services.AddSingleton<IAuthProvider, EntraAuthProvider>();
builder.Services.AddSingleton<IAuthProvider, GenericOidcAuthProvider>();
builder.Services.AddSingleton<AuthProviderResolver>();

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/bootstrap", (IBootstrapService bootstrapService) => Results.Ok(bootstrapService.GetBootstrapStatus()));

app.MapPost("/auth/admin/login", (AdminLoginRequest request, IBootstrapService bootstrapService) =>
{
    try
    {
        return Results.Ok(bootstrapService.LoginAdmin(request.Username, request.Password));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/auth/user/login", (UserLoginRequest request, IBootstrapService bootstrapService) =>
{
    try
    {
        return Results.Ok(bootstrapService.LoginUser(request.Username));
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

app.MapGet("/admins", (IAdminRepository adminRepository) => Results.Ok(adminRepository.ListAdmins()));
app.MapPost("/admins/default/password", (PasswordChangeRequest request, IBootstrapService bootstrapService) =>
{
    try
    {
        return Results.Ok(bootstrapService.UpdateAdminPassword(request.CurrentPassword, request.NewPassword));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
app.MapPost("/admins/default/mfa", (IBootstrapService bootstrapService) => Results.Ok(bootstrapService.EnrollAdminMfa()));

app.MapGet("/users", (IUserRepository userRepository) => Results.Ok(userRepository.ListUsers()));
app.MapPost("/users/{userId}/enable", (string userId, IUserRepository userRepository) =>
{
    try
    {
        return Results.Ok(userRepository.EnableUser(userId, "admin"));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
app.MapPost("/users/{userId}/disable", (string userId, IUserRepository userRepository) =>
{
    try
    {
        return Results.Ok(userRepository.DisableUser(userId, "admin", "User disabled by admin."));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/devices", (IDeviceRepository deviceRepository) => Results.Ok(deviceRepository.ListDevices()));
app.MapGet("/gateways", (IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.ListGateways()));
app.MapPost("/gateways/heartbeat", (Gateway gateway, IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.UpsertGatewayHeartbeat(gateway)));
app.MapGet("/gateway-pools", (IGatewayPoolRepository gatewayPoolRepository) => Results.Ok(gatewayPoolRepository.ListGatewayPools()));
app.MapGet("/policies", (IPolicyRepository policyRepository) => Results.Ok(policyRepository.ListPolicies()));
app.MapGet("/sessions", (ISessionRepository sessionRepository) => Results.Ok(sessionRepository.ListSessions()));
app.MapGet("/alerts", (IAlertRepository alertRepository) => Results.Ok(alertRepository.ListAlerts()));
app.MapGet("/telemetry/query", (IHealthSampleRepository healthSampleRepository) => Results.Ok(healthSampleRepository.ListHealthSamples()));
app.MapGet("/map/connections", (IDeviceRepository deviceRepository) => Results.Ok(deviceRepository.GetConnectionMap()));
app.MapGet("/auth/providers", (IAuthProviderConfigRepository authProviderConfigRepository) => Results.Ok(authProviderConfigRepository.ListAuthProviders()));
app.MapGet("/audit", (IAuditRepository auditRepository) => Results.Ok(auditRepository.ListAuditEvents()));

app.MapPost("/privileged/step-up", (HttpContext context, PrivilegedOperationRequest request, IBootstrapService bootstrapService) =>
{
    var stepUpSatisfied = string.Equals(context.Request.Headers["X-Step-Up"], "approved", StringComparison.OrdinalIgnoreCase);
    if (!bootstrapService.ValidatePrivilegedOperation(stepUpSatisfied))
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
MapSocket(app, "/ws/alert-stream", state => state.ListAlerts());
MapSocket(app, "/ws/gateway-health", state => state.ListGateways());
MapSocket(app, "/ws/client-health", state => state.ListHealthSamples());
MapSocket(app, "/ws/client-session", state => state.ListSessions());

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
