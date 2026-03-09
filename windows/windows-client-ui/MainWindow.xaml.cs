using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OWLProtect.WindowsClientUi;

public sealed partial class MainWindow : Window
{
    private readonly DispatcherQueueTimer _pollTimer;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        Activated += async (_, _) => await TryLoadStatusAsync();
        _pollTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(5);
        _pollTimer.IsRepeating = true;
        _pollTimer.Tick += async (_, _) => await TryLoadStatusAsync();
        _pollTimer.Start();
        Closed += (_, _) => _pollTimer.Stop();
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(PipeProtocol.ConnectCommand, silentSsoPreferred: true);
    }

    private async void InteractiveButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(PipeProtocol.ConnectCommand, silentSsoPreferred: false);
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(PipeProtocol.DisconnectCommand, silentSsoPreferred: false);
    }

    private async void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(PipeProtocol.SignOutCommand, silentSsoPreferred: false);
    }

    private async void SupportBundleButton_Click(object sender, RoutedEventArgs e)
    {
        await SendCommandAsync(PipeProtocol.SupportBundleCommand, silentSsoPreferred: false);
    }

    private async Task SendCommandAsync(string command, bool silentSsoPreferred)
    {
        try
        {
            var response = await PipeClient.SendCommandAsync(command, silentSsoPreferred);
            ApplyStatus(response.Status);
            if (!response.Success)
            {
                StatusMessageText.Text = response.ErrorMessage ?? response.Status.UserMessage;
                LastErrorText.Text = $"Last error: {response.ErrorCode ?? "unknown"}";
            }
            else if (!string.IsNullOrWhiteSpace(response.ExportPath))
            {
                SupportBundleText.Text = $"Support bundle: {response.ExportPath}";
            }
        }
        catch (Exception)
        {
            StatusMessageText.Text = "The Windows service is not reachable over the named pipe.";
            DiagnosticDetailText.Text = "Start the Windows client service to retrieve enrollment, gateway placement, and diagnostics.";
            ApplyVisualState(
                faceColor: ColorHelper.FromArgb(255, 79, 95, 107),
                eyeColor: ColorHelper.FromArgb(255, 160, 165, 170),
                connectionLabel: "Disconnected",
                qualityLabel: "Quality unknown");
        }
    }

    private void ApplyStatus(ClientStatus status)
    {
        StatusMessageText.Text = status.UserMessage;
        IdentityText.Text = $"User: {status.Username}";
        DeviceText.Text = $"Device: {status.DeviceName} ({status.DeviceId})";
        GatewayText.Text = $"Gateway: {status.CurrentGateway}";
        AuthModeText.Text = $"Auth mode: {status.AuthMode}";
        RecoveryText.Text = $"Recovery: {status.RecoveryState}";
        RegistrationText.Text = $"Registration: {status.RegistrationState} | Enrollment: {status.EnrollmentKind}";
        ExpiryText.Text =
            $"Session revalidate: {FormatTimestamp(status.RevalidateAfterUtc)} | Access token: {FormatTimestamp(status.AccessTokenExpiresAtUtc)}";
        PolicyText.Text = $"Policy: {status.PolicyBundleVersion}";
        RoutesText.Text = $"Routes: {JoinOrFallback(status.Routes)}";
        DnsText.Text = $"DNS zones: {JoinOrFallback(status.DnsZones)}";
        PortsText.Text = $"Ports: {JoinOrFallback(status.Ports.Select(port => port.ToString()).ToArray())}";

        DiagnosticScopeText.Text = $"Diagnostic scope: {status.DiagnosticScope} | State: {status.State}";
        MetricsText.Text =
            $"Latency: {status.LatencyMs} ms | Jitter: {status.JitterMs} ms | Signal: {status.SignalStrengthPercent}% | Throughput: {status.ThroughputMbps} Mbps";
        FailoverText.Text = $"Failover chain: {JoinOrFallback(status.FailoverGateways)}";
        DiagnosticDetailText.Text = status.DiagnosticDetail;
        LastErrorText.Text = $"Last error: {status.LastErrorCode ?? "none"}";
        LastUpdatedText.Text = $"Last updated: {status.UpdatedAtUtc.LocalDateTime:g}";

        PostureSummaryText.Text =
            $"Managed: {YesNo(status.Posture.Managed)} | Compliant: {YesNo(status.Posture.Compliant)} | Score: {status.Posture.PostureScore}";
        PostureSignalsText.Text =
            $"Signals: BitLocker {YesNo(status.Posture.BitLockerEnabled)} | Defender {YesNo(status.Posture.DefenderHealthy)} | Firewall {YesNo(status.Posture.FirewallEnabled)} | Secure Boot {YesNo(status.Posture.SecureBootEnabled)} | Tamper {YesNo(status.Posture.TamperProtectionEnabled)}";
        ComplianceReasonsText.Text = $"Compliance reasons: {JoinOrFallback(status.Posture.ComplianceReasons)}";
        OsText.Text = $"OS: {status.Posture.OperatingSystem}";

        SupportBundleText.Text = $"Support bundle: {status.LastSupportBundlePath ?? "not exported"}";
        TimelineText.Text = $"Recent activity: {JoinOrFallback(status.Timeline)}";
        var visual = OwlIconRenderer.Describe(status);
        ApplyVisualState(visual.FaceColor, visual.EyeColor, visual.Label, visual.QualityLabel);
    }

    private async Task TryLoadStatusAsync()
    {
        await SendCommandAsync(PipeProtocol.StatusCommand, silentSsoPreferred: true);
    }

    private void ApplyVisualState(Color faceColor, Color eyeColor, string connectionLabel, string qualityLabel)
    {
        OwlFace.Fill = new SolidColorBrush(faceColor);
        OwlLeftEar.Fill = new SolidColorBrush(faceColor);
        OwlRightEar.Fill = new SolidColorBrush(faceColor);
        OwlLeftEye.Fill = new SolidColorBrush(eyeColor);
        OwlRightEye.Fill = new SolidColorBrush(eyeColor);
        ConnectionBadge.Background = new SolidColorBrush(faceColor);
        QualityBadge.Background = new SolidColorBrush(eyeColor);
        ConnectionBadgeText.Text = connectionLabel;
        QualityBadgeText.Text = qualityLabel;
    }

    private static string JoinOrFallback(IReadOnlyList<string> values) =>
        values.Count == 0 ? "--" : string.Join(" | ", values);

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value is null ? "--" : value.Value.LocalDateTime.ToString("g");

    private static string YesNo(bool value) => value ? "yes" : "no";
}
