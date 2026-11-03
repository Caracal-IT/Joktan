using System;
using System.IO;
using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Core.Tests;

[Collection(ProcessStateCollection.Name)]
public class FileLogsTests : DataFolderTest
{
    private static readonly TimeSpan Never = TimeSpan.FromHours(1);

    [Fact]
    public void A_line_in_the_file_names_the_app_and_module()
    {
        AppLog.Configure(new TelemetrySettings { LogsToFile = true });
        var day = new DateTime(2026, 11, 2);
        using (var file = new FileLogSink(() => (Data.At("files"), 7), Never, () => day))
        using (var provider = new FileLoggerProvider(file))
        {
            provider.CreateLogger("Joktan.Mqtt").LogWarning("Could not connect");
        }

        var line = Assert.Single(File.ReadAllLines(Data.At("files", "joktan-2026-11-02.log")));
        Assert.Contains("WARNING app=joktan module=mqtt Could not connect", line);
        AppLog.Configure(new TelemetrySettings());
    }

    [Fact]
    public void Old_files_are_deleted_and_nothing_else()
    {
        var old = Data.Write("files/joktan-2026-10-01.log", "");
        var recent = Data.Write("files/joktan-2026-11-01.log", "");
        var other = Data.Write("files/joktan-notes.log", "");
        var unrelated = Data.Write("files/other-2020-01-01.log", "");
        using var file = new FileLogSink(() => (Data.At("files"), 7), Never, () => new DateTime(2026, 11, 2));

        file.Add("a line");
        file.Flush();

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent) && File.Exists(other) && File.Exists(unrelated));
    }

    [Fact]
    public void A_file_starts_with_its_first_line()
    {
        using (var file = new FileLogSink(() => (Data.At("files"), 7), Never, () => new DateTime(2026, 11, 2)))
        {
            file.Add("a line");
            file.Flush();
        }

        Assert.Equal((byte)'a', File.ReadAllBytes(Data.At("files", "joktan-2026-11-02.log"))[0]);
    }

    [Fact]
    public void What_cannot_be_written_is_reported_and_kept()
    {
        var blocked = Data.Write("a-file", "");
        using var file = new FileLogSink(() => (blocked, 7), Never);

        file.Add("a line");
        file.Flush();
        Assert.NotNull(FileLogSink.LastProblem);
    }
}
