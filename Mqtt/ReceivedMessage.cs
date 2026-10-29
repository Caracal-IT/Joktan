using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Joktan.Mqtt;

/// <summary>
/// A message that arrived on a subscription.
/// </summary>
public sealed class ReceivedMessage
{
    private const int PreviewLength = 120;
    private const int HexBytesShown = 256;

    private static readonly UTF8Encoding StrictUtf8 = new(false, throwOnInvalidBytes: true);

    private string? _text;
    private string? _formatted;

    /// <param name="payload">The payload as it arrived; a gzipped one is unzipped here.</param>
    public ReceivedMessage(string topic, byte[] payload, int qos, bool retain, DateTime time)
    {
        Topic = topic;
        ReceivedSize = payload.Length;
        if (Gzip.TryDecompress(payload) is { } unzipped)
        {
            Payload = unzipped;
            IsGzipped = true;
        }
        else
        {
            Payload = payload;
        }

        Qos = qos;
        Retain = retain;
        Time = time;
    }

    public string Topic { get; }

    /// <summary>The payload, unzipped when it arrived gzipped.</summary>
    public byte[] Payload { get; }

    /// <summary>True when the payload arrived gzipped and <see cref="Payload"/> is what it unzipped to.</summary>
    public bool IsGzipped { get; }

    /// <summary>The size of the payload as it arrived, before any unzipping.</summary>
    public int ReceivedSize { get; }

    /// <summary>"gzip" for a message that arrived gzipped, otherwise empty.</summary>
    public string GzipTag => IsGzipped ? "gzip" : "";

    public int Qos { get; }

    /// <summary>True when the broker sent this as the stored last value of the topic.</summary>
    public bool Retain { get; }

    public DateTime Time { get; }

    public string TimeText => Time.ToString("HH:mm:ss.fff");

    public string SizeText => IsGzipped
        ? $"{ReceivedSize:N0} bytes ({Payload.Length:N0} unzipped)"
        : $"{Payload.Length:N0} bytes";

    /// <summary>The payload as text, or a hex dump when it is not valid UTF-8.</summary>
    public string Text => _text ??= Decode();

    /// <summary>The payload laid out for reading: JSON is indented, anything else is shown as is.</summary>
    public string Formatted => _formatted ??= Format();

    /// <summary>The start of the payload on one line.</summary>
    public string Preview
    {
        get
        {
            if (Payload.Length == 0)
                return "(empty)";

            var line = Text.ReplaceLineEndings(" ").Trim();
            return line.Length > PreviewLength ? line[..PreviewLength] + "…" : line;
        }
    }

    /// <summary>The payload as a number, if it is one: a bare number or a JSON number.</summary>
    public double? Number
        => Payload.Length is > 0 and <= 32
           && double.TryParse(Text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
           && double.IsFinite(value)
            ? value
            : null;

    private string Decode()
    {
        if (Payload.Length == 0)
            return "";

        try
        {
            return StrictUtf8.GetString(Payload);
        }
        catch (DecoderFallbackException)
        {
            var shown = Payload.AsSpan(0, Math.Min(Payload.Length, HexBytesShown));
            var hex = Convert.ToHexString(shown);
            var spaced = new StringBuilder(hex.Length * 3 / 2);
            for (var i = 0; i < hex.Length; i += 2)
                spaced.Append(hex, i, 2).Append(' ');
            return $"(binary, {Payload.Length:N0} bytes) {spaced.ToString().TrimEnd()}"
                   + (Payload.Length > HexBytesShown ? " …" : "");
        }
    }

    private string Format()
    {
        var text = Text;
        if (text.Length == 0 || (text[0] != '{' && text[0] != '['))
            return text;

        try
        {
            return JsonNode.Parse(text)?.ToJsonString(JsonFormats.Indented) ?? text;
        }
        catch (JsonException)
        {
            return text;
        }
    }
}
