using System.Net.WebSockets;
using System.Text.Json;
using OWLProtect.ControlPlane.Api;
using OWLProtect.Core;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));
builder.Services.Configure<SecretManagementOptions>(builder.Configuration.GetSection("SecretManagement"));
builder.Services.Configure<AuditRetentionOptions>(builder.Configuration.GetSection("AuditRetention"));

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
    builder.Services.AddSingleton<IAuditWriter>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IAuditRetentionRepository>(serviceProvider => serviceProvider.GetRequiredService<PostgresStore>());
    builder.Services.AddSingleton<IPlatformSessionStore, PostgresPlatformSessionStore>();
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
    builder.Services.AddSingleton<IAuditWriter>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
    builder.Services.AddSingleton<IAuditRetentionRepository>(serviceProvider => serviceProvider.GetRequiredService<InMemoryState>());
    builder.Services.AddSingleton<IPlatformSessionStore, InMemoryPlatformSessionStore>();
}

builder.Services.AddSingleton<IBootstrapAdminCredentialsProvider, ConfigurationBootstrapAdminCredentialsProvider>();
builder.Services.AddSingleton<AuditRetentionService>();
builder.Services.AddHostedService<AuditRetentionWorker>();
builder.Services.AddSingleton<IAuthProvider, EntraAuthProvider>();
builder.Services.AddSingleton<IAuthProvider, GenericOidcAuthProvider>();
builder.Services.AddSingleton<OpenIdConnectTokenValidator>();
builder.Services.AddSingleton<AuthProviderResolver>();

var app = builder.Build();
if (string.Equals(persistenceProvider, "postgres", StringComparison.OrdinalIgnoreCase))
{
    await app.Services.GetRequiredService<PostgresStore>().InitializeAsync(app.Lifetime.ApplicationStopping);
}

app.UseCors();
app.Use(async (context, next) =>
{
    ControlPlaneSecurity.AttachIdentity(context);
    await next();
});
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/bootstrap", (IBootstrapService bootstrapService) => Results.Ok(bootstrapService.GetBootstrapStatus()));

