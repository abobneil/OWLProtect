using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace OWLProtect.Core;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddOwlProtectObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName,
        bool includeAspNetCoreInstrumentation)
    {
        services.Configure<ObservabilityOptions>(configuration.GetSection("Observability"));

        var options = configuration.GetSection("Observability").Get<ObservabilityOptions>() ?? new ObservabilityOptions();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        var openTelemetry = services.AddOpenTelemetry();

        openTelemetry.ConfigureResource(resource =>
        {
            resource
                .AddService(
                    serviceName: serviceName,
                    serviceNamespace: string.IsNullOrWhiteSpace(options.ServiceNamespace) ? "owlprotect" : options.ServiceNamespace,
                    serviceVersion: version)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment.name", environment.EnvironmentName)
                ]);
        });

        openTelemetry.WithMetrics(metrics =>
        {
            metrics
                .AddMeter(OwlProtectTelemetry.Meter.Name)
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation();

            if (includeAspNetCoreInstrumentation)
            {
                metrics.AddAspNetCoreInstrumentation();
            }

            if (includeAspNetCoreInstrumentation && options.EnablePrometheusEndpoint)
            {
                metrics.AddPrometheusExporter();
            }

            if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
            {
                metrics.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(options.OtlpEndpoint);
                    otlp.Protocol = ParseProtocol(options.OtlpProtocol);
                });
            }
        });

        openTelemetry.WithTracing(tracing =>
        {
            tracing
                .AddSource(OwlProtectTelemetry.ActivitySource.Name)
                .AddHttpClientInstrumentation(options => options.RecordException = true);

            if (includeAspNetCoreInstrumentation)
            {
                tracing.AddAspNetCoreInstrumentation(options => options.RecordException = true);
            }

            if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint))
            {
                tracing.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(options.OtlpEndpoint);
                    otlp.Protocol = ParseProtocol(options.OtlpProtocol);
                });
            }
        });

        return services;
    }

    public static WebApplication UseOwlProtectRequestCorrelation(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("OWLProtect.Requests");

        app.Use(async (context, next) =>
        {
            var incoming = context.Request.Headers[OwlProtectTelemetry.CorrelationIdHeaderName].ToString();
            var correlationId = string.IsNullOrWhiteSpace(incoming)
                ? Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("n")
                : incoming.Trim();

            context.TraceIdentifier = correlationId;
            context.Response.Headers[OwlProtectTelemetry.CorrelationIdHeaderName] = correlationId;
            Activity.Current?.SetTag("owlprotect.correlation_id", correlationId);

            var start = Stopwatch.GetTimestamp();
            using (logger.BeginScope(new Dictionary<string, object?> { ["correlationId"] = correlationId }))
            {
                await next();

                if (options.EnableRequestLogging)
                {
                    var durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var sessionCorrelationId = context.Items.TryGetValue(OwlProtectTelemetry.SessionCorrelationItemKey, out var sessionValue)
                        ? sessionValue as string
                        : null;
                    var remoteIp = options.RedactIpAddresses
                        ? SensitiveDataRedactor.Redact("ip", context.Connection.RemoteIpAddress?.ToString())
                        : context.Connection.RemoteIpAddress?.ToString() ?? "n/a";

                    logger.LogInformation(
                        "HTTP {Method} {Path} completed with {StatusCode} in {DurationMs}ms. CorrelationId={CorrelationId} SessionCorrelationId={SessionCorrelationId} RemoteIp={RemoteIp}",
                        context.Request.Method,
                        context.Request.Path.Value,
                        context.Response.StatusCode,
                        Math.Round(durationMs, 2),
                        correlationId,
                        sessionCorrelationId ?? "n/a",
                        remoteIp);
                }
            }
        });

        return app;
    }

    public static WebApplication MapOwlProtectOperationalEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<ObservabilityOptions>>().Value;

        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Count == 0 || registration.Tags.Contains("live"),
            ResponseWriter = WriteHealthResponseAsync
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
            ResponseWriter = WriteHealthResponseAsync
        });
        app.MapGet("/health", () => Results.Redirect("/health/ready", permanent: false));

        if (options.EnablePrometheusEndpoint)
        {
            app.MapPrometheusScrapingEndpoint("/metrics");
        }

        return app;
    }

    private static OtlpExportProtocol ParseProtocol(string? protocol) =>
        string.Equals(protocol, "http/protobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

    private static async Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;

        var payload = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration,
            entries = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration,
                data = entry.Value.Data.ToDictionary(item => item.Key, item => item.Value)
            })
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
