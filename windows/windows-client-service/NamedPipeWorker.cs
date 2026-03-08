using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace OWLProtect.WindowsClientService;

public sealed class NamedPipeWorker(PipeProtocolServer server) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => server.RunAsync(stoppingToken);
}

public sealed class PipeProtocolServer(
    ILogger<PipeProtocolServer> logger,
    ClientSessionState state)
{
    private const string PipeName = "owlprotect-client";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            await pipe.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var payload = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            var command = JsonSerializer.Deserialize<PipeCommand>(payload);
            if (command is null)
            {
                await writer.WriteLineAsync("{\"error\":\"Invalid payload.\"}");
                continue;
            }

            var response = command.Command.ToLowerInvariant() switch
            {
                "status" => state.GetStatus(),
                "connect" => state.Connect(command.SilentSsoPreferred),
                "disconnect" => state.Disconnect(),
                _ => state.GetStatus() with { UserMessage = "Unknown command received by service." }
            };

            logger.LogInformation("Handled pipe command {Command}.", command.Command);
            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
    }
}

