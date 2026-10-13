using System;

namespace Joktan.Mqtt;

/// <summary>
/// One line of the MQTT activity log. <see cref="Body"/> is set for sent messages.
/// </summary>
public sealed record ActivityEntry(
    DateTime Time, string Event, string Topic = "", string Source = "", string Detail = "", string? Body = null)
{
    public string TimeText => Time.ToString("HH:mm:ss");
}
