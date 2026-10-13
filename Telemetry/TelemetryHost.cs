using System;
using System.Diagnostics;
using System.Reflection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Joktan.Telemetry;

/// <summary>
/// Owns the OpenTelemetry SDK for the application. Metrics and traces are exported over OTLP;
/// the endpoint and protocol come from the standard OTEL_EXPORTER_OTLP_* environment variables.
/// </summary>
public sealed class TelemetryHost : IDisposable
{
    public const string ServiceName = "Joktan";
    public const string DefaultEndpoint = "http://localhost:4317";

    public static readonly ActivitySource ActivitySource = new(ServiceName);

    private readonly MachineMetrics _machineMetrics;
    private readonly MeterProvider _meterProvider;
    private readonly TracerProvider _tracerProvider;

    public TelemetryHost(MachineMonitor monitor)
    {
        _machineMetrics = new MachineMetrics(monitor);

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        var resource = ResourceBuilder.CreateDefault().AddService(ServiceName, serviceVersion: version);

        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(resource)
            .AddMeter(MachineMetrics.MeterName)
            .AddMeter(Joktan.Mqtt.MqttMetrics.MeterName)
            .AddMeter("System.Runtime")
            .AddOtlpExporter()
            .Build();

        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(resource)
            .AddSource(ServiceName)
            .AddOtlpExporter()
            .Build();
    }

    /// <summary>The OTLP endpoint in effect, for display.</summary>
    public static string Endpoint
        => Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") is { Length: > 0 } endpoint
            ? endpoint
            : DefaultEndpoint;

    public void Dispose()
    {
        // Disposing the providers flushes anything not yet exported.
        _tracerProvider.Dispose();
        _meterProvider.Dispose();
        _machineMetrics.Dispose();
    }
}
