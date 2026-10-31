using System;
using System.Globalization;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class ActivityEntryTests
{
    [Fact]
    public void Describes_the_size_of_a_message()
    {
        Assert.Equal(
            $"{1234.ToString("N0", CultureInfo.CurrentCulture)} bytes",
            ActivityEntry.SizeDetail(9, 1234, gzipped: false));
        Assert.Equal("5 bytes gzipped (9 unzipped)", ActivityEntry.SizeDetail(9, 5, gzipped: true));
    }

    [Fact]
    public void Shows_the_time_to_the_second()
    {
        var entry = new ActivityEntry(new DateTime(2026, 11, 2, 8, 30, 15), "Sent");

        Assert.Equal("08:30:15", entry.TimeText);
    }
}
