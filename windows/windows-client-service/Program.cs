using OWLProtect.WindowsClientService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<WindowsClientOptions>(builder.Configuration.GetSection("WindowsClient"));
builder.Services.AddHttpClient<ControlPlaneClient>();
builder.Services.AddSingleton<WindowsAuthBroker>();
builder.Services.AddSingleton<LocalPostureCollector>();
builder.Services.AddSingleton<SupportBundleExporter>();
builder.Services.AddSingleton<ClientSessionState>();
builder.Services.AddSingleton<PipeProtocolServer>();
builder.Services.AddHostedService<DiagnosticsSamplerWorker>();
builder.Services.AddHostedService<NamedPipeWorker>();

var host = builder.Build();
host.Run();
