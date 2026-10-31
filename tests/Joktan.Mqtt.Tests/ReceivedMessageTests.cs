using System;
using System.Linq;
using System.Text;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class ReceivedMessageTests
{
    private static readonly DateTime Time = new(2026, 11, 2, 8, 30, 15, 123);

    private static ReceivedMessage Message(byte[] payload, int qos = 0, bool retain = false)
        => new("a/b", payload, qos, retain, Time);

    private static ReceivedMessage Message(string payload) => Message(Encoding.UTF8.GetBytes(payload));

    [Fact]
    public void Keeps_what_arrived()
    {
        var message = Message([1], qos: 2, retain: true);

        Assert.Equal("a/b", message.Topic);
        Assert.Equal(2, message.Qos);
        Assert.True(message.Retain);
        Assert.Equal(Time, message.Time);
        Assert.Equal("08:30:15.123", message.TimeText);
    }

    [Fact]
    public void A_plain_payload_is_kept_as_it_is()
    {
        var message = Message("hello");

        Assert.False(message.IsGzipped);
        Assert.Equal("", message.GzipTag);
        Assert.Equal("hello", message.Text);
        Assert.Equal(5, message.ReceivedSize);
        Assert.Equal("5 bytes", message.SizeText);
    }

    [Fact]
    public void A_gzipped_payload_is_unzipped()
    {
        var zipped = Gzip.Compress(Encoding.UTF8.GetBytes("hello"));

        var message = Message(zipped);

        Assert.True(message.IsGzipped);
        Assert.Equal("gzip", message.GzipTag);
        Assert.Equal("hello", message.Text);
        Assert.Equal(zipped.Length, message.ReceivedSize);
        Assert.Equal($"{zipped.Length} bytes (5 unzipped)", message.SizeText);
    }

    [Fact]
    public void Binary_is_shown_in_hex()
    {
        var message = Message([0xFF, 0x00, 0xAB]);

        Assert.Equal("(binary, 3 bytes) FF 00 AB", message.Text);
    }

    [Fact]
    public void Long_binary_is_cut_short()
    {
        var payload = new byte[300];
        payload[0] = 0xFF;

        var text = Message(payload).Text;

        Assert.StartsWith("(binary, 300 bytes) FF 00", text);
        Assert.EndsWith(" …", text);
        Assert.Equal(256, text.Split(' ').Count(part => part.Length == 2));
    }

    [Fact]
    public void An_empty_payload_has_empty_text_and_a_preview_that_says_so()
    {
        var message = Message([]);

        Assert.Equal("", message.Text);
        Assert.Equal("(empty)", message.Preview);
        Assert.Equal("", message.Formatted);
    }

    [Fact]
    public void The_preview_is_one_line_and_cut_short()
    {
        Assert.Equal("a b", Message("a\nb\n").Preview);
        Assert.Equal(new string('x', 120) + "…", Message(new string('x', 121)).Preview);
        Assert.Equal(new string('x', 120), Message(new string('x', 120)).Preview);
    }

    [Fact]
    public void JSON_is_indented_for_reading()
    {
        Assert.Contains("\n", Message("""{"a":1}""").Formatted);
        Assert.Contains("\n", Message("[1,2]").Formatted);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("{not json")]
    [InlineData("[1,")]
    public void Anything_else_is_shown_as_it_is(string text) => Assert.Equal(text, Message(text).Formatted);

    [Theory]
    [InlineData("42", 42.0)]
    [InlineData(" 42.5\n", 42.5)]
    [InlineData("-1e3", -1000.0)]
    [InlineData("hello", null)]
    [InlineData("", null)]
    [InlineData("NaN", null)]
    [InlineData("Infinity", null)]
    [InlineData("1e400", null)]
    [InlineData("1,5", null)]
    public void A_number_is_read_from_the_whole_payload(string payload, double? expected)
        => Assert.Equal(expected, Message(payload).Number);

    [Fact]
    public void A_long_payload_is_not_a_number()
    {
        Assert.Null(Message(new string('1', 33)).Number);
        Assert.NotNull(Message(new string('1', 32)).Number);
    }

    [Fact]
    public void The_numeric_fields_are_the_whole_payload_or_those_in_its_JSON()
    {
        var bare = Assert.Single(Message("42").NumericFields);
        Assert.Equal("", bare.Path);
        Assert.Equal([42.0], bare.Values);

        var inside = Assert.Single(Message("""{"t":1.5}""").NumericFields);
        Assert.Equal("t", inside.Path);

        Assert.Empty(Message("hello").NumericFields);
    }
}