app.MapPost("/auth/admin/login", (AdminLoginRequest request, IBootstrapService bootstrapService, IPlatformSessionStore sessionStore) =>
{
    try
    {
        var admin = bootstrapService.LoginAdmin(request.Username, request.Password);
        var issuedSession = sessionStore.CreateSession(PlatformSessionKind.Admin, admin.Id, admin.Username, admin.Role.ToString());
        return Results.Ok(BuildAuthSessionResponse(issuedSession, admin, user: null));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/auth/user/login", (UserLoginRequest request, IBootstrapService bootstrapService, IPlatformSessionStore sessionStore) =>
{
    try
    {
        var user = bootstrapService.LoginUser(request.Username);
        var issuedSession = sessionStore.CreateSession(PlatformSessionKind.User, user.Id, user.Username, role: null);
        return Results.Ok(BuildAuthSessionResponse(issuedSession, admin: null, user));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/auth/provider/login", async (ProviderLoginRequest request, AuthProviderResolver resolver, IAuthProviderConfigRepository authProviderConfigRepository, IUserRepository userRepository, IPlatformSessionStore sessionStore, IAuditWriter auditWriter, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await resolver.ValidateAsync(request.ProviderId, request.Token, cancellationToken);
        var providerConfig = authProviderConfigRepository.ListAuthProviders().Single(item => string.Equals(item.Id, request.ProviderId, StringComparison.OrdinalIgnoreCase));
        var existingUser = userRepository.ListUsers().SingleOrDefault(user =>
            string.Equals(user.Id, ExternalIdentityUserFactory.CreateUserId(providerConfig.Id, result.Subject), StringComparison.Ordinal));

        if (existingUser is not null && !existingUser.Enabled)
        {
            auditWriter.WriteAudit(result.Username, "provider-login", "user", existingUser.Id, "failure", $"External identity login was rejected because user '{existingUser.Id}' is disabled.");
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var provisionedUser = userRepository.UpsertUser(
            ExternalIdentityUserFactory.CreateOrUpdateUser(existingUser, providerConfig, result),
            $"idp:{request.ProviderId}:{result.Username}");
        var issuedSession = sessionStore.CreateSession(PlatformSessionKind.User, provisionedUser.Id, provisionedUser.Username, role: null);
        auditWriter.WriteAudit(result.Username, "provider-login", "user", provisionedUser.Id, "success", $"Validated provider '{request.ProviderId}' token and issued a platform session with {provisionedUser.GroupIds.Count} mapped groups.");
        return Results.Ok(BuildAuthSessionResponse(issuedSession, admin: null, provisionedUser));
    }
    catch (AuthProviderValidationException exception)
    {
        auditWriter.WriteAudit(request.ProviderId, "provider-login", "auth-provider", request.ProviderId, "failure", $"[{exception.DiagnosticCode}] {exception.AuditDetail}");
        return Results.BadRequest(new { error = exception.SafeMessage, diagnosticCode = exception.DiagnosticCode });
    }
    catch (InvalidOperationException exception)
    {
        auditWriter.WriteAudit(request.ProviderId, "provider-login", "auth-provider", request.ProviderId, "failure", exception.Message);
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapPost("/auth/session/refresh", (RefreshSessionRequest request, IPlatformSessionStore sessionStore, IAdminRepository adminRepository, IUserRepository userRepository) =>
{
    try
    {
        var issuedSession = sessionStore.Refresh(request.RefreshToken);
        return Results.Ok(BuildAuthSessionResponseFromStore(issuedSession, adminRepository, userRepository));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

var sessionGroup = app.MapGroup(string.Empty);
sessionGroup.AddEndpointFilterFactory(ControlPlaneSecurity.RequireSession);
sessionGroup.MapGet("/auth/me", (HttpContext context) =>
{
    var identity = ControlPlaneSecurity.GetIdentity(context)!;
    return Results.Ok(new
    {
        identity.Session,
        identity.Admin,
        identity.User
    });
});
sessionGroup.MapPost("/auth/session/revoke", (HttpContext context, IPlatformSessionStore sessionStore) =>
{
    var identity = ControlPlaneSecurity.GetIdentity(context)!;
    var revoked = sessionStore.RevokeSession(identity.Session.Id, identity.Actor, "Session revoked by subject.");
    return revoked ? Results.NoContent() : Results.NotFound();
});

var userSessionGroup = app.MapGroup(string.Empty);
userSessionGroup.AddEndpointFilterFactory(ControlPlaneSecurity.RequireUser);
userSessionGroup.MapPost("/auth/client/session", (HttpContext context, ClientSessionIssueRequest request, IDeviceRepository deviceRepository, IPlatformSessionStore sessionStore) =>
{
    var identity = ControlPlaneSecurity.GetIdentity(context)!;
    var user = identity.User!;
    var device = deviceRepository.ListDevices().SingleOrDefault(item => item.Id == request.DeviceId);
    if (device is null)
    {
        return Results.BadRequest(new { error = "Device not found." });
    }

    if (!string.Equals(device.UserId, user.Id, StringComparison.Ordinal))
    {
        context.RequestServices.GetRequiredService<IAuditWriter>()
            .WriteAudit(identity.Actor, "client-session-issue", "device", request.DeviceId, "failure", "User attempted to issue a client session for a device they do not own.");
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    if (!device.Managed)
    {
        context.RequestServices.GetRequiredService<IAuditWriter>()
            .WriteAudit(identity.Actor, "client-session-issue", "device", request.DeviceId, "failure", "User attempted to issue a client session for an unmanaged device.");
        return Results.BadRequest(new { error = "Device must be managed before a client session can be issued." });
    }

    var issuedSession = sessionStore.CreateSession(PlatformSessionKind.Client, device.Id, device.Name, role: null);
    return Results.Ok(new ClientAuthSessionResponse(
        issuedSession.Session,
        new SessionTokenPair(
            issuedSession.AccessToken,
            issuedSession.Session.AccessTokenExpiresAtUtc,
            issuedSession.RefreshToken,
            issuedSession.Session.RefreshTokenExpiresAtUtc),
        user,
        device));
});

var bootstrapAdminGroup = app.MapGroup(string.Empty);
bootstrapAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, AdminRole.SuperAdmin, requireCompliantAdmin: false, requireStepUp: false));
bootstrapAdminGroup.MapPost("/admins/default/password", (HttpContext context, PasswordChangeRequest request, IBootstrapService bootstrapService) =>
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
bootstrapAdminGroup.MapPost("/admins/default/mfa", (IBootstrapService bootstrapService) => Results.Ok(bootstrapService.EnrollAdminMfa()));

var compliantAdminGroup = app.MapGroup(string.Empty);
compliantAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, AdminRole.ReadOnly, requireCompliantAdmin: true, requireStepUp: false));
compliantAdminGroup.MapPost("/auth/step-up", (HttpContext context, StepUpRequest request, IAdminRepository adminRepository, IPlatformSessionStore sessionStore) =>
{
    var identity = ControlPlaneSecurity.GetIdentity(context)!;
    var admin = adminRepository.ListAdmins().Single(item => item.Id == identity.Session.SubjectId);
    if (!PasswordProtector.Verify(request.Password, admin.Password))
    {
        context.RequestServices.GetRequiredService<IAuditWriter>()
            .WriteAudit(identity.Actor, "platform-session-step-up", "platform-session", identity.Session.Id, "failure", "Step-up password verification failed.");
        return Results.BadRequest(new { error = "Step-up verification failed." });
    }

    var stepUpSession = sessionStore.MarkStepUp(identity.Session.Id, DateTimeOffset.UtcNow.AddMinutes(10), identity.Actor);
    return Results.Ok(new { session = stepUpSession, stepUpExpiresAtUtc = stepUpSession.StepUpExpiresAtUtc });
});
compliantAdminGroup.MapGet("/admins", (IAdminRepository adminRepository) => Results.Ok(adminRepository.ListAdmins()));
compliantAdminGroup.MapGet("/admins/query", (IAdminRepository adminRepository, string? username, string? role) =>
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
compliantAdminGroup.MapGet("/users", (IUserRepository userRepository) => Results.Ok(userRepository.ListUsers()));
compliantAdminGroup.MapGet("/users/query", (IUserRepository userRepository, string? username, bool? enabled, string? provider) =>
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
compliantAdminGroup.MapGet("/devices", (IDeviceRepository deviceRepository) => Results.Ok(deviceRepository.ListDevices()));
compliantAdminGroup.MapGet("/devices/query", (IDeviceRepository deviceRepository, string? userId, bool? managed, bool? compliant, string? state) =>
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
compliantAdminGroup.MapGet("/gateways", (IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.ListGateways()));
compliantAdminGroup.MapGet("/gateways/query", (IGatewayRepository gatewayRepository, string? region, string? health) =>
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
compliantAdminGroup.MapGet("/gateway-pools", (IGatewayPoolRepository gatewayPoolRepository) => Results.Ok(gatewayPoolRepository.ListGatewayPools()));
compliantAdminGroup.MapGet("/policies", (IPolicyRepository policyRepository) => Results.Ok(policyRepository.ListPolicies()));
compliantAdminGroup.MapGet("/policies/query", (IPolicyRepository policyRepository, string? name, string? cidr, string? dnsZone) =>
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
compliantAdminGroup.MapGet("/sessions", (ISessionRepository sessionRepository) => Results.Ok(sessionRepository.ListSessions()));
compliantAdminGroup.MapGet("/sessions/query", (ISessionRepository sessionRepository, string? userId, string? deviceId, string? gatewayId) =>
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
compliantAdminGroup.MapGet("/alerts", (IAlertRepository alertRepository) => Results.Ok(alertRepository.ListAlerts()));
compliantAdminGroup.MapGet("/telemetry/query", (IHealthSampleRepository healthSampleRepository) => Results.Ok(healthSampleRepository.ListHealthSamples()));
compliantAdminGroup.MapGet("/map/connections", (IDeviceRepository deviceRepository) => Results.Ok(deviceRepository.GetConnectionMap()));
compliantAdminGroup.MapGet("/auth/providers", (IAuthProviderConfigRepository authProviderConfigRepository) => Results.Ok(authProviderConfigRepository.ListAuthProviders()));
compliantAdminGroup.MapGet("/audit", (IAuditRepository auditRepository) => Results.Ok(auditRepository.ListAuditEvents()));
compliantAdminGroup.MapGet("/audit/checkpoints", (IAuditRepository auditRepository) => Results.Ok(auditRepository.ListAuditRetentionCheckpoints()));
compliantAdminGroup.MapGet("/audit/export", (IAuditRepository auditRepository, DateTimeOffset? before, int? limit) =>
{
    var boundedLimit = Math.Clamp(limit ?? 1000, 1, 10_000);
    var events = before.HasValue
        ? auditRepository.ListAuditEventsForExport(before.Value, boundedLimit)
        : [.. auditRepository.ListAuditEvents().OrderBy(evt => evt.Sequence).TakeLast(boundedLimit)];

    return Results.Ok(new AuditExportResponse(DateTimeOffset.UtcNow, before, events.Count, events));
});

var operatorAdminGroup = app.MapGroup(string.Empty);
operatorAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, AdminRole.Operator, requireCompliantAdmin: true, requireStepUp: false));
operatorAdminGroup.MapPost("/users", (HttpContext context, User user, IUserRepository userRepository) =>
{
    var actor = ControlPlaneSecurity.GetIdentity(context)!.Actor;
    var upsert = string.IsNullOrWhiteSpace(user.Id) ? user with { Id = Guid.NewGuid().ToString("n") } : user;
    return Results.Ok(userRepository.UpsertUser(upsert, actor));
});
operatorAdminGroup.MapPost("/users/{userId}/enable", (HttpContext context, string userId, IUserRepository userRepository) =>
{
    try
    {
        return Results.Ok(userRepository.EnableUser(userId, ControlPlaneSecurity.GetIdentity(context)!.Actor));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
operatorAdminGroup.MapPost("/devices", (Device device, IDeviceRepository deviceRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(device.Id) ? device with { Id = Guid.NewGuid().ToString("n") } : device;
    return Results.Ok(deviceRepository.UpsertDevice(upsert));
});
operatorAdminGroup.MapPost("/gateways", (Gateway gateway, IGatewayRepository gatewayRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(gateway.Id) ? gateway with { Id = Guid.NewGuid().ToString("n") } : gateway;
    return Results.Ok(gatewayRepository.UpsertGatewayHeartbeat(upsert));
});
operatorAdminGroup.MapPost("/policies", (PolicyRule policy, IPolicyRepository policyRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(policy.Id) ? policy with { Id = Guid.NewGuid().ToString("n") } : policy;
    return Results.Ok(policyRepository.UpsertPolicy(upsert));
});
operatorAdminGroup.MapPost("/sessions", (TunnelSession session, ISessionRepository sessionRepository) =>
{
    var upsert = string.IsNullOrWhiteSpace(session.Id)
        ? session with { Id = Guid.NewGuid().ToString("n"), ConnectedAtUtc = session.ConnectedAtUtc == default ? DateTimeOffset.UtcNow : session.ConnectedAtUtc }
        : session;
    return Results.Ok(sessionRepository.UpsertSession(upsert));
});

var privilegedAdminGroup = app.MapGroup(string.Empty);
privilegedAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, AdminRole.Operator, requireCompliantAdmin: true, requireStepUp: true));
privilegedAdminGroup.MapPost("/users/{userId}/disable", (HttpContext context, string userId, IUserRepository userRepository, IPlatformSessionStore platformSessionStore) =>
{
    try
    {
        var actor = ControlPlaneSecurity.GetIdentity(context)!.Actor;
        var updatedUser = userRepository.DisableUser(userId, actor, "User disabled by admin.");
        platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.User, userId, actor, "User was disabled.");
        foreach (var deviceId in context.RequestServices.GetRequiredService<IDeviceRepository>().ListDevices().Where(device => device.UserId == userId).Select(device => device.Id))
        {
            platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Client, deviceId, actor, "Owning user was disabled.");
        }
        return Results.Ok(updatedUser);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
privilegedAdminGroup.MapDelete("/users/{userId}", (HttpContext context, string userId, IUserRepository userRepository, IPlatformSessionStore platformSessionStore) =>
{
    var actor = ControlPlaneSecurity.GetIdentity(context)!.Actor;
    platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.User, userId, actor, "User was deleted.");
    foreach (var deviceId in context.RequestServices.GetRequiredService<IDeviceRepository>().ListDevices().Where(device => device.UserId == userId).Select(device => device.Id))
    {
        platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Client, deviceId, actor, "Owning user was deleted.");
    }
    userRepository.DeleteUser(userId);
    return Results.NoContent();
});
privilegedAdminGroup.MapDelete("/devices/{deviceId}", (HttpContext context, string deviceId, IDeviceRepository deviceRepository, IPlatformSessionStore platformSessionStore) =>
{
    platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Client, deviceId, ControlPlaneSecurity.GetIdentity(context)!.Actor, "Device was deleted.");
    deviceRepository.DeleteDevice(deviceId);
    return Results.NoContent();
});
privilegedAdminGroup.MapDelete("/gateways/{gatewayId}", (string gatewayId, IGatewayRepository gatewayRepository) =>
{
    gatewayRepository.DeleteGateway(gatewayId);
    return Results.NoContent();
});
privilegedAdminGroup.MapDelete("/policies/{policyId}", (string policyId, IPolicyRepository policyRepository) =>
{
    policyRepository.DeletePolicy(policyId);
    return Results.NoContent();
});
privilegedAdminGroup.MapPost("/sessions/{sessionId}/revoke", (HttpContext context, string sessionId, ISessionRepository sessionRepository) =>
{
    var revoked = sessionRepository.RevokeSession(sessionId, ControlPlaneSecurity.GetIdentity(context)!.Actor, "Session revoked by admin.");
    return revoked ? Results.Ok(new { sessionId, status = "revoked" }) : Results.NotFound();
});
privilegedAdminGroup.MapPost("/privileged/step-up", (HttpContext context, PrivilegedOperationRequest request) => Results.Ok(new
{
    actor = ControlPlaneSecurity.GetIdentity(context)!.Actor,
    operation = request.OperationName,
    status = "approved"
}));
privilegedAdminGroup.MapPost("/audit/retention/run", async (AuditRetentionService auditRetentionService, CancellationToken cancellationToken) =>
{
    var result = await auditRetentionService.RunRetentionAsync(cancellationToken);
    return Results.Ok(new AuditRetentionRunResponse(result.ExportedEventCount, result.Checkpoint));
});

// Gateway/device trust material is still pending; keep the current heartbeat surface available until mTLS-backed machine auth lands.
app.MapPost("/gateways/heartbeat", (Gateway gateway, IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.UpsertGatewayHeartbeat(gateway)));

MapSocket<IDashboardQueryService, DashboardSnapshot>(app, "/ws/admin-dashboard", service => service.Snapshot(), AdminRole.ReadOnly);
MapSocket<IAlertRepository, IReadOnlyList<Alert>>(app, "/ws/alert-stream", service => service.ListAlerts(), AdminRole.ReadOnly);
MapSocket<IGatewayRepository, IReadOnlyList<Gateway>>(app, "/ws/gateway-health", service => service.ListGateways(), AdminRole.ReadOnly);
MapSocket<IHealthSampleRepository, IReadOnlyList<HealthSample>>(app, "/ws/client-health", service => service.ListHealthSamples(), AdminRole.ReadOnly);
MapSocket<ISessionRepository, IReadOnlyList<TunnelSession>>(app, "/ws/client-session", service => service.ListSessions(), AdminRole.ReadOnly);

app.Run();

static AuthSessionResponse BuildAuthSessionResponse(IssuedPlatformSession issuedSession, AdminAccount? admin, User? user) =>
    new(
        issuedSession.Session,
        new SessionTokenPair(
            issuedSession.AccessToken,
            issuedSession.Session.AccessTokenExpiresAtUtc,
            issuedSession.RefreshToken,
            issuedSession.Session.RefreshTokenExpiresAtUtc),
        admin,
        user);

static AuthSessionResponse BuildAuthSessionResponseFromStore(IssuedPlatformSession issuedSession, IAdminRepository adminRepository, IUserRepository userRepository)
{
    var admin = issuedSession.Session.Kind == PlatformSessionKind.Admin
        ? adminRepository.ListAdmins().SingleOrDefault(item => item.Id == issuedSession.Session.SubjectId)
        : null;
    var user = issuedSession.Session.Kind == PlatformSessionKind.User
        ? userRepository.ListUsers().SingleOrDefault(item => item.Id == issuedSession.Session.SubjectId)
        : null;

    return BuildAuthSessionResponse(issuedSession, admin, user);
}

static void MapSocket<TService, TPayload>(
    WebApplication app,
    string path,
    Func<TService, TPayload> payloadFactory,
    AdminRole minimumRole)
    where TService : notnull
{
    app.Map(path, async (HttpContext context, TService service) =>
    {
        var identity = ControlPlaneSecurity.GetIdentity(context);
        if (identity?.Admin is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (identity.Admin.MustChangePassword || !identity.Admin.MfaEnrolled)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (minimumRole == AdminRole.Operator && identity.Admin.Role == AdminRole.ReadOnly)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

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
