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
}
