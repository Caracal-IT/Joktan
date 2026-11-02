using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Joktan.Core;
using Joktan.Dashboard.Telemetry;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

[Collection(ProcessStateCollection.Name)]
public class TelemetryHostTests : DataFolderTest
{
    private const string Variable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // A closed port on this machine, so that nothing is sent anywhere.
    private const string Nowhere = "http://127.0.0.1:9";

    private readonly string? _previous = Environment.GetEnvironmentVariable(Variable);

    protected override void Dispose(bool disposing)
    {
        Environment.SetEnvironmentVariable(Variable, _previous);
        AppLog.MetricsProblem = null;
        base.Dispose(disposing);
    }

    private static TelemetryHost Host(MachineMonitor monitor, TelemetrySettings settings, params string[] meters)
    {
        settings.CollectorEndpoint = Nowhere;
        settings.Save();
        return new TelemetryHost(monitor, new FakeCredentialStore(), meters);
    }

    [Fact]
    public void The_endpoint_is_the_one_in_the_settings_and_otherwise_the_variable_or_the_default()
    {
        Environment.SetEnvironmentVariable(Variable, "http://from-the-ide:49466");
        Assert.Equal(TelemetryHost.DefaultEndpoint, TelemetryHost.Endpoint);

        new TelemetrySettings { CollectorEndpoint = "http://collector:4317" }.Save();
        Assert.Equal("http://collector:4317", TelemetryHost.Endpoint);

        new TelemetrySettings { CollectorEndpoint = "" }.Save();
        Assert.Equal("http://from-the-ide:49466", TelemetryHost.Endpoint);

        Environment.SetEnvironmentVariable(Variable, "");
        Assert.Equal(TelemetryHost.DefaultEndpoint, TelemetryHost.Endpoint);
    }

    [Fact]
    public async Task With_the_endpoint_on_the_machines_metrics_can_be_scraped()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var monitor = new MachineMonitor();
        using var host = Host(monitor, new TelemetrySettings { MetricsEndpointEnabled = true, MetricsPort = port });
        using var http = new HttpClient();

        var text = await http.GetStringAsync($"http://127.0.0.1:{port}/metrics", TestContext.Current.CancellationToken);

        Assert.Null(AppLog.MetricsProblem);
        Assert.Contains("system_uptime", text);
        Assert.Contains("target_info", text);
    }

    [Fact]
    public void A_port_in_use_stops_only_the_endpoint()
    {
        var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        using var monitor = new MachineMonitor();

        using (Host(monitor, new TelemetrySettings { MetricsEndpointEnabled = true, MetricsPort = ((IPEndPoint)busy.LocalEndpoint).Port }))
            Assert.NotNull(AppLog.MetricsProblem);

        busy.Stop();
    }

    [Fact]
    public void Starts_and_stops_without_anything_to_export_to()
    {
        using var monitor = new MachineMonitor();

        var host = Host(monitor, new TelemetrySettings { LogsToFile = true, LogsToElastic = true }, "Some.Meter");
        using var activity = TelemetryHost.ActivitySource.StartActivity("test");
        host.Dispose();

        Assert.Equal("Joktan", TelemetryHost.ServiceName);
    }
}
