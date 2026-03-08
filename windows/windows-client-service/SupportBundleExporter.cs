using System.Text.Json;
using Microsoft.Extensions.Options;

namespace OWLProtect.WindowsClientService;

public sealed class SupportBundleExporter(IOptions<WindowsClientOptions> options)
{
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        WriteIndented = true
    };

    public async Task<string> ExportAsync(ClientStatus status, CancellationToken cancellationToken)
    {
        var directory = options.Value.SupportBundleDirectory;
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(
            directory,
            $"owlprotect-support-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");

        var payload = new
        {
            exportedAtUtc = DateTimeOffset.UtcNow,
            machineName = Environment.MachineName,
            userDomain = Environment.UserDomainName,
            protocolVersion = PipeProtocol.Version,
            status
        };

        await File.WriteAllTextAsync(
            filePath,
            JsonSerializer.Serialize(payload, _serializerOptions),
            cancellationToken);

        return filePath;
    }
}
