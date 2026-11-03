using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Logs.Tests;

public class ErrorGroupTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static LogEntry Error(int minute, string message, string? type = null, string? stack = null, string app = "joktan", LogLevel level = LogLevel.Error)
        => new(Noon.AddMinutes(minute), level, app, "mqtt", message, type, stack);

    private const string Stack = """
        System.InvalidOperationException: gone
           at Joktan.Mqtt.Client.ConnectAsync() in /build/src/Joktan.Mqtt/Client.cs:line 42
           at Joktan.Mqtt.Page.StartAsync() in /build/src/Joktan.Mqtt/Page.cs:line 7
        """;

    [Fact]
    public void Groups_the_same_exception_and_first_frame_even_when_the_line_moved()
    {
        var moved = Stack.Replace("line 42", "line 45");

        var groups = ErrorGroup.Of(
        [
            Error(1, "Could not connect to a", "System.InvalidOperationException", Stack),
            Error(5, "Could not connect to b", "System.InvalidOperationException", moved, level: LogLevel.Critical),
            Error(3, "Could not connect to c", "System.TimeoutException", Stack),
        ]);

        Assert.Equal(2, groups.Count);
        var first = groups[0];
        Assert.Equal(2, first.Count);
        Assert.Equal("Could not connect to b", first.Latest.Message);
        Assert.Equal(Noon.AddMinutes(1), first.First);
        Assert.Equal(Noon.AddMinutes(5), first.Last);
        Assert.Equal(LogLevel.Critical, first.Level);
    }

    [Fact]
    public void Groups_logs_without_an_exception_by_message_with_numbers_and_quotes_left_out()
    {
        var groups = ErrorGroup.Of(
        [
            Error(1, "Order 1234 failed for \"ann\""),
            Error(2, "Order 98 failed for \"bob\""),
            Error(3, "Order 98 failed for \"bob\"", app: "billing"),
            Error(4, "Something else"),
        ]);

        Assert.Equal([1, 1, 2], groups.Select(g => g.Count).Order());
        Assert.Equal("billing", groups.Single(g => g.Count == 1 && g.Latest.Message.StartsWith("Order")).App);
    }

    [Fact]
    public void Finds_the_frames_that_name_a_file()
    {
        var frames = ErrorGroup.FramesOf(Stack + "\n   at System.Threading.Tasks.Task.Run()");

        Assert.Equal(2, frames.Count);
        Assert.Equal(("Joktan.Mqtt.Client.ConnectAsync()", "/build/src/Joktan.Mqtt/Client.cs", 42), (frames[0].Method, frames[0].File, frames[0].Line));
    }

    [Theory]
    [InlineData("  File \"/app/billing/charge.py\", line 12, in charge", "/app/billing/charge.py", 12)]
    [InlineData("    at com.example.Billing.charge(Billing.java:88)", "Billing.java", 88)]
    [InlineData("    at charge (/srv/app/src/billing.js:17:5)", "/srv/app/src/billing.js", 17)]
    public void Finds_frames_of_other_languages(string line, string file, int number)
    {
        var frame = Assert.Single(ErrorGroup.FramesOf(line));

        Assert.Equal((file, number), (frame.File, frame.Line));
        Assert.Null(frame.Method);
    }
}
