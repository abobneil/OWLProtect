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
var api = app.MapGroup(ControlPlaneApiConventions.ApiPrefix);
var webSocketApi = api.MapGroup("/ws");

api.MapGet("/bootstrap", (IBootstrapService bootstrapService) => Results.Ok(bootstrapService.GetBootstrapStatus()));

api.MapPost("/auth/admin/login", (AdminLoginRequest request, IBootstrapService bootstrapService, IPlatformSessionStore sessionStore) =>
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

api.MapPost("/auth/user/login", (UserLoginRequest request, IBootstrapService bootstrapService, IPlatformSessionStore sessionStore) =>
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

api.MapPost("/auth/provider/login", async (ProviderLoginRequest request, AuthProviderResolver resolver, IAuthProviderConfigRepository authProviderConfigRepository, IUserRepository userRepository, IPlatformSessionStore sessionStore, IAuditWriter auditWriter, CancellationToken cancellationToken) =>
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
            return Results.Json(new ApiErrorResponse("User is disabled.", "user_disabled"), statusCode: StatusCodes.Status403Forbidden);
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

api.MapPost("/auth/session/refresh", (RefreshSessionRequest request, IPlatformSessionStore sessionStore, IAdminRepository adminRepository, IUserRepository userRepository) =>
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

var sessionGroup = api.MapGroup(string.Empty);
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

var userSessionGroup = api.MapGroup(string.Empty);
userSessionGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireUser(factoryContext, next, "user.session.issue-client"));
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
        return Results.Json(new ApiErrorResponse("Device does not belong to the authenticated user.", "device_ownership_required"), statusCode: StatusCodes.Status403Forbidden);
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

var bootstrapAdminGroup = api.MapGroup(string.Empty);
bootstrapAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, "admin.bootstrap.manage", AdminRole.SuperAdmin, requireCompliantAdmin: false, requireStepUp: false));
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

var compliantAdminGroup = api.MapGroup(string.Empty);
compliantAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, "admin.read", AdminRole.ReadOnly, requireCompliantAdmin: true, requireStepUp: false));
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
compliantAdminGroup.MapGet("/admins/{adminId}", (string adminId, IAdminRepository adminRepository) =>
{
    var admin = adminRepository.ListAdmins().SingleOrDefault(item => item.Id == adminId);
    return admin is null ? NotFound("Admin not found.", "admin_not_found") : Results.Ok(admin);
});
compliantAdminGroup.MapGet("/admins/query", (IAdminRepository adminRepository, string? username, string? role) =>
{
    var query = adminRepository.ListAdmins().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(username))
    {
        query = query.Where(admin => admin.Username.Contains(username, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(role))
    {
        if (!Enum.TryParse<AdminRole>(role, true, out var parsedRole))
        {
        return ValidationProblemResponse([$"Role '{role}' is not valid."]);
        }

        query = query.Where(admin => admin.Role == parsedRole);
    }

    return Results.Ok(query.ToArray());
});
compliantAdminGroup.MapGet("/users", (IUserRepository userRepository) => Results.Ok(userRepository.ListUsers()));
compliantAdminGroup.MapGet("/users/{userId}", (string userId, IUserRepository userRepository) =>
{
    var user = userRepository.ListUsers().SingleOrDefault(item => item.Id == userId);
    return user is null ? NotFound("User not found.", "user_not_found") : Results.Ok(user);
});
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
        if (!IsKnownProvider(provider))
        {
            return ValidationProblemResponse([$"Provider '{provider}' is not valid."]);
        }

        query = query.Where(user => string.Equals(user.Provider, provider, StringComparison.OrdinalIgnoreCase));
    }

    return Results.Ok(query.ToArray());
});
compliantAdminGroup.MapGet("/devices", (IDeviceRepository deviceRepository) => Results.Ok(deviceRepository.ListDevices()));
compliantAdminGroup.MapGet("/devices/{deviceId}", (string deviceId, IDeviceRepository deviceRepository) =>
{
    var device = deviceRepository.ListDevices().SingleOrDefault(item => item.Id == deviceId);
    return device is null ? NotFound("Device not found.", "device_not_found") : Results.Ok(device);
});
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

    if (!string.IsNullOrWhiteSpace(state))
    {
        if (!Enum.TryParse<ConnectionState>(state, true, out var parsedState))
        {
            return ValidationProblemResponse([$"Connection state '{state}' is not valid."]);
        }

        query = query.Where(device => device.ConnectionState == parsedState);
    }

    return Results.Ok(query.ToArray());
});
compliantAdminGroup.MapGet("/gateways", (IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.ListGateways()));
compliantAdminGroup.MapGet("/gateways/{gatewayId}", (string gatewayId, IGatewayRepository gatewayRepository) =>
{
    var gateway = gatewayRepository.ListGateways().SingleOrDefault(item => item.Id == gatewayId);
    return gateway is null ? NotFound("Gateway not found.", "gateway_not_found") : Results.Ok(gateway);
});
compliantAdminGroup.MapGet("/gateways/query", (IGatewayRepository gatewayRepository, string? region, string? health) =>
{
    var query = gatewayRepository.ListGateways().AsEnumerable();
    if (!string.IsNullOrWhiteSpace(region))
    {
        query = query.Where(gateway => string.Equals(gateway.Region, region, StringComparison.OrdinalIgnoreCase));
    }

    if (!string.IsNullOrWhiteSpace(health))
    {
        if (!Enum.TryParse<HealthSeverity>(health, true, out var parsedHealth))
        {
            return ValidationProblemResponse([$"Health severity '{health}' is not valid."]);
        }

        query = query.Where(gateway => gateway.Health == parsedHealth);
    }

    return Results.Ok(query.ToArray());
});
compliantAdminGroup.MapGet("/gateway-pools", (IGatewayPoolRepository gatewayPoolRepository) => Results.Ok(gatewayPoolRepository.ListGatewayPools()));
compliantAdminGroup.MapGet("/policies", (IPolicyRepository policyRepository) => Results.Ok(policyRepository.ListPolicies()));
compliantAdminGroup.MapGet("/policies/{policyId}", (string policyId, IPolicyRepository policyRepository) =>
{
    var policy = policyRepository.ListPolicies().SingleOrDefault(item => item.Id == policyId);
    return policy is null ? NotFound("Policy not found.", "policy_not_found") : Results.Ok(policy);
});
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
compliantAdminGroup.MapGet("/sessions/{sessionId}", (string sessionId, ISessionRepository sessionRepository) =>
{
    var session = sessionRepository.ListSessions().SingleOrDefault(item => item.Id == sessionId);
    return session is null ? NotFound("Session not found.", "session_not_found") : Results.Ok(session);
});
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

