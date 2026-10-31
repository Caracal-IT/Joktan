using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Joktan.Dashboard.Telemetry;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

public class MachineMetricsTests
{
    [Fact]
    public void Publishes_the_readings_under_their_OpenTelemetry_names()
    {
        using var monitor = new MachineMonitor();
        using var metrics = new MachineMetrics(monitor);
        var names = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == MachineMetrics.MeterName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => names.Add(instrument.Name));
        listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => names.Add(instrument.Name));
        listener.SetMeasurementEventCallback<int>((instrument, _, _, _) => names.Add(instrument.Name));
        listener.Start();

        listener.RecordObservableInstruments();

        Assert.Contains("system.cpu.logical.count", names);
        Assert.Contains("system.uptime", names);
        Assert.Contains("process.memory.usage", names);
    }
}
