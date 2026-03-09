using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Microsoft.Extensions.Options;
using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class ClientSessionState(
    ILogger<ClientSessionState> logger,
    WindowsAuthBroker authBroker,
    ControlPlaneClient controlPlaneClient,
    LocalPostureCollector postureCollector,
    SupportBundleExporter supportBundleExporter,
    IOptions<WindowsClientOptions> options)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _statusGate = new();
    private ClientStatus _status = CreateInitialStatus();
    private ControlPlaneSessionTokenPair? _tokens;
    private PlatformSession? _platformSession;
    private User? _user;
    private Device? _device;
    private ResolvedPolicyBundle? _bundle;
    private GatewayPlacement? _placement;

    public ClientStatus GetStatus()
    {
        lock (_statusGate)
        {
            EvaluateRecoveryState();
            return _status;
        }
    }

    public async Task<ClientStatus> ConnectAsync(bool silentSsoPreferred, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var start = Stopwatch.GetTimestamp();
        var outcome = "success";
        var authMode = "unknown";
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("windowsclient.connect");
        activity?.SetTag("owlprotect.client.silent_sso_preferred", silentSsoPreferred);
        try
        {
            AppendStatusTimeline($"Beginning {(silentSsoPreferred ? "silent SSO" : "interactive")} connect workflow.");

            var authSession = await authBroker.AuthenticateAsync(silentSsoPreferred, cancellationToken);
            authMode = authSession.Mode;
            activity?.SetTag("owlprotect.auth.mode", authSession.Mode);
            var authResponse = authSession.Response;
            _tokens = authResponse.Tokens;
            _platformSession = authResponse.Session;
            _user = authResponse.User ?? throw new InvalidOperationException("The control plane did not return an end-user identity.");
            var enrollmentPosture = postureCollector.Collect("pending-enrollment");

            var enrollment = await controlPlaneClient.EnrollDeviceAsync(
                _tokens.AccessToken,
                BuildEnrollmentRequest(enrollmentPosture.Status),
                cancellationToken);

            _device = enrollment.Device;
            var posture = postureCollector.Collect(_device.Id);

            _device = await controlPlaneClient.SubmitPostureAsync(
                _tokens.AccessToken,
                _device.Id,
                posture.Report,
                cancellationToken);

            var clientSession = await controlPlaneClient.IssueClientSessionAsync(
                _tokens.AccessToken,
                _device.Id,
                cancellationToken);

            _tokens = clientSession.Tokens;
            _platformSession = clientSession.Session;
            _user = clientSession.User;
            _device = clientSession.Device;
            _bundle = clientSession.Bundle;
            _placement = clientSession.Placement;

            var connectedMessage = $"{authSession.Summary} Gateway '{clientSession.Placement.GatewayName}' is active.";
            UpdateStatus(status => status with
            {
                Connected = true,
                Username = clientSession.User.Username,
                DeviceId = clientSession.Device.Id,
                CurrentGateway = clientSession.Placement.GatewayName,
                State = ConnectionState.Healthy.ToString(),
                DiagnosticScope = DiagnosticScope.Healthy.ToString(),
                UserMessage = connectedMessage,
                DiagnosticDetail = BuildConnectedDetail(clientSession),
                AuthMode = authSession.Mode,
                RecoveryState = "Connected",
                RegistrationState = clientSession.Device.RegistrationState.ToString(),
                EnrollmentKind = clientSession.Device.EnrollmentKind.ToString(),
                PolicyBundleVersion = clientSession.Bundle.Version,
                Routes = clientSession.Bundle.Cidrs.ToArray(),
                DnsZones = clientSession.Bundle.DnsZones.ToArray(),
                Ports = clientSession.Bundle.Ports.ToArray(),
                FailoverGateways = ResolveFailoverGateways(clientSession.Placement).ToArray(),
                Timeline = AppendTimeline(
                    status.Timeline,
                    $"{DateTimeOffset.UtcNow:HH:mm:ss} Connected via {authSession.Mode.ToLowerInvariant()} and selected {clientSession.Placement.GatewayName}."),
                LatencyMs = 20,
                JitterMs = 4,
                SignalStrengthPercent = posture.Status.Compliant ? 91 : 66,
                ThroughputMbps = posture.Status.Compliant ? 165 : 0,
                AccessTokenExpiresAtUtc = clientSession.Tokens.AccessTokenExpiresAtUtc,
                RevalidateAfterUtc = clientSession.Bundle.GeneratedAtUtc.AddMinutes(5),
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                LastErrorCode = null,
                Posture = posture.Status
            });

            activity?.SetTag("owlprotect.client.gateway", clientSession.Placement.GatewayName);
            activity?.SetTag("owlprotect.client.connection_state", ConnectionState.Healthy.ToString());
            return _status;
        }
        catch (Exception exception)
        {
            outcome = exception is ControlPlaneApiException apiException ? apiException.ErrorCode : "failure";
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            HandleConnectionFailure(exception);
            return _status;
        }
        finally
        {
            var tags = new TagList
            {
                { "outcome", outcome },
                { "auth_mode", authMode }
            };
            OwlProtectTelemetry.ClientConnectAttempts.Add(1, tags);
            OwlProtectTelemetry.ClientConnectDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
            _gate.Release();
        }
    }

    public async Task<ClientStatus> DisconnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("windowsclient.disconnect");
        try
        {
            UpdateStatus(status => status with
            {
                Connected = false,
                CurrentGateway = "unassigned",
                State = ConnectionState.ServerUnavailable.ToString(),
                DiagnosticScope = DiagnosticScope.Authentication.ToString(),
                UserMessage = "Tunnel disconnected.",
                DiagnosticDetail = "The service kept the enrolled device record but tore down the active tunnel workflow.",
                RecoveryState = "Disconnected",
                Timeline = AppendTimeline(status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Tunnel disconnected by the user."),
                ThroughputMbps = 0,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            return _status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<ClientStatus> SignOutAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("windowsclient.sign_out");
        try
        {
            if (!string.IsNullOrWhiteSpace(_tokens?.AccessToken))
            {
                try
                {
                    await controlPlaneClient.RevokeSessionAsync(_tokens.AccessToken, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Failed to revoke the cached platform session before clearing local state.");
                }
            }

            ClearCachedSession(
                "Signed out and revoked the local platform session.",
                "Signed out.",
                "The next connection attempt must re-authenticate and re-issue policy context.");

            return _status;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(ClientStatus Status, string ExportPath)> ExportSupportBundleAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("windowsclient.support_bundle.export");
        try
        {
            var path = await supportBundleExporter.ExportAsync(_status, cancellationToken);
            UpdateStatus(status => status with
            {
                LastSupportBundlePath = path,
                Timeline = AppendTimeline(status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Exported support bundle to {path}."),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            OwlProtectTelemetry.ClientSupportBundleExports.Add(1);
            return (_status, path);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void UpdateDiagnostics(ClientStatus nextStatus)
    {
        lock (_statusGate)
        {
            _status = nextStatus with { UpdatedAtUtc = DateTimeOffset.UtcNow };
        }
    }

    private void HandleConnectionFailure(Exception exception)
    {
        var errorCode = exception is ControlPlaneApiException apiException
            ? apiException.ErrorCode
            : "control_plane_unreachable";
        var now = DateTimeOffset.UtcNow;

        lock (_statusGate)
        {
            EvaluateRecoveryState();
            if (_status.Connected && _status.RevalidateAfterUtc is { } revalidateAfterUtc && revalidateAfterUtc > now)
            {
                _status = _status with
                {
                    State = ConnectionState.ServerUnavailable.ToString(),
                    DiagnosticScope = DiagnosticScope.ServerSide.ToString(),
                    UserMessage = "Control plane unavailable. Keeping the last authorized tunnel alive inside the offline grace window.",
                    DiagnosticDetail = exception.Message,
                    RecoveryState = "OfflineGrace",
                    Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Control plane unreachable. Reusing cached authorization until {revalidateAfterUtc:HH:mm:ss}."),
                    LastErrorCode = errorCode,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                return;
            }

            _status = _status with
            {
                Connected = false,
                State = errorCode.Contains("policy", StringComparison.OrdinalIgnoreCase)
                    ? ConnectionState.PolicyBlocked.ToString()
                    : ConnectionState.AuthExpired.ToString(),
                DiagnosticScope = errorCode.Contains("policy", StringComparison.OrdinalIgnoreCase)
                    ? DiagnosticScope.Policy.ToString()
                    : DiagnosticScope.Authentication.ToString(),
                UserMessage = "Connection attempt failed.",
                DiagnosticDetail = exception.Message,
                RecoveryState = "ReconnectRequired",
                Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Connect failed with {errorCode}."),
                ThroughputMbps = 0,
                LastErrorCode = errorCode,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private void ClearCachedSession(string timelineMessage, string userMessage, string diagnosticDetail)
    {
        _tokens = null;
        _platformSession = null;
        _user = null;
        _device = null;
        _bundle = null;
        _placement = null;

        lock (_statusGate)
        {
            _status = CreateInitialStatus() with
            {
                UserMessage = userMessage,
                DiagnosticDetail = diagnosticDetail,
                RecoveryState = "SignedOut",
                Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} {timelineMessage}"),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private DeviceEnrollmentRequest BuildEnrollmentRequest(ClientPostureStatus posture)
    {
        return new DeviceEnrollmentRequest(
            Environment.MachineName,
            City: options.Value.DeviceCity,
            Country: options.Value.DeviceCountry,
            PublicIp: ResolveLocalIpv4Address(),
            HardwareKey: CreateHardwareKey(),
            SerialNumber: $"SER-{CreateHardwareKey()[..12].ToUpperInvariant()}",
            OperatingSystem: string.IsNullOrWhiteSpace(posture.OperatingSystem) ? Environment.OSVersion.VersionString : posture.OperatingSystem,
            EnrollmentKind: _device is null ? DeviceEnrollmentKind.Bootstrap : DeviceEnrollmentKind.ReEnrollment,
            Managed: posture.Managed);
    }

    private string BuildConnectedDetail(ControlPlaneClientAuthSessionResponse clientSession) =>
        $"Device '{clientSession.Device.Name}' is {clientSession.Device.RegistrationState}. Policy bundle '{clientSession.Bundle.Version}' routes {clientSession.Bundle.Cidrs.Count} CIDR(s) and {clientSession.Bundle.DnsZones.Count} DNS zone(s) through {clientSession.Placement.GatewayName}.";

    private IReadOnlyList<string> ResolveFailoverGateways(GatewayPlacement placement)
    {
        if (placement.FailoverGatewayIds.Count == 0)
        {
            return [];
        }

        return placement.FailoverGatewayIds.Select(id => id).ToArray();
    }

    private static string ResolveLocalIpv4Address()
    {
        try
        {
            return Dns.GetHostAddresses(Dns.GetHostName())
                .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                ?.ToString()
                ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    private static string CreateHardwareKey()
    {
        var source = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.OSVersion.Version}|{Environment.ProcessorCount}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private void AppendStatusTimeline(string message)
    {
        lock (_statusGate)
        {
            _status = _status with
            {
                Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} {message}"),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private void UpdateStatus(Func<ClientStatus, ClientStatus> update)
    {
        lock (_statusGate)
        {
            _status = update(_status);
        }
    }

    private void EvaluateRecoveryState()
    {
        var now = DateTimeOffset.UtcNow;
        if (!_status.Connected)
        {
            return;
        }

        if (_status.RevalidateAfterUtc is { } revalidateAfterUtc && revalidateAfterUtc <= now)
        {
            _status = _status with
            {
                Connected = false,
                State = ConnectionState.AuthExpired.ToString(),
                DiagnosticScope = DiagnosticScope.Authentication.ToString(),
                UserMessage = "Cached session expired. Reconnect to refresh device authorization.",
                DiagnosticDetail = "The offline recovery window elapsed before the client could revalidate with the control plane.",
                RecoveryState = "ReconnectRequired",
                Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Cached session expired and requires reconnect."),
                ThroughputMbps = 0,
                LastErrorCode = "stale_session",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            return;
        }

        if (_status.AccessTokenExpiresAtUtc is { } accessTokenExpiresAtUtc && accessTokenExpiresAtUtc <= now)
        {
            _status = _status with
            {
                Connected = false,
                State = ConnectionState.AuthExpired.ToString(),
                DiagnosticScope = DiagnosticScope.Authentication.ToString(),
                UserMessage = "Platform session expired. Sign in again to reconnect.",
                DiagnosticDetail = "The cached access token can no longer re-issue client session state.",
                RecoveryState = "ReauthenticateRequired",
                Timeline = AppendTimeline(_status.Timeline, $"{DateTimeOffset.UtcNow:HH:mm:ss} Platform access token expired."),
                ThroughputMbps = 0,
                LastErrorCode = "access_token_expired",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    private static ClientStatus CreateInitialStatus() =>
        new(
            Connected: false,
            DeviceName: Environment.MachineName,
            Username: "Not signed in",
            DeviceId: "unregistered",
            CurrentGateway: "unassigned",
            State: ConnectionState.AuthExpired.ToString(),
            DiagnosticScope: DiagnosticScope.Authentication.ToString(),
            UserMessage: "Sign in with silent SSO or interactive fallback to establish the tunnel.",
            DiagnosticDetail: "The Windows client is waiting for the first enrollment, posture report, and control-plane session exchange.",
            AuthMode: "NotConnected",
            RecoveryState: "Idle",
            RegistrationState: DeviceRegistrationState.Pending.ToString(),
            EnrollmentKind: DeviceEnrollmentKind.Bootstrap.ToString(),
            PolicyBundleVersion: "unassigned",
            Routes: [],
            DnsZones: [],
            Ports: [],
            FailoverGateways: [],
            Timeline:
            [
                $"{DateTimeOffset.UtcNow:HH:mm:ss} Waiting for authentication before selecting a gateway."
            ],
            LatencyMs: 0,
            JitterMs: 0,
            SignalStrengthPercent: 0,
            ThroughputMbps: 0,
            AccessTokenExpiresAtUtc: null,
            RevalidateAfterUtc: null,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            LastErrorCode: null,
            LastSupportBundlePath: null,
            Posture: new ClientPostureStatus(
                Managed: false,
                Compliant: false,
                PostureScore: 0,
                BitLockerEnabled: false,
                DefenderHealthy: false,
                FirewallEnabled: false,
                SecureBootEnabled: false,
                TamperProtectionEnabled: false,
                OperatingSystem: Environment.OSVersion.VersionString,
                ComplianceReasons: ["posture_not_collected"],
                CollectedAtUtc: null));

    private static IReadOnlyList<string> AppendTimeline(IReadOnlyList<string> existing, string entry) =>
        existing
            .Concat([entry])
            .TakeLast(6)
            .ToArray();
}
