using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace OWLProtect.WindowsClientUi;

public sealed partial class MainWindow : Window
{
    private const string PipeName = "owlprotect-client";

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Activated += async (_, _) => await TryLoadStatusAsync();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(new PipeCommand("connect", SilentSsoPreferred: true));
    }

    private async void InteractiveButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(new PipeCommand("connect", SilentSsoPreferred: false));
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(new PipeCommand("disconnect", SilentSsoPreferred: false));
    }

    private async Task SendCommandAsync(PipeCommand command)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(2000);

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            await writer.WriteLineAsync(JsonSerializer.Serialize(command));
            var payload = await reader.ReadLineAsync() ?? string.Empty;
            var status = JsonSerializer.Deserialize<ClientStatus>(payload);
            if (status is null)
            {
                StatusMessageText.Text = "The Windows service returned an invalid response.";
                return;
            }

            StatusMessageText.Text = status.UserMessage;
            GatewayText.Text = $"Gateway: {status.CurrentGateway}";
            DiagnosticScopeText.Text = $"Diagnostic scope: {status.DiagnosticScope} | State: {status.State}";
            DiagnosticDetailText.Text = status.DiagnosticDetail;
            FailoverText.Text = status.FailoverGateways.Length > 0
                ? $"Failover chain: {string.Join(" -> ", status.FailoverGateways)}"
                : "Failover chain: no warm failover candidate";
            TimelineText.Text = status.Timeline.Length > 0
                ? $"Recent activity: {string.Join(" | ", status.Timeline)}"
                : "Recent activity: no timeline entries yet.";
            MetricsText.Text =
                $"Latency: {status.LatencyMs} ms | Jitter: {status.JitterMs} ms | Signal: {status.SignalStrengthPercent}% | Throughput: {status.ThroughputMbps} Mbps";
        }
        catch (Exception)
        {
            StatusMessageText.Text = "The Windows service is not reachable over the named pipe.";
            DiagnosticDetailText.Text = "Start the Windows client service to retrieve gateway placement and diagnostics.";
        }
    }

    private async Task TryLoadStatusAsync()
    {
        if (string.IsNullOrWhiteSpace(StatusMessageText.Text) || StatusMessageText.Text.StartsWith("Disconnected", StringComparison.Ordinal))
        {
            await SendCommandAsync(new PipeCommand("status", SilentSsoPreferred: true));
        }
    }
}
