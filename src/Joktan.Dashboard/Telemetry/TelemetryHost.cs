using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Core;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Joktan.Dashboard.Telemetry;

/// <summary>
/// Owns the OpenTelemetry SDK for the application. Metrics and traces are exported over OTLP;
/// the endpoint comes from Settings / Telemetry, and otherwise, like the protocol, from the standard
/// OTEL_EXPORTER_OTLP_* environment variables. Metrics can also be scraped, and logs can also go to a file or
/// straight to Elasticsearch.
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
    private readonly ElasticLogSink _logSink;
    private readonly FileLogSink _fileSink;
    private readonly MetricsEndpoint? _metricsEndpoint;
    private readonly HttpClient _scrapeClient = new();

    /// <param name="credentials">Where the API key or password for Elasticsearch is, if not the system's.</param>
    /// <param name="meterNames">The meters of the modules, exported along with the machine's own.</param>
    public TelemetryHost(MachineMonitor monitor, CredentialStore? credentials = null, params string[] meterNames)
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

        var settings = TelemetrySettings.Load();
        settings.ReadSecret(credentials ?? CredentialStore.Create(CredentialStore.ElasticService));
        AppLog.Configure(settings);

        var metricsPort = settings.MetricsEndpointEnabled ? FreeLoopbackPort() : (int?)null;
        var meters = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(MachineMetrics.MeterName)
            .AddMeter(meterNames)
            .AddMeter("System.Runtime")
            .AddOtlpExporter((exporter, reader) =>
            {
                UseChosenEndpoint(exporter);
                if (Environment.GetEnvironmentVariable("OTEL_METRIC_EXPORT_INTERVAL") is not { Length: > 0 })
                    reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = MetricExportMilliseconds;
            });
        if (metricsPort is { } exporterPort)
            meters.AddPrometheusHttpListener(options =>
            {
                options.Host = "localhost";
                options.Port = exporterPort;
            });
        _meterProvider = meters.Build();

        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(ServiceName)
            .AddOtlpExporter(UseChosenEndpoint)
            .Build();

        _logSink = new ElasticLogSink(AppLog.Target);
        _fileSink = new FileLogSink(() => (AppLog.Settings.EffectiveLogFolder, AppLog.Settings.LogFileDays));
        _logs = LoggerFactory.Create(logging =>
        {
            // Each one asks the settings for each log, as the settings page changes them while the app runs.
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new ElasticLoggerProvider(_logSink, version));
            logging.AddProvider(new FileLoggerProvider(_fileSink));
            logging.AddFilter<OpenTelemetryLoggerProvider>((_, level) => AppLog.Settings.LogsToCollector && level >= AppLog.Settings.MinimumLevel);
            logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(resource);
                options.IncludeFormattedMessage = true;
                options.AddProcessor(new LogLabels());
                options.AddOtlpExporter(UseChosenEndpoint);
            });
        });
        AppLog.Use(_logs);

        if (metricsPort is { } port)
            _metricsEndpoint = StartEndpoint(settings, port);
    }

    private MetricsEndpoint? StartEndpoint(TelemetrySettings settings, int exporterPort)
    {
        AppLog.MetricsProblem = null;
        try
        {
            return new MetricsEndpoint(settings.MetricsFromOtherComputers, settings.MetricsPort, cancel => ScrapeAsync(exporterPort, cancel));
        }
        catch (SocketException e)
        {
            AppLog.MetricsProblem = e.Message;
            AppLog.For("App").LogWarning("The metrics endpoint could not start on port {Port}: {Reason}", settings.MetricsPort, e.Message);
            return null;
        }
    }

    private async Task<(string ContentType, byte[] Body)> ScrapeAsync(int exporterPort, CancellationToken cancel)
    {
        using var response = await _scrapeClient.GetAsync($"http://localhost:{exporterPort}/metrics", cancel);
        return (response.Content.Headers.ContentType?.ToString() ?? "text/plain", await response.Content.ReadAsByteArrayAsync(cancel));
    }

    private static int FreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    /// <summary>The OTLP endpoint in effect, for display.</summary>
    public static string Endpoint
        => ChosenEndpoint
           ?? (Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") is { Length: > 0 } endpoint ? endpoint : DefaultEndpoint);

    private static string? ChosenEndpoint
        => TelemetrySettings.Load().CollectorEndpoint is { Length: > 0 } chosen && TelemetrySettings.IsValidUrl(chosen) ? chosen : null;

    private static void UseChosenEndpoint(OtlpExporterOptions exporter)
    {
        if (ChosenEndpoint is { } chosen)
            exporter.Endpoint = new Uri(chosen);
    }

    public void Dispose()
    {
        // Disposing the providers flushes anything not yet exported.
        AppLog.Use(null);
        _logs.Dispose();
        _logSink.Dispose();
        _fileSink.Dispose();
        _metricsEndpoint?.Dispose();
        _scrapeClient.Dispose();
        _tracerProvider.Dispose();
        _meterProvider.Dispose();
        _machineMetrics.Dispose();
    }
}
