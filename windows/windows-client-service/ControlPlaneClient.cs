using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OWLProtect.Core;

namespace OWLProtect.WindowsClientService;

public sealed class ControlPlaneClient(
    HttpClient httpClient,
    IOptions<WindowsClientOptions> options,
    ILogger<ControlPlaneClient> logger)
{
    private readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<ControlPlaneAuthSessionResponse> LoginWithLocalUserAsync(string username, CancellationToken cancellationToken)
    {
        logger.LogInformation("Authenticating Windows client against the control plane using local user {Username}.", username);
        return await PostAsync<UserLoginRequest, ControlPlaneAuthSessionResponse>(
            "auth.user.login",
            ControlPlaneApiConventions.Path("auth/user/login"),
            new UserLoginRequest(username),
            accessToken: null,
            cancellationToken);
    }

    public async Task<ControlPlaneAuthSessionResponse> LoginWithProviderAsync(string providerId, string token, CancellationToken cancellationToken)
    {
        logger.LogInformation("Authenticating Windows client against the control plane with provider {ProviderId}.", providerId);
        return await PostAsync<ProviderLoginRequest, ControlPlaneAuthSessionResponse>(
            "auth.provider.login",
            ControlPlaneApiConventions.Path("auth/provider/login"),
            new ProviderLoginRequest(providerId, token),
            accessToken: null,
            cancellationToken);
    }

    public async Task<DeviceEnrollmentResult> EnrollDeviceAsync(string accessToken, DeviceEnrollmentRequest request, CancellationToken cancellationToken) =>
        await PostAsync<DeviceEnrollmentRequest, DeviceEnrollmentResult>(
            "client.devices.enroll",
            ControlPlaneApiConventions.Path("auth/client/devices/enroll"),
            request,
            accessToken,
            cancellationToken);

    public async Task<Device> SubmitPostureAsync(string accessToken, string deviceId, PostureReport report, CancellationToken cancellationToken) =>
        await PostAsync<PostureReport, Device>(
            "client.devices.posture",
            ControlPlaneApiConventions.Path($"auth/client/devices/{deviceId}/posture"),
            report,
            accessToken,
            cancellationToken);

    public async Task<ControlPlaneClientAuthSessionResponse> IssueClientSessionAsync(string accessToken, string deviceId, CancellationToken cancellationToken) =>
        await PostAsync<ClientSessionIssueRequest, ControlPlaneClientAuthSessionResponse>(
            "client.session.issue",
            ControlPlaneApiConventions.Path("auth/client/session"),
            new ClientSessionIssueRequest(deviceId),
            accessToken,
            cancellationToken);

    public async Task RevokeSessionAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity("windowsclient.controlplane.session.revoke");
        activity?.SetTag("owlprotect.client.operation", "session.revoke");
        await ExecuteAsync(
            "session.revoke",
            HttpMethod.Post,
            ControlPlaneApiConventions.Path("auth/session/revoke"),
            accessToken,
            cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string operationName, string relativePath, TRequest payload, string? accessToken, CancellationToken cancellationToken)
    {
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity($"windowsclient.controlplane.{operationName}");
        activity?.SetTag("owlprotect.client.operation", operationName);
        return await ExecuteAsync(
            operationName,
            HttpMethod.Post,
            relativePath,
            payload,
            accessToken,
            async response =>
            {
                var result = await response.Content.ReadFromJsonAsync<TResponse>(_serializerOptions, cancellationToken);
                return result ?? throw new InvalidOperationException($"Control-plane response for '{relativePath}' was empty.");
            },
            cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, string? accessToken)
    {
        var baseUrl = options.Value.ControlPlaneBaseUrl.TrimEnd('/');
        var target = new Uri($"{baseUrl}{relativePath}", UriKind.Absolute);
        var request = new HttpRequestMessage(method, target);
        var correlationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n");
        request.Headers.TryAddWithoutValidation(OwlProtectTelemetry.CorrelationIdHeaderName, correlationId);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return request;
    }

    private async Task<TResponse> ExecuteAsync<TRequest, TResponse>(
        string operationName,
        HttpMethod method,
        string relativePath,
        TRequest payload,
        string? accessToken,
        Func<HttpResponseMessage, ValueTask<TResponse>> responseFactory,
        CancellationToken cancellationToken)
    {
        var start = Stopwatch.GetTimestamp();
        var outcome = "success";

        using var request = CreateRequest(method, relativePath, accessToken);
        request.Content = JsonContent.Create(payload, options: _serializerOptions);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                outcome = $"http_{(int)response.StatusCode}";
                throw await BuildExceptionAsync(response, cancellationToken);
            }

            return await responseFactory(response);
        }
        catch
        {
            if (string.Equals(outcome, "success", StringComparison.Ordinal))
            {
                outcome = "exception";
            }

            throw;
        }
        finally
        {
            var durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var tags = new TagList
            {
                { "operation", operationName },
                { "outcome", outcome }
            };
            OwlProtectTelemetry.ClientControlPlaneCalls.Add(1, tags);
            OwlProtectTelemetry.ClientControlPlaneCallDuration.Record(durationMs, tags);
        }
    }

    private async Task ExecuteAsync(
        string operationName,
        HttpMethod method,
        string relativePath,
        string? accessToken,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            operationName,
            method,
            relativePath,
            new Dictionary<string, string>(),
            accessToken,
            static _ => ValueTask.FromResult(true),
            cancellationToken);
    }

    private async Task<ControlPlaneApiException> BuildExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiErrorResponse? error = null;
        try
        {
            error = await response.Content.ReadFromJsonAsync<ApiErrorResponse>(_serializerOptions, cancellationToken);
        }
        catch (NotSupportedException)
        {
        }
        catch (JsonException)
        {
        }

        var raw = error?.Error ?? await response.Content.ReadAsStringAsync(cancellationToken);
        var message = string.IsNullOrWhiteSpace(raw)
            ? $"Control plane call failed with HTTP {(int)response.StatusCode}."
            : raw.Trim();

        return new ControlPlaneApiException(
            response.StatusCode,
            error?.ErrorCode ?? "control_plane_error",
            message);
    }
}

public sealed class ControlPlaneApiException(HttpStatusCode statusCode, string errorCode, string message)
    : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}
