using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
            ControlPlaneApiConventions.Path("auth/user/login"),
            new UserLoginRequest(username),
            accessToken: null,
            cancellationToken);
    }

    public async Task<ControlPlaneAuthSessionResponse> LoginWithProviderAsync(string providerId, string token, CancellationToken cancellationToken)
    {
        logger.LogInformation("Authenticating Windows client against the control plane with provider {ProviderId}.", providerId);
        return await PostAsync<ProviderLoginRequest, ControlPlaneAuthSessionResponse>(
            ControlPlaneApiConventions.Path("auth/provider/login"),
            new ProviderLoginRequest(providerId, token),
            accessToken: null,
            cancellationToken);
    }

    public async Task<DeviceEnrollmentResult> EnrollDeviceAsync(string accessToken, DeviceEnrollmentRequest request, CancellationToken cancellationToken) =>
        await PostAsync<DeviceEnrollmentRequest, DeviceEnrollmentResult>(
            ControlPlaneApiConventions.Path("auth/client/devices/enroll"),
            request,
            accessToken,
            cancellationToken);

    public async Task<Device> SubmitPostureAsync(string accessToken, string deviceId, PostureReport report, CancellationToken cancellationToken) =>
        await PostAsync<PostureReport, Device>(
            ControlPlaneApiConventions.Path($"auth/client/devices/{deviceId}/posture"),
            report,
            accessToken,
            cancellationToken);

    public async Task<ControlPlaneClientAuthSessionResponse> IssueClientSessionAsync(string accessToken, string deviceId, CancellationToken cancellationToken) =>
        await PostAsync<ClientSessionIssueRequest, ControlPlaneClientAuthSessionResponse>(
            ControlPlaneApiConventions.Path("auth/client/session"),
            new ClientSessionIssueRequest(deviceId),
            accessToken,
            cancellationToken);

    public async Task RevokeSessionAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, ControlPlaneApiConventions.Path("auth/session/revoke"), accessToken);
        request.Content = JsonContent.Create(new { }, options: _serializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw await BuildExceptionAsync(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string relativePath, TRequest payload, string? accessToken, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, relativePath, accessToken);
        request.Content = JsonContent.Create(payload, options: _serializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw await BuildExceptionAsync(response, cancellationToken);
        }

        var result = await response.Content.ReadFromJsonAsync<TResponse>(_serializerOptions, cancellationToken);
        return result ?? throw new InvalidOperationException($"Control-plane response for '{relativePath}' was empty.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath, string? accessToken)
    {
        var baseUrl = options.Value.ControlPlaneBaseUrl.TrimEnd('/');
        var target = new Uri($"{baseUrl}{relativePath}", UriKind.Absolute);
        var request = new HttpRequestMessage(method, target);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return request;
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
