using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Threading;

namespace Joktan.Mqtt;

/// <summary>
/// OpenTelemetry instruments for the MQTT publisher. Topics and bodies are never recorded.
/// </summary>
public static class MqttMetrics
{
    public const string MeterName = "Joktan.Mqtt";

    private static readonly Meter Meter = new(MeterName);
    private static int _connected;

    private static readonly Counter<long> Sent = Meter.CreateCounter<long>(
        "joktan.mqtt.messages.sent", "{message}", "Messages published");

    private static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        "joktan.mqtt.messages.failed", "{message}", "Messages the broker did not accept");

    private static readonly Counter<long> Skipped = Meter.CreateCounter<long>(
        "joktan.mqtt.messages.skipped", "{message}", "Scheduled sends skipped because there was no connection");

    private static readonly Counter<long> Received = Meter.CreateCounter<long>(
        "joktan.mqtt.messages.received", "{message}", "Messages received by the topic explorer");

    static MqttMetrics()
    {
        Meter.CreateObservableGauge("joktan.mqtt.connected", () => Volatile.Read(ref _connected), "{client}",
            "The number of MQTT clients connected to their broker");
    }

    public static void ConnectedChanged(bool wasConnected, bool isConnected)
    {
        if (isConnected != wasConnected)
            Interlocked.Add(ref _connected, isConnected ? 1 : -1);
    }

    public static void RecordSent(bool manual)
        => Sent.Add(1, new KeyValuePair<string, object?>("source", manual ? "manual" : "folder"));

    public static void RecordFailed() => Failed.Add(1);

    public static void RecordReceived(long count) => Received.Add(count);

    public static void RecordSkipped() => Skipped.Add(1);
}
