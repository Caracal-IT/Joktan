using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Core.Tests;

[Collection(ProcessStateCollection.Name)]
public class AppLogTests : DataFolderTest
{
    [Fact]
    public void Without_telemetry_logging_goes_nowhere() => AppLog.For("Mqtt").LogInformation("Nothing listens");

    [Fact]
    public void A_logger_is_for_a_module()
    {
        using var logs = new CapturedLogs();

        AppLog.For("Mqtt").LogWarning("Something");

        Assert.Equal(("Joktan.Mqtt", LogLevel.Warning, "Something"), Assert.Single(logs.Lines));
        Assert.Equal("mqtt", AppLog.ModuleOf("Joktan.Mqtt"));
    }

    [Theory]
    [InlineData(LogLevel.Debug, false)]
    [InlineData(LogLevel.Information, true)]
    [InlineData(LogLevel.Error, true)]
    public void Levels_below_the_chosen_one_are_not_sent(LogLevel level, bool sent)
    {
        AppLog.Configure(new LoggingSettings());

        Assert.Equal(sent, AppLog.IsOn(level));
    }

    [Fact]
    public void Switched_off_nothing_is_sent()
    {
        AppLog.Configure(new LoggingSettings { Enabled = false });

        Assert.False(AppLog.IsOn(LogLevel.Critical));
        AppLog.Configure(new LoggingSettings());
    }

    [Fact]
    public void Settings_come_back_with_the_level_as_a_word()
    {
        new LoggingSettings { Enabled = false, MinimumLevel = LogLevel.Warning, Endpoint = "http://collector:4317" }.Save();

        var loaded = LoggingSettings.Load();

        Assert.False(loaded.Enabled);
        Assert.Equal(LogLevel.Warning, loaded.MinimumLevel);
        Assert.Equal("http://collector:4317", loaded.Endpoint);
        Assert.Contains("\"Warning\"", System.IO.File.ReadAllText(Data.At("settings", "logging.json")));
    }

    [Fact]
    public void A_file_that_cannot_be_read_leaves_logging_on()
    {
        Data.Write("settings/logging.json", "not json");

        Assert.True(LoggingSettings.Load().Enabled);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("http://localhost:4317", true)]
    [InlineData("https://collector", true)]
    [InlineData("localhost:4317", false)]
    [InlineData("ftp://collector", false)]
    public void Only_a_whole_web_address_is_an_endpoint(string text, bool valid)
        => Assert.Equal(valid, LoggingSettings.IsValidEndpoint(text));
}
