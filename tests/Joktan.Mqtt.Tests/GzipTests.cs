using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Text;
using System;
using Joktan.Mqtt;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class GzipTests
{
    [Theory]
    [InlineData("hello")]
    [InlineData("Grüße")]
    public void What_is_compressed_comes_back(string text)
    {
        var data = Encoding.UTF8.GetBytes(text);

        var zipped = Gzip.Compress(data);

        Assert.True(Gzip.LooksZipped(zipped));
        Assert.Equal(data, Gzip.TryDecompress(zipped));
    }

    [Fact]
    public void Nothing_compresses_to_nothing()
    {
        Assert.Empty(Gzip.Compress([]));
    }

    [Fact]
    public void Leaves_a_payload_that_is_only_the_two_bytes_gzip_starts_with()
    {
        Assert.Null(Gzip.TryDecompress([0x1f, 0x8b]));
    }

    [Fact]
    public void Leaves_gzip_that_was_cut_off()
    {
        var zipped = Gzip.Compress(Encoding.UTF8.GetBytes(new string('x', 1000)));

        Assert.Null(Gzip.TryDecompress(zipped[..(zipped.Length / 2)]));
    }

    [Theory]
    [InlineData(new byte[0], false)]
    [InlineData(new byte[] { 0x1f }, false)]
    [InlineData(new byte[] { 0x8b, 0x1f }, false)]
    [InlineData(new byte[] { 0x1f, 0x8b }, true)]
    [InlineData(new byte[] { 0x1f, 0x8b, 0x08 }, true)]
    public void Recognises_the_start_of_gzip(byte[] data, bool expected)
        => Assert.Equal(expected, Gzip.LooksZipped(data));

    [Fact]
    public void Leaves_what_is_not_gzip()
    {
        Assert.Null(Gzip.TryDecompress(Encoding.UTF8.GetBytes("plain")));
        Assert.Null(Gzip.TryDecompress([]));
    }

    [Fact]
    public void Leaves_what_only_starts_like_gzip()
    {
        Assert.Null(Gzip.TryDecompress([0x1f, 0x8b, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]));
    }

    [Fact]
    public void Unzips_up_to_the_limit_and_no_further()
    {
        var atLimit = Gzip.Compress(new byte[Gzip.MaxUnzippedLength]);
        var overLimit = Gzip.Compress(new byte[Gzip.MaxUnzippedLength + 1]);

        Assert.Equal(Gzip.MaxUnzippedLength, Gzip.TryDecompress(atLimit)!.Length);
        Assert.Null(Gzip.TryDecompress(overLimit));
    }
}
