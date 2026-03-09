using System.IO.Pipes;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using OWLProtect.Core;

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
            using var pipe = NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                CreatePipeSecurity());
            await pipe.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var payload = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                continue;
            }

            PipeResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<PipeRequest>(payload);
                response = request is null
                    ? BuildResponse(new PipeRequest(PipeProtocol.Version, Guid.NewGuid().ToString("n"), PipeProtocol.StatusCommand), success: false, errorCode: "invalid_payload", errorMessage: "The IPC payload could not be deserialized.")
                    : await HandleRequestAsync(request, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to process an IPC request.");
                response = BuildResponse(
                    new PipeRequest(PipeProtocol.Version, Guid.NewGuid().ToString("n"), PipeProtocol.StatusCommand),
                    success: false,
                    errorCode: "pipe_error",
                    errorMessage: exception.Message);
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }
    }

    private async Task<PipeResponse> HandleRequestAsync(PipeRequest request, CancellationToken cancellationToken)
    {
        var normalizedCommand = request.Command.Trim().ToLowerInvariant();
        var start = Stopwatch.GetTimestamp();
        var outcome = "success";
        using var activity = OwlProtectTelemetry.ActivitySource.StartActivity($"windowsclient.ipc.{normalizedCommand.Replace('-', '_')}");
        activity?.SetTag("owlprotect.client.ipc.command", normalizedCommand);

        if (request.ProtocolVersion != PipeProtocol.Version)
        {
            outcome = "unsupported_protocol";
            return BuildResponse(
                request,
                success: false,
                errorCode: "unsupported_protocol",
                errorMessage: $"IPC version {request.ProtocolVersion} is not supported.");
        }

        try
        {
            return normalizedCommand switch
            {
                PipeProtocol.StatusCommand => BuildResponse(request, success: true),
                PipeProtocol.ConnectCommand => BuildResponse(request, await state.ConnectAsync(request.SilentSsoPreferred, cancellationToken), success: true),
                PipeProtocol.DisconnectCommand => BuildResponse(request, await state.DisconnectAsync(cancellationToken), success: true),
                PipeProtocol.SignOutCommand => BuildResponse(request, await state.SignOutAsync(cancellationToken), success: true),
                PipeProtocol.SupportBundleCommand => BuildSupportBundleResponse(request, await state.ExportSupportBundleAsync(cancellationToken)),
                _ => BuildUnknownCommand(request, ref outcome)
            };
        }
        catch (Exception exception)
        {
            outcome = "failure";
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            throw;
        }
        finally
        {
            OwlProtectTelemetry.ClientIpcRequests.Add(1, new TagList
            {
                { "command", normalizedCommand },
                { "outcome", outcome }
            });
            OwlProtectTelemetry.ClientIpcRequestDuration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, new TagList
            {
                { "command", normalizedCommand },
                { "outcome", outcome }
            });
        }
    }

    private PipeResponse BuildResponse(PipeRequest request, bool success, string? errorCode = null, string? errorMessage = null) =>
        BuildResponse(request, state.GetStatus(), success, errorCode, errorMessage);

    private static PipeResponse BuildResponse(PipeRequest request, ClientStatus status, bool success, string? errorCode = null, string? errorMessage = null) =>
        new(
            PipeProtocol.Version,
            string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("n") : request.RequestId,
            success,
            status,
            errorCode,
            errorMessage,
            ExportPath: null);

    private static PipeResponse BuildSupportBundleResponse(PipeRequest request, (ClientStatus Status, string ExportPath) exportResult) =>
        new(
            PipeProtocol.Version,
            string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("n") : request.RequestId,
            Success: true,
            exportResult.Status,
            ErrorCode: null,
            ErrorMessage: null,
            ExportPath: exportResult.ExportPath);

    private PipeResponse BuildUnknownCommand(PipeRequest request, ref string outcome)
    {
        outcome = "unknown_command";
        return BuildResponse(
            request,
            success: false,
            errorCode: "unknown_command",
            errorMessage: $"'{request.Command}' is not a supported client command.");
    }

    private static PipeSecurity CreatePipeSecurity()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Allow));
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
            AccessControlType.Allow));
        return pipeSecurity;
    }
}
