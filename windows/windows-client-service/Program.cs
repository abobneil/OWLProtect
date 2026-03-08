using OWLProtect.WindowsClientService;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<ClientSessionState>();
builder.Services.AddSingleton<PipeProtocolServer>();
builder.Services.AddHostedService<DiagnosticsSamplerWorker>();
builder.Services.AddHostedService<NamedPipeWorker>();

var host = builder.Build();
host.Run();

