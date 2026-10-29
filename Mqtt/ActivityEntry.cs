using System;

namespace Joktan.Mqtt;

/// <summary>
/// One line of the MQTT activity log. <see cref="Body"/> is set for sent messages.
/// </summary>
public sealed record ActivityEntry(
    DateTime Time, string Event, string Topic = "", string Source = "", string Detail = "", string? Body = null)
{
    public string TimeText => Time.ToString("HH:mm:ss");

    /// <summary>The detail of a sent message: its size, and for a gzipped one the size before zipping too.</summary>
    public static string SizeDetail(int bodyLength, int payloadLength, bool gzipped)
        => gzipped ? $"{payloadLength:N0} bytes gzipped ({bodyLength:N0} unzipped)" : $"{payloadLength:N0} bytes";
}
