using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OWLProtect.ControlPlane.Api;
using OWLProtect.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));

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
var persistenceProvider = builder.Configuration["Persistence:Provider"] ?? "in-memory";
if (string.Equals(persistenceProvider, "postgres", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<PostgresStore>();
    builder.Services.AddSingleton<IBootstrapService>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IDashboardQueryService>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IAdminRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IUserRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IDeviceRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IGatewayRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IGatewayPoolRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IPolicyRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<ISessionRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IHealthSampleRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IAlertRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IAuthProviderConfigRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IAuditRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
}
else
{
    builder.Services.AddSingleton<InMemoryState>();
    builder.Services.AddSingleton<IBootstrapService>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
    builder.Services.AddSingleton<IDashboardQueryService>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
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
}
builder.Services.AddSingleton<IAuthProvider, EntraAuthProvider>();
builder.Services.AddSingleton<IAuthProvider, GenericOidcAuthProvider>();
builder.Services.AddSingleton<AuthProviderResolver>();

var app = builder.Build();
if (string.Equals(persistenceProvider, "postgres", StringComparison.OrdinalIgnoreCase))
{
    await app.Services.GetRequiredService<PostgresStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
}

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
app.MapGet("/admins/query", (IAdminRepository adminRepository, string? username, string? role) =>
{
    var query = adminRepository.ListAdmins().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(username))
    {
        query = query.Where(admin => admin.Username.Contains(username, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(role) && Enum.TryParse<AdminRole>(role, true, out var parsedRole))
    {
        query = query.Where(admin => admin.Role == parsedRole);
    }

    return Results.Ok(query.ToArray());
});
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
app.MapGet("/users/query", (IUserRepository userRepository, string? username, bool? enabled, string? provider) =>
{
    var query = userRepository.ListUsers().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(username))
    {
        query = query.Where(user => user.Username.Contains(username, StringComparison.OrdinalIgnoreCase));
    }

    if (enabled.HasValue)
    {
        query = query.Where(user => user.Enabled == enabled.Value);
    }

    if (!string.IsNullOrWhiteSpace(provider))
    {
        query = query.Where(user => string.Equals(user.Provider, provider, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.ToArray());
});
app.MapPost("/users", (User user, IUserRepository userRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(user.Id) ? user with { Id = Guid.NewGuid().ToString("n") } : user;
    return Results.Ok(userRepository.UpsertUser(upsert));
});
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
app.MapDelete("/users/{userId}", (string userId, IUserRepository userRepository) =>
{
    userRepository.DeleteUser(userId);
    return Results.NoContent();
});

app.MapGet("/devices", (IDeviceRepository deviceRepository) => Results.Ok(deviceRepository.ListDevices()));
app.MapGet("/devices/query", (IDeviceRepository deviceRepository, string? userId, bool? managed, bool? compliant, string? state) =>
{
    var query = deviceRepository.ListDevices().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(userId))
    {
        query = query.Where(device => device.UserId == userId);
    }

    if (managed.HasValue)
    {
        query = query.Where(device => device.Managed == managed.Value);
    }

    if (compliant.HasValue)
    {
        query = query.Where(device => device.Compliant == compliant.Value);
    }

    if (!string.IsNullOrWhiteSpace(state) && Enum.TryParse<ConnectionState>(state, true, out var parsedState))
    {
        query = query.Where(device => device.ConnectionState == parsedState);
    }

    return Results.Ok(query.ToArray());
});
app.MapPost("/devices", (Device device, IDeviceRepository deviceRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(device.Id) ? device with { Id = Guid.NewGuid().ToString("n") } : device;
    return Results.Ok(deviceRepository.UpsertDevice(upsert));
});
app.MapDelete("/devices/{deviceId}", (string deviceId, IDeviceRepository deviceRepository) =>
{
    deviceRepository.DeleteDevice(deviceId);
    return Results.NoContent();
});
app.MapGet("/gateways", (IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.ListGateways()));
app.MapGet("/gateways/query", (IGatewayRepository gatewayRepository, string? region, string? health) =>
{
    var query = gatewayRepository.ListGateways().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(region))
    {
        query = query.Where(gateway => string.Equals(gateway.Region, region, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(health) && Enum.TryParse<HealthSeverity>(health, true, out var parsedHealth))
    {
        query = query.Where(gateway => gateway.Health == parsedHealth);
    }

    return Results.Ok(query.ToArray());
});
app.MapPost("/gateways", (Gateway gateway, IGatewayRepository gatewayRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(gateway.Id) ? gateway with { Id = Guid.NewGuid().ToString("n") } : gateway;
    return Results.Ok(gatewayRepository.UpsertGatewayHeartbeat(upsert));
});
app.MapPost("/gateways/heartbeat", (Gateway gateway, IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.UpsertGatewayHeartbeat(gateway)));
app.MapDelete("/gateways/{gatewayId}", (string gatewayId, IGatewayRepository gatewayRepository) =>
{
    gatewayRepository.DeleteGateway(gatewayId);
    return Results.NoContent();
});
app.MapGet("/gateway-pools", (IGatewayPoolRepository gatewayPoolRepository) => Results.Ok(gatewayPoolRepository.ListGatewayPools()));
app.MapGet("/policies", (IPolicyRepository policyRepository) => Results.Ok(policyRepository.ListPolicies()));
app.MapGet("/policies/query", (IPolicyRepository policyRepository, string? name, string? cidr, string? dnsZone) =>
{
    var query = policyRepository.ListPolicies().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(name))
    {
        query = query.Where(policy => policy.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(cidr))
    {
        query = query.Where(policy => policy.Cidrs.Any(item => item.Contains(cidr, StringComparison.OrdinalIgnoreCase)));
    }

    if (!string.IsNullOrWhiteSpace(dnsZone))
    {
        query = query.Where(policy => policy.DnsZones.Any(item => item.Contains(dnsZone, StringComparison.OrdinalIgnoreCase)));
    }

    return Results.Ok(query.ToArray());
});
app.MapPost("/policies", (PolicyRule policy, IPolicyRepository policyRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(policy.Id) ? policy with { Id = Guid.NewGuid().ToString("n") } : policy;
    return Results.Ok(policyRepository.UpsertPolicy(upsert));
});
app.MapDelete("/policies/{policyId}", (string policyId, IPolicyRepository policyRepository) =>
{
    policyRepository.DeletePolicy(policyId);
    return Results.NoContent();
});
app.MapGet("/sessions", (ISessionRepository sessionRepository) => Results.Ok(sessionRepository.ListSessions()));
app.MapGet("/sessions/query", (ISessionRepository sessionRepository, string? userId, string? deviceId, string? gatewayId) =>
{
    var query = sessionRepository.ListSessions().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(userId))
    {
        query = query.Where(session => session.UserId == userId);
    }

    if (!string.IsNullOrWhiteSpace(deviceId))
    {
        query = query.Where(session => session.DeviceId == deviceId);
    }

    if (!string.IsNullOrWhiteSpace(gatewayId))
    {
        query = query.Where(session => session.GatewayId == gatewayId);
    }

    return Results.Ok(query.ToArray());
});
app.MapPost("/sessions", (TunnelSession session, ISessionRepository sessionRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(session.Id)
        ? session with { Id = Guid.NewGuid().ToString("n"), ConnectedAtUtc = session.ConnectedAtUtc == default ? DateTimeOffset.UtcNow : session.ConnectedAtUtc }
        : session;
    return Results.Ok(sessionRepository.UpsertSession(upsert));
});
app.MapPost("/sessions/{sessionId}/revoke", (string sessionId, ISessionRepository sessionRepository) =>
{
    var revoked = sessionRepository.RevokeSession(sessionId, "admin", "Session revoked by admin.");
    return revoked ? Results.Ok(new { sessionId, status = "revoked" }) : Results.NotFound();
});
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

MapSocket<IDashboardQueryService, DashboardSnapshot>(app, "/ws/admin-dashboard", service => service.Snapshot());
MapSocket<IAlertRepository, IReadOnlyList<Alert>>(app, "/ws/alert-stream", service => service.ListAlerts());
MapSocket<IGatewayRepository, IReadOnlyList<Gateway>>(app, "/ws/gateway-health", service => service.ListGateways());
MapSocket<IHealthSampleRepository, IReadOnlyList<HealthSample>>(app, "/ws/client-health", service => service.ListHealthSamples());
MapSocket<ISessionRepository, IReadOnlyList<TunnelSession>>(app, "/ws/client-session", service => service.ListSessions());

app.Run();

static void MapSocket<TService, TPayload>(WebApplication app, string path, Func<TService, TPayload> payloadFactory)
    where TService : notnull
{
    app.Map(path, async (HttpContext context, TService service) =>
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        while (socket.State == WebSocketState.Open && !context.RequestAborted.IsCancellationRequested)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(payloadFactory(service));
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, context.RequestAborted);
            await Task.Delay(TimeSpan.FromSeconds(5), context.RequestAborted);
        }
    });
}
