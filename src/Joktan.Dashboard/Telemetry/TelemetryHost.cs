using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Joktan.Core;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Joktan.Dashboard.Telemetry;

/// <summary>
/// Owns the OpenTelemetry SDK for the application. Metrics, traces and logs are exported over OTLP;
/// the endpoint and protocol come from the standard OTEL_EXPORTER_OTLP_* environment variables.
/// </summary>
public sealed class TelemetryHost : IDisposable
{
    public const string ServiceName = "Joktan";
    public const string DefaultEndpoint = "http://localhost:4317";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    // Often enough for a dashboard of the machine to follow it, as the SDK's default is a minute.
    private const int MetricExportMilliseconds = 5000;

    private readonly MachineMetrics _machineMetrics;
    private readonly MeterProvider _meterProvider;
    private readonly TracerProvider _tracerProvider;
    private readonly ILoggerFactory _logs;

    /// <param name="meters">The meters of the modules, exported along with the machine's own.</param>
    public TelemetryHost(MachineMonitor monitor, params string[] meters)
    {
        _machineMetrics = new MachineMetrics(monitor);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        var resource = ResourceBuilder.CreateDefault()
            // The machine rather than a new id each run, so that a restart does not look like a second machine.
            .AddService(ServiceName, serviceVersion: version, autoGenerateServiceInstanceId: false, serviceInstanceId: Environment.MachineName)
            .AddAttributes(
        [
            new("host.name", Environment.MachineName),
            new("host.arch", RuntimeInformation.OSArchitecture.ToString()),
            new("os.description", RuntimeInformation.OSDescription),
            new("process.runtime.version", Environment.Version.ToString()),
        ]);

        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(MachineMetrics.MeterName)
            .AddMeter(meters)
            .AddMeter("System.Runtime")
            .AddOtlpExporter((_, reader) =>
            {
                if (Environment.GetEnvironmentVariable("OTEL_METRIC_EXPORT_INTERVAL") is not { Length: > 0 })
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = MetricExportMilliseconds;
            })
            .Build();

        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(ServiceName)
            .AddOtlpExporter()
            .Build();

        var settings = LoggingSettings.Load();
        AppLog.Configure(settings);
        _logs = LoggerFactory.Create(logging =>
        {
            // Asked for each log, as the settings page changes these while the app runs.
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddFilter((_, _, level) => AppLog.IsOn(level));
            logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resource);
                options.IncludeFormattedMessage = true;
                options.AddProcessor(new LogLabels());
                options.AddOtlpExporter(exporter =>
                {
                    if (settings.Endpoint.Length > 0 && LoggingSettings.IsValidEndpoint(settings.Endpoint))
                        exporter.Endpoint = new Uri(settings.Endpoint);
                });
            });
        });
        AppLog.Use(_logs);
    }

    /// <summary>The OTLP endpoint in effect, for display.</summary>
    public static string Endpoint
        => Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") is { Length: > 0 } endpoint
            ? endpoint
            : DefaultEndpoint;

    public void Dispose()
    {
        // Disposing the providers flushes anything not yet exported.
        AppLog.Use(null);
        _logs.Dispose();
        _tracerProvider.Dispose();
        _meterProvider.Dispose();
        _machineMetrics.Dispose();
    }
}
