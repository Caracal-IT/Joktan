using System.Collections.Generic;
using Joktan.Core;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Joktan.Dashboard.Telemetry;

/// <summary>Puts the app's name and the module on every log sent to a collector.</summary>
public sealed class LogLabels : BaseProcessor<LogRecord>
{
    public override void OnEnd(LogRecord data)
    {
        data.Attributes = new List<KeyValuePair<string, object?>>(data.Attributes ?? [])
        {
            new("app", AppLog.AppName),
            new("module", AppLog.ModuleOf(data.CategoryName)),
        };
    }
}
