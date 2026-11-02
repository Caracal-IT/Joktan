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
    public void The_endpoint_in_the_settings_wins_over_the_variable()
    {
        Environment.SetEnvironmentVariable(Variable, "http://from-the-ide:49466");
        var data = new Joktan.Tests.TestSupport.TempFolder();
        var previous = Environment.GetEnvironmentVariable(Joktan.Core.DataFolder.Variable);
        Environment.SetEnvironmentVariable(Joktan.Core.DataFolder.Variable, data.Path);
        try
        {
            new Joktan.Core.LoggingSettings { Endpoint = "http://collector:4317" }.Save();

            Assert.Equal("http://collector:4317", TelemetryHost.Endpoint);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Joktan.Core.DataFolder.Variable, previous);
            data.Dispose();
        }
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
