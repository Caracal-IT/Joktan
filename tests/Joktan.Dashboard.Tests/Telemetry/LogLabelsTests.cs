using System.Collections.Generic;
using System.Linq;
using Joktan.Core;
using Joktan.Dashboard.Telemetry;
using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

[Collection(ProcessStateCollection.Name)]
public class LogLabelsTests
{
    private sealed class Keep(List<KeyValuePair<string, object?>> kept) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) => kept.AddRange(data.Attributes ?? []);
    }

    [Fact]
    public void Every_log_names_the_app_and_its_module_and_keeps_what_it_had()
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

    [Fact]
    public void The_chosen_level_applies_to_a_logger_already_made()
    {
        var kept = new List<KeyValuePair<string, object?>>();
        using var logs = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddFilter((_, _, level) => AppLog.IsOn(level));
            logging.AddOpenTelemetry(options =>
            {
                options.AddProcessor(new LogLabels());
                options.AddProcessor(new Keep(kept));
            });
        });
        var logger = logs.CreateLogger("Joktan.App");

        try
        {
            AppLog.Configure(new LoggingSettings { MinimumLevel = LogLevel.Warning });
            logger.LogInformation("Below the level");
            Assert.Empty(kept);

            logger.LogWarning("At the level");
            Assert.NotEmpty(kept);

            kept.Clear();
            AppLog.Configure(new LoggingSettings { Enabled = false });
            logger.LogError("Switched off");
            Assert.Empty(kept);
        }
        finally
        {
            AppLog.Configure(new LoggingSettings());
        }
    }
}
