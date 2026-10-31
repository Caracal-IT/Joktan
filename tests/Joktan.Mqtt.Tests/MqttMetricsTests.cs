using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

// The meter is shared by the whole process, so these do not run beside anything else.
[Collection(ProcessStateCollection.Name)]
public class MqttMetricsTests : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string Name, long Value, Dictionary<string, object?> Tags)> _longs = [];
    private readonly List<(string Name, int Value)> _ints = [];

    public MqttMetricsTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MqttMetrics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var copied = new Dictionary<string, object?>();
            foreach (var tag in tags)
                copied[tag.Key] = tag.Value;
            _longs.Add((instrument.Name, value, copied));
        });
        _listener.SetMeasurementEventCallback<int>((instrument, value, _, _) => _ints.Add((instrument.Name, value)));
        _listener.Start();
    }

    public void Dispose() => _listener.Dispose();

    [Theory]
    [InlineData(true, "manual")]
    [InlineData(false, "folder")]
    public void Counts_sent_messages_by_where_they_came_from(bool manual, string source)
    {
        MqttMetrics.RecordSent(manual);

        var sent = Assert.Single(_longs, m => m.Name == "joktan.mqtt.messages.sent");
        Assert.Equal(1, sent.Value);
        Assert.Equal(source, sent.Tags["source"]);
    }

    [Fact]
    public void Counts_failed_skipped_and_received_messages()
    {
        MqttMetrics.RecordFailed();
        MqttMetrics.RecordSkipped();
        MqttMetrics.RecordReceived(7);

        Assert.Equal(1, _longs.Single(m => m.Name == "joktan.mqtt.messages.failed").Value);
        Assert.Equal(1, _longs.Single(m => m.Name == "joktan.mqtt.messages.skipped").Value);
        Assert.Equal(7, _longs.Single(m => m.Name == "joktan.mqtt.messages.received").Value);
    }

    [Fact]
    public void Follows_the_number_of_connected_clients_when_it_changes()
    {
        var before = Connected();

        MqttMetrics.ConnectedChanged(false, true);
        MqttMetrics.ConnectedChanged(true, true);
        Assert.Equal(before + 1, Connected());

        MqttMetrics.ConnectedChanged(true, false);
        MqttMetrics.ConnectedChanged(false, false);
        Assert.Equal(before, Connected());
    }

    private int Connected()
    {
        _ints.Clear();
        _listener.RecordObservableInstruments();
        return _ints.Single(m => m.Name == "joktan.mqtt.connected").Value;
    }
}
