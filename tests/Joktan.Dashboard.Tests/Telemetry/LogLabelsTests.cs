using System.Collections.Generic;
using System.Linq;
using Joktan.Dashboard.Telemetry;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

public class LogLabelsTests
{
    private sealed class Keep(List<KeyValuePair<string, object?>> kept) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) => kept.AddRange(data.Attributes ?? []);
    }

    [Fact]
    public void A_log_for_a_collector_names_the_app_and_module_and_keeps_what_it_had()
    {
        var kept = new List<KeyValuePair<string, object?>>();
        using (var logs = LoggerFactory.Create(logging => logging.AddOpenTelemetry(options =>
        {
            options.AddProcessor(new LogLabels());
            options.AddProcessor(new Keep(kept));
        })))
        {
            logs.CreateLogger("Joktan.Mqtt").LogInformation("Connection to {Broker}", "localhost:1883");
        }

        Assert.Equal("joktan", kept.Single(a => a.Key == "app").Value);
        Assert.Equal("mqtt", kept.Single(a => a.Key == "module").Value);
        Assert.Equal("localhost:1883", kept.Single(a => a.Key == "Broker").Value);
    }
}