var operatorAdminGroup = api.MapGroup(string.Empty);
operatorAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, "admin.operator.write", AdminRole.Operator, requireCompliantAdmin: true, requireStepUp: false));
operatorAdminGroup.MapPost("/users", (HttpContext context, UserUpsertRequest request, IUserRepository userRepository) =>
{
    var errors = ManagementValidation.ValidateUserRequest(request, userRepository.ListUsers());
    if (errors.Count > 0)
    {
        return ValidationProblemResponse(errors);
    }

    var actor = ControlPlaneSecurity.GetIdentity(context)!.Actor;
    var upsert = ManagementValidation.ToUser(request, string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("n") : request.Id);
    return Results.Ok(userRepository.UpsertUser(upsert, actor));
});
operatorAdminGroup.MapPost("/users/{userId}/enable", (HttpContext context, string userId, IUserRepository userRepository) =>
{
    try
    {
        if (userRepository.ListUsers().All(user => user.Id != userId))
        {
            return NotFound("User not found.", "user_not_found");
        }

        return Results.Ok(userRepository.EnableUser(userId, ControlPlaneSecurity.GetIdentity(context)!.Actor));
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});
operatorAdminGroup.MapPost("/devices", (DeviceUpsertRequest request, IDeviceRepository deviceRepository, IUserRepository userRepository) =>
{
    var errors = ManagementValidation.ValidateDeviceRequest(request, userRepository.ListUsers());
    if (errors.Count > 0)
    {
        return ValidationProblemResponse(errors);
    }

    var upsert = ManagementValidation.ToDevice(request, string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("n") : request.Id);
    return Results.Ok(deviceRepository.UpsertDevice(upsert));
});
operatorAdminGroup.MapPost("/gateways", (GatewayUpsertRequest request, IGatewayRepository gatewayRepository) =>
{
    var errors = ManagementValidation.ValidateGatewayRequest(request);
    if (errors.Count > 0)
    {
        return ValidationProblemResponse(errors);
    }

    var upsert = ManagementValidation.ToGateway(request, string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("n") : request.Id);
    return Results.Ok(gatewayRepository.UpsertGatewayHeartbeat(upsert));
});
operatorAdminGroup.MapPost("/policies", (PolicyUpsertRequest request, IPolicyRepository policyRepository) =>
{
    var errors = ManagementValidation.ValidatePolicyRequest(request, policyRepository.ListPolicies());
    if (errors.Count > 0)
    {
        return ValidationProblemResponse(errors);
    }

    var upsert = ManagementValidation.ToPolicy(request, string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("n") : request.Id);
    return Results.Ok(policyRepository.UpsertPolicy(upsert));
});
operatorAdminGroup.MapPost("/sessions", (SessionUpsertRequest request, ISessionRepository sessionRepository, IUserRepository userRepository, IDeviceRepository deviceRepository, IGatewayRepository gatewayRepository) =>
{
    var errors = ManagementValidation.ValidateSessionRequest(request, userRepository.ListUsers(), deviceRepository.ListDevices(), gatewayRepository.ListGateways());
    if (errors.Count > 0)
    {
        return ValidationProblemResponse(errors);
    }

    var upsert = ManagementValidation.ToSession(request, string.IsNullOrWhiteSpace(request.Id) ? Guid.NewGuid().ToString("n") : request.Id);
    return Results.Ok(sessionRepository.UpsertSession(upsert));
});

var superAdminPrivilegedGroup = api.MapGroup(string.Empty);
superAdminPrivilegedGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, "admin.super.privileged.write", AdminRole.SuperAdmin, requireCompliantAdmin: true, requireStepUp: true));
superAdminPrivilegedGroup.MapPost("/admins", (HttpContext context, AdminUpsertRequest request, IAdminRepository adminRepository) =>
{
    var existingAdmins = adminRepository.ListAdmins();
    var creating = string.IsNullOrWhiteSpace(request.Id);
    var errors = ManagementValidation.ValidateAdminRequest(request, creating, existingAdmins);
    if (errors.Count > 0)
    {
        return ValidationProblemResponse(errors);
    }

    var existing = creating ? null : existingAdmins.SingleOrDefault(item => item.Id == request.Id);
    if (!creating && existing is null)
    {
        return NotFound("Admin not found.", "admin_not_found");
    }

    var passwordHash = !string.IsNullOrWhiteSpace(request.Password)
        ? PasswordProtector.Hash(request.Password)
        : existing!.Password;
    var admin = ManagementValidation.ToAdmin(request, request.Id ?? Guid.NewGuid().ToString("n"), passwordHash);
    return Results.Ok(adminRepository.UpsertAdmin(admin, ControlPlaneSecurity.GetIdentity(context)!.Actor));
});
superAdminPrivilegedGroup.MapDelete("/admins/{adminId}", (HttpContext context, string adminId, IAdminRepository adminRepository, IPlatformSessionStore platformSessionStore) =>
{
    try
    {
        var admin = adminRepository.ListAdmins().SingleOrDefault(item => item.Id == adminId);
        if (admin is null)
        {
            return NotFound("Admin not found.", "admin_not_found");
        }

        platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Admin, adminId, ControlPlaneSecurity.GetIdentity(context)!.Actor, "Admin account was deleted.");
        var deleted = adminRepository.DeleteAdmin(adminId, ControlPlaneSecurity.GetIdentity(context)!.Actor);
        return deleted ? Results.NoContent() : NotFound("Admin not found.", "admin_not_found");
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

var privilegedAdminGroup = api.MapGroup(string.Empty);
privilegedAdminGroup.AddEndpointFilterFactory((factoryContext, next) =>
    ControlPlaneSecurity.RequireAdmin(factoryContext, next, "admin.privileged.write", AdminRole.Operator, requireCompliantAdmin: true, requireStepUp: true));
privilegedAdminGroup.MapPost("/users/{userId}/disable", (HttpContext context, string userId, IUserRepository userRepository, IPlatformSessionStore platformSessionStore) =>
{
    try
    {
        if (userRepository.ListUsers().All(user => user.Id != userId))
        {
            return NotFound("User not found.", "user_not_found");
        }

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
    if (userRepository.ListUsers().All(user => user.Id != userId))
    {
        return NotFound("User not found.", "user_not_found");
    }

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
    if (deviceRepository.ListDevices().All(device => device.Id != deviceId))
    {
        return NotFound("Device not found.", "device_not_found");
    }

    platformSessionStore.RevokeSubjectSessions(PlatformSessionKind.Client, deviceId, ControlPlaneSecurity.GetIdentity(context)!.Actor, "Device was deleted.");
    deviceRepository.DeleteDevice(deviceId);
    return Results.NoContent();
});
privilegedAdminGroup.MapDelete("/gateways/{gatewayId}", (string gatewayId, IGatewayRepository gatewayRepository) =>
{
    if (gatewayRepository.ListGateways().All(gateway => gateway.Id != gatewayId))
    {
        return NotFound("Gateway not found.", "gateway_not_found");
    }

    gatewayRepository.DeleteGateway(gatewayId);
    return Results.NoContent();
});
privilegedAdminGroup.MapDelete("/policies/{policyId}", (string policyId, IPolicyRepository policyRepository) =>
{
    if (policyRepository.ListPolicies().All(policy => policy.Id != policyId))
    {
        return NotFound("Policy not found.", "policy_not_found");
    }

    policyRepository.DeletePolicy(policyId);
    return Results.NoContent();
});
privilegedAdminGroup.MapPost("/sessions/{sessionId}/revoke", (HttpContext context, string sessionId, ISessionRepository sessionRepository) =>
{
    if (sessionRepository.ListSessions().All(session => session.Id != sessionId))
    {
        return NotFound("Session not found.", "session_not_found");
    }

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
api.MapPost("/gateways/heartbeat", (Gateway gateway, IGatewayRepository gatewayRepository) => Results.Ok(gatewayRepository.UpsertGatewayHeartbeat(gateway)));

MapSocket<IDashboardQueryService, DashboardSnapshot>(webSocketApi, "/admin-dashboard", service => service.Snapshot(), AdminRole.ReadOnly, "ws.dashboard.read");
MapSocket<IAlertRepository, IReadOnlyList<Alert>>(webSocketApi, "/alert-stream", service => service.ListAlerts(), AdminRole.ReadOnly, "ws.alerts.read");
MapSocket<IGatewayRepository, IReadOnlyList<Gateway>>(webSocketApi, "/gateway-health", service => service.ListGateways(), AdminRole.ReadOnly, "ws.gateways.read");
MapSocket<IHealthSampleRepository, IReadOnlyList<HealthSample>>(webSocketApi, "/client-health", service => service.ListHealthSamples(), AdminRole.ReadOnly, "ws.health.read");
MapSocket<ISessionRepository, IReadOnlyList<TunnelSession>>(webSocketApi, "/client-session", service => service.ListSessions(), AdminRole.ReadOnly, "ws.sessions.read");

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

static IResult ValidationProblemResponse(IReadOnlyList<string> details) =>
    Results.Json(new ValidationErrorResponse("Request validation failed.", "validation_failed", details), statusCode: StatusCodes.Status400BadRequest);

static IResult NotFound(string error, string errorCode) =>
    Results.Json(new ApiErrorResponse(error, errorCode), statusCode: StatusCodes.Status404NotFound);

static bool IsKnownProvider(string provider) =>
    string.Equals(provider, "local", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(provider, "entra", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(provider, "oidc", StringComparison.OrdinalIgnoreCase);

static void MapSocket<TService, TPayload>(
    IEndpointRouteBuilder routes,
    string path,
    Func<TService, TPayload> payloadFactory,
    AdminRole minimumRole,
    string policyName)
    where TService : notnull
{
    routes.Map(path, async (HttpContext context, TService service) =>
    {
        var requirement = ControlPlaneAuthorizationPolicies.Admin(policyName, minimumRole, requireCompliantAdmin: true, requireStepUp: false);
        if (!ControlPlaneSecurity.TryAuthorize(context, requirement, out var failure))
        {
            var denied = ControlPlaneSecurity.BuildDeniedResult(context, requirement, failure!);
            await denied.ExecuteAsync(context);
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
