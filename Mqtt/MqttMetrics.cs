using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace Joktan.Mqtt;

/// <summary>
/// OpenTelemetry instruments for the MQTT publisher. Topics and bodies are never recorded.
/// </summary>
public static class MqttMetrics
{
    public const string MeterName = "Joktan.Mqtt";

    private static readonly Meter Meter = new(MeterName);
    private static volatile bool _connected;

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
        Meter.CreateObservableGauge("joktan.mqtt.connected", () => _connected ? 1 : 0, "1",
            "1 while connected to the broker, otherwise 0");
    }

    public static void SetConnected(bool connected) => _connected = connected;

    public static void RecordSent(bool manual)
        => Sent.Add(1, new KeyValuePair<string, object?>("source", manual ? "manual" : "folder"));

    public static void RecordFailed() => Failed.Add(1);

    public static void RecordReceived(long count) => Received.Add(count);

    public static void RecordSkipped() => Skipped.Add(1);
}
