using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class DiagnosticsSamplerWorker(
    ILogger<DiagnosticsSamplerWorker> logger,
    ClientSessionState state,
    LocalPostureCollector postureCollector,
    IOptions<WindowsClientOptions> options) : BackgroundService
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromMilliseconds(1500);
    private const int ProbeAttempts = 3;
    private ThroughputSnapshot? _throughputSnapshot;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var current = state.GetStatus();
            if (current.Connected)
            {
                try
                {
                    var nextStatus = await BuildUpdatedStatusAsync(current, stoppingToken);
                    state.UpdateDiagnostics(nextStatus);
                    await state.PublishHealthSampleAsync(nextStatus, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Client diagnostics sampling failed.");
                }
            }

            logger.LogDebug("Client diagnostics sampled at {Time}.", DateTimeOffset.UtcNow);
            await Task.Delay(SampleInterval, stoppingToken);
        }
    }

    private async Task<ClientStatus> BuildUpdatedStatusAsync(ClientStatus current, CancellationToken cancellationToken)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("windowsclient.diagnostics.sample");
        var posture = postureCollector.Collect(current.DeviceId).Status;
        var measurement = await MeasureConnectivityAsync(cancellationToken);
        var preserveRecoveryNarrative = !string.Equals(current.RecoveryState, "Connected", StringComparison.Ordinal);
        var nextState = preserveRecoveryNarrative
            ? ParseConnectionState(current.State)
            : DetermineConnectionState(posture, measurement);
        var sample = BuildHealthSample(current.DeviceId, nextState, measurement, posture);
        var diagnostics = GatewayDiagnostics.ClassifyDevice(BuildDiagnosticDevice(current, nextState, posture, measurement.SampledAtUtc), sample, session: null, gateway: null);

        var timeline = current.Timeline;
        if (!string.Equals(current.State, nextState.ToString(), StringComparison.Ordinal))
        {
            timeline = AppendTimeline(timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Diagnostics moved to {nextState} ({measurement.LatencyMs} ms latency, {measurement.JitterMs} ms jitter).");
        }

        activity?.SetTag("owlprotect.client.connection_state", nextState.ToString());
        activity?.SetTag("owlprotect.client.route_healthy", measurement.RouteHealthy);
        OwlProtectTelemetry.ClientDiagnosticsSamples.Add(1, new TagList
        {
            { "state", nextState.ToString() },
            { "route_healthy", measurement.RouteHealthy }
        });
        OwlProtectTelemetry.ClientNetworkLatency.Record(measurement.LatencyMs, new TagList
        {
            { "state", nextState.ToString() }
        });
        OwlProtectTelemetry.ClientNetworkPacketLoss.Record((double)measurement.PacketLossPercent, new TagList
        {
            { "state", nextState.ToString() }
        });

        return current with
        {
            State = nextState.ToString(),
            DiagnosticScope = preserveRecoveryNarrative ? current.DiagnosticScope : diagnostics.Scope.ToString(),
            UserMessage = preserveRecoveryNarrative ? current.UserMessage : diagnostics.Summary,
            DiagnosticDetail = preserveRecoveryNarrative ? current.DiagnosticDetail : diagnostics.Detail,
            Timeline = timeline,
            LatencyMs = measurement.LatencyMs,
            JitterMs = measurement.JitterMs,
            SignalStrengthPercent = measurement.SignalStrengthPercent,
            ThroughputMbps = measurement.ThroughputMbps,
            Posture = posture
        };
    }

    private async Task<NetworkMeasurement> MeasureConnectivityAsync(CancellationToken cancellationToken)
    {
        var sampledAtUtc = DateTimeOffset.UtcNow;
        var activeInterface = GetPrimaryInterface();
        var throughputMbps = MeasureThroughputMbps(activeInterface, sampledAtUtc);
        var signalStrengthPercent = TryReadWirelessSignalPercent() ?? (activeInterface is null ? 0 : 100);
        var endpoint = TryResolveControlPlaneEndpoint();
        if (endpoint is null)
        {
            return new NetworkMeasurement(sampledAtUtc, 0, 0, 100m, signalStrengthPercent, false, false, throughputMbps);
        }

        var dnsReachable = await TryResolveHostAsync(endpoint.Value.Host, cancellationToken);
        var latencies = await ProbeTcpLatenciesAsync(endpoint.Value.Host, endpoint.Value.Port, cancellationToken);
        var packetLossPercent = decimal.Round((ProbeAttempts - latencies.Count) * 100m / ProbeAttempts, 1);
        var latencyMs = latencies.Count == 0 ? 0 : (int)Math.Round(latencies.Average());
        var jitterMs = latencies.Count < 2
            ? 0
            : (int)Math.Round(latencies.Zip(latencies.Skip(1), (left, right) => Math.Abs(left - right)).Average());
        var routeHealthy = latencies.Count > 0 && packetLossPercent < 50m;

        return new NetworkMeasurement(
            sampledAtUtc,
            latencyMs,
            jitterMs,
            packetLossPercent,
            signalStrengthPercent,
            dnsReachable,
            routeHealthy,
            throughputMbps);
    }

    private async Task<List<int>> ProbeTcpLatenciesAsync(string host, int port, CancellationToken cancellationToken)
    {
        var latencies = new List<int>(ProbeAttempts);
        for (var attempt = 0; attempt < ProbeAttempts; attempt++)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ProbeTimeout);

            var started = Stopwatch.GetTimestamp();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, timeout.Token);
                latencies.Add((int)Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException)
            {
            }

            if (attempt < ProbeAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }
        }

        return latencies;
    }

    private static async Task<bool> TryResolveHostAsync(string host, CancellationToken cancellationToken)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out _))
        {
            return true;
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private (string Host, int Port)? TryResolveControlPlaneEndpoint()
    {
        if (!Uri.TryCreate(options.Value.ControlPlaneBaseUrl, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var port = uri.IsDefaultPort
            ? string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : uri.Port;
        return (uri.Host, port);
    }

    private int MeasureThroughputMbps(NetworkInterface? activeInterface, DateTimeOffset observedAtUtc)
    {
        if (activeInterface is null)
        {
            _throughputSnapshot = null;
            return 0;
        }

        long totalBytes;
        try
        {
            var statistics = activeInterface.GetIPv4Statistics();
            totalBytes = statistics.BytesReceived + statistics.BytesSent;
        }
        catch
        {
            _throughputSnapshot = null;
            return 0;
        }

        var previous = _throughputSnapshot;
        _throughputSnapshot = new ThroughputSnapshot(activeInterface.Id, totalBytes, observedAtUtc);

        if (previous is null || !string.Equals(previous.InterfaceId, activeInterface.Id, StringComparison.Ordinal))
        {
            return 0;
        }

        var elapsedSeconds = (observedAtUtc - previous.ObservedAtUtc).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return 0;
        }

        var deltaBytes = Math.Max(0, totalBytes - previous.TotalBytes);
        return (int)Math.Round((deltaBytes * 8d) / (elapsedSeconds * 1_000_000d));
    }

    private static NetworkInterface? GetPrimaryInterface() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(candidate => candidate.OperationalStatus == OperationalStatus.Up)
            .Where(candidate => candidate.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel)
            .OrderByDescending(candidate => candidate.GetIPProperties().GatewayAddresses.Count)
            .ThenByDescending(candidate => candidate.Speed)
            .FirstOrDefault();

    private static int? TryReadWirelessSignalPercent()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(1000);
            var match = Regex.Match(output, @"^\s*Signal\s*:\s*(\d+)%\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
            return match.Success && int.TryParse(match.Groups[1].Value, out var signal)
                ? Math.Clamp(signal, 0, 100)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static ConnectionState DetermineConnectionState(ClientPostureStatus posture, NetworkMeasurement measurement)
    {
        if (!posture.Compliant)
        {
            return ConnectionState.PolicyBlocked;
        }

        if (!measurement.RouteHealthy)
        {
            return !measurement.DnsReachable || measurement.SignalStrengthPercent < 50
                ? ConnectionState.LocalNetworkPoor
                : ConnectionState.ServerUnavailable;
        }

        if (measurement.PacketLossPercent >= 10m || measurement.SignalStrengthPercent < 60)
        {
            return ConnectionState.LocalNetworkPoor;
        }

        if (measurement.ThroughputMbps > 0 && measurement.ThroughputMbps < 20)
        {
            return ConnectionState.LowBandwidth;
        }

        if (measurement.JitterMs >= 25)
        {
            return ConnectionState.HighJitter;
        }

        return ConnectionState.Healthy;
    }

    private static HealthSample BuildHealthSample(string deviceId, ConnectionState state, NetworkMeasurement measurement, ClientPostureStatus posture) =>
        new(
            Guid.NewGuid().ToString("n"),
            deviceId,
            state,
            InferSeverity(state, measurement),
            measurement.LatencyMs,
            measurement.JitterMs,
            measurement.PacketLossPercent,
            measurement.ThroughputMbps,
            measurement.SignalStrengthPercent,
            measurement.DnsReachable,
            measurement.RouteHealthy,
            measurement.SampledAtUtc,
            BuildSampleMessage(state, measurement, posture));

    private static Device BuildDiagnosticDevice(ClientStatus current, ConnectionState state, ClientPostureStatus posture, DateTimeOffset sampledAtUtc) =>
        new(
            current.DeviceId,
            current.DeviceName,
            current.Username,
            "Unknown",
            "Unknown",
            "0.0.0.0",
            posture.Managed,
            posture.Compliant,
            posture.PostureScore,
            state,
            sampledAtUtc,
            RegistrationState: ParseRegistrationState(current.RegistrationState),
            EnrollmentKind: ParseEnrollmentKind(current.EnrollmentKind),
            OperatingSystem: posture.OperatingSystem,
            ComplianceReasons: posture.ComplianceReasons);

    private static HealthSeverity InferSeverity(ConnectionState state, NetworkMeasurement measurement) =>
        state switch
        {
            ConnectionState.Healthy => HealthSeverity.Green,
            ConnectionState.PolicyBlocked or ConnectionState.AuthExpired or ConnectionState.ServerUnavailable => HealthSeverity.Red,
            _ => measurement.PacketLossPercent >= 20m || measurement.SignalStrengthPercent < 50
                ? HealthSeverity.Red
                : HealthSeverity.Yellow
        };

    private static string BuildSampleMessage(ConnectionState state, NetworkMeasurement measurement, ClientPostureStatus posture) =>
        state switch
        {
            ConnectionState.PolicyBlocked => $"Posture score {posture.PostureScore} is blocking enterprise routes.",
            ConnectionState.LocalNetworkPoor => $"Local network quality is degraded with {measurement.PacketLossPercent:0.#}% packet loss and {measurement.SignalStrengthPercent}% signal strength.",
            ConnectionState.LowBandwidth => $"Throughput dropped to {measurement.ThroughputMbps} Mbps across the active network path.",
            ConnectionState.HighJitter => $"Latency variance reached {measurement.JitterMs} ms across repeated control-plane probes.",
            ConnectionState.ServerUnavailable => "The configured control plane did not answer repeated network probes even though the local link is still up.",
            _ => $"Stable network path detected with {measurement.LatencyMs} ms latency and {measurement.ThroughputMbps} Mbps throughput."
        };

    private static DeviceRegistrationState ParseRegistrationState(string value) =>
        Enum.TryParse<DeviceRegistrationState>(value, ignoreCase: true, out var parsed)
            ? parsed
            : DeviceRegistrationState.Pending;

    private static DeviceEnrollmentKind ParseEnrollmentKind(string value) =>
        Enum.TryParse<DeviceEnrollmentKind>(value, ignoreCase: true, out var parsed)
            ? parsed
            : DeviceEnrollmentKind.Bootstrap;

    private static ConnectionState ParseConnectionState(string value) =>
        Enum.TryParse<ConnectionState>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ConnectionState.ServerUnavailable;

    private static IReadOnlyList<string> AppendTimeline(IReadOnlyList<string> existing, string entry) =>
        existing
            .Concat([entry])
            .TakeLast(6)
            .ToArray();

    private sealed record ThroughputSnapshot(
        string InterfaceId,
        long TotalBytes,
        DateTimeOffset ObservedAtUtc);

    private sealed record NetworkMeasurement(
        DateTimeOffset SampledAtUtc,
        int LatencyMs,
        int JitterMs,
        decimal PacketLossPercent,
        int SignalStrengthPercent,
        bool DnsReachable,
        bool RouteHealthy,
        int ThroughputMbps);
}
