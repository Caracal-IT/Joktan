using System;
using Joktan.Dashboard.Telemetry;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

[Collection(ProcessStateCollection.Name)]
public class TelemetryHostTests : IDisposable
{
    private const string Variable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

    public void Dispose() => Environment.SetEnvironmentVariable(Variable, _previous);

    [Fact]
    public void The_endpoint_comes_from_the_environment_or_the_default()
    {
        Environment.SetEnvironmentVariable(Variable, null);
        Assert.Equal(TelemetryHost.DefaultEndpoint, TelemetryHost.Endpoint);

        Environment.SetEnvironmentVariable(Variable, "");
        Assert.Equal(TelemetryHost.DefaultEndpoint, TelemetryHost.Endpoint);

        Environment.SetEnvironmentVariable(Variable, "http://collector:4317");
        Assert.Equal("http://collector:4317", TelemetryHost.Endpoint);
    }

    [Fact]
    public void Starts_and_stops_without_a_collector_to_export_to()
    {
        // A closed port on this machine, so that nothing is sent anywhere.
        Environment.SetEnvironmentVariable(Variable, "http://127.0.0.1:9");
        using var monitor = new MachineMonitor();

        var host = new TelemetryHost(monitor, "Some.Meter");
        using var activity = TelemetryHost.ActivitySource.StartActivity("test");
        host.Dispose();

        Assert.Equal("Joktan", TelemetryHost.ServiceName);
    }
}
