using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace OWLProtect.WindowsClientUi;

internal static class PipeClient
{
    private const string PipeName = "owlprotect-client";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<PipeResponse> RequestStatusAsync(CancellationToken cancellationToken = default) =>
        await SendCommandAsync(PipeProtocol.StatusCommand, silentSsoPreferred: true, cancellationToken);

    public static async Task<PipeResponse> SendCommandAsync(string command, bool silentSsoPreferred, CancellationToken cancellationToken = default)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        await client.ConnectAsync(timeout.Token);

        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        var request = new PipeRequest(PipeProtocol.Version, Guid.NewGuid().ToString("n"), command, silentSsoPreferred);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request, SerializerOptions));

        var payload = await reader.ReadLineAsync(timeout.Token) ?? string.Empty;
        return JsonSerializer.Deserialize<PipeResponse>(payload, SerializerOptions)
            ?? throw new InvalidOperationException("The Windows service returned an invalid response.");
    }
}
