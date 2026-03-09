using OWLProtect.Core;
using OWLProtect.WindowsClientService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "OWLProtect Windows Client";
});
builder.Services.AddOwlProtectObservability(builder.Configuration, builder.Environment, "windows-client-service", includeAspNetCoreInstrumentation: false);
builder.Services.Configure<WindowsClientOptions>(builder.Configuration.GetSection("WindowsClient"));
builder.Services.AddHttpClient<ControlPlaneClient>();
builder.Services.AddSingleton<WindowsAuthBroker>();
builder.Services.AddSingleton<LocalPostureCollector>();
builder.Services.AddSingleton<SupportBundleExporter>();
builder.Services.AddSingleton<ClientSessionState>();
builder.Services.AddSingleton<PipeProtocolServer>();
builder.Services.AddHostedService<ClientRevalidationWorker>();
builder.Services.AddHostedService<DiagnosticsSamplerWorker>();
builder.Services.AddHostedService<NamedPipeWorker>();

var host = builder.Build();
host.Run();
