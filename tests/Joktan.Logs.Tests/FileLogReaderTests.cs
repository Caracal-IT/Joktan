using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Logs.Tests;

public class FileLogReaderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(2));

    private static LogQuery Day(params LogLevel[] levels)
        => new(Noon.AddHours(-12), Noon.AddHours(12), levels.Length > 0 ? levels.ToHashSet() : LogLevels.Errors);

    private static void WrittenAtNoon(string file) => File.SetLastWriteTimeUtc(file, Noon.UtcDateTime);

    [Fact]
    public void Reads_Joktans_own_lines_with_the_exception_after_them()
    {
        var entries = FileLogReader.Parse(
        [
            "2026-11-03T12:00:01.250+02:00 INFORMATION app=joktan module=app Joktan started",
            "2026-11-03T12:00:02.000+02:00 ERROR app=joktan module=mqtt Could not connect to broker-1",
            "System.InvalidOperationException: The broker went away",
            "   at Joktan.Mqtt.MqttClientViewModel.ConnectAsync() in /build/src/Joktan.Mqtt/MqttClientViewModel.cs:line 42",
            "",
        ]).ToList();

        Assert.Equal(2, entries.Count);
        var error = entries[1];
        Assert.Equal(LogLevel.Error, error.Level);
        Assert.Equal("joktan", error.App);
        Assert.Equal("mqtt", error.Module);
        Assert.Equal("Could not connect to broker-1", error.Message);
        Assert.Equal("System.InvalidOperationException", error.ErrorType);
        Assert.EndsWith("line 42", error.StackTrace);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 10, 0, 2, TimeSpan.Zero), error.Time);
        Assert.Null(entries[0].StackTrace);
    }

    [Theory]
    [InlineData("2026-11-03 12:00:00.123 +02:00 [ERR] Payment failed", LogLevel.Error, "Payment failed")]
    [InlineData("[2026-11-03 12:00:00,123] FATAL main - Out of memory", LogLevel.Critical, "main - Out of memory")]
    [InlineData("2026-11-03T10:00:00Z WARN cache: slow", LogLevel.Warning, "cache: slow")]
    [InlineData("2026-11-03 12:00:00 something without a level", LogLevel.Information, "something without a level")]
    public void Reads_other_apps_plain_lines(string line, LogLevel level, string message)
    {
        var entry = Assert.Single(FileLogReader.Parse([line]));

        Assert.Equal(level, entry.Level);
        Assert.Equal(message, entry.Message);
    }

    [Fact]
    public void Reads_JSON_lines_from_ECS_and_Serilog()
    {
        var entries = FileLogReader.Parse(
        [
            """{"@timestamp":"2026-11-03T10:00:00Z","log":{"level":"error"},"service":{"name":"billing"},"message":"Charge failed","error":{"type":"System.TimeoutException","stack_trace":"System.TimeoutException: slow\n   at A.B()"}}""",
            """{"@t":"2026-11-03T10:00:01Z","@m":"Started","SourceContext":"Billing.Host"}""",
            """{"@t":"2026-11-03T10:00:02Z","@l":"Fatal","@mt":"Crashed {Reason}","@x":"System.Exception: boom"}""",
            """{"not":"a log"}""",
        ]).ToList();

        Assert.Equal(3, entries.Count);
        Assert.Equal((LogLevel.Error, "billing", "Charge failed", "System.TimeoutException"), (entries[0].Level, entries[0].App, entries[0].Message, entries[0].ErrorType));
        Assert.Equal((LogLevel.Information, "Billing.Host"), (entries[1].Level, entries[1].Module));
        Assert.Equal((LogLevel.Critical, "Crashed {Reason}", "System.Exception"), (entries[2].Level, entries[2].Message, entries[2].ErrorType));
    }

    [Fact]
    public void Leaves_out_lines_before_the_first_log()
    {
        Assert.Empty(FileLogReader.Parse(["   at A.B()", "continued"]));
    }

    [Fact]
    public async Task Reads_a_folder_through_its_filter_and_only_into_subfolders_when_asked()
    {
        using var folder = new TempFolder();
        WrittenAtNoon(folder.Write("app.log", "2026-11-03T12:00:00+02:00 ERROR top\n"));
        WrittenAtNoon(folder.Write("notes.txt", "2026-11-03T12:00:00+02:00 ERROR not a log file\n"));
        WrittenAtNoon(folder.Write("old/app.log", "2026-11-03T11:00:00+02:00 ERROR inside\n"));

        var top = await new FileLogReader(folder.Path, "*.log", false).ReadAsync(Day(), TestContext.Current.CancellationToken);
        var all = await new FileLogReader(folder.Path, "*.log;*.txt", true).ReadAsync(Day(), TestContext.Current.CancellationToken);

        Assert.Equal(["top"], top.Entries.Select(e => e.Message));
        Assert.Equal(["inside", "not a log file", "top"], all.Entries.Select(e => e.Message).Order());
    }

    [Fact]
    public async Task Keeps_the_newest_in_the_window_at_the_levels_asked_for()
    {
        using var folder = new TempFolder();
        var lines = Enumerable.Range(0, 30).Select(i => $"2026-11-03T{i % 24:00}:00:00+02:00 {(i % 3 == 0 ? "WARN" : "ERROR")} number {i}");
        var file = folder.Write("app.log", string.Join('\n', lines));
        WrittenAtNoon(file);
        var query = new LogQuery(Noon.AddHours(-12), Noon.AddHours(-1), LogLevels.Errors, Limit: 3);

        var result = await new FileLogReader(file, "*.log", false).ReadAsync(query, TestContext.Current.CancellationToken);

        Assert.True(result.Truncated);
        Assert.Equal(["number 11", "number 10", "number 8"], result.Entries.Select(e => e.Message));
    }

    [Fact]
    public async Task Passes_over_files_last_written_before_the_window()
    {
        using var folder = new TempFolder();
        var file = folder.Write("app.log", "2026-11-03T12:00:00+02:00 ERROR stale clock\n");
        File.SetLastWriteTimeUtc(file, Noon.AddDays(-2).UtcDateTime);

        var result = await new FileLogReader(folder.Path, "*.log", false).ReadAsync(Day(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Entries);
        Assert.Null(result.Problem);
    }

    [Fact]
    public async Task Says_when_the_path_is_not_there()
    {
        var result = await new FileLogReader("/nowhere/at/all", "*.log", false).ReadAsync(Day(), TestContext.Current.CancellationToken);

        Assert.Equal("/nowhere/at/all is not there.", result.Problem);
    }

    [Theory]
    [InlineData("2026-11-03T12:00:00+0200", 10)]
    [InlineData("2026-11-03 12:00:00,5 +02:00", 10)]
    [InlineData("2026-11-03T12:00:00Z", 12)]
    public void Reads_times_in_the_usual_forms(string text, int utcHour)
    {
        Assert.Equal(utcHour, FileLogReader.ParseTime(text)!.Value.UtcDateTime.Hour);
    }

    [Fact]
    public void A_time_without_an_offset_is_in_this_computers_zone()
    {
        var time = FileLogReader.ParseTime("2026-11-03 12:00:00")!.Value;

        Assert.Equal(TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 11, 3, 12, 0, 0)), time.Offset);
    }
}
