using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Mqtt;

namespace Joktan.ViewModels;

/// <summary>
/// One varying field in the message settings editor.
/// </summary>
public partial class FieldRuleViewModel : ViewModelBase
{
    public static IReadOnlyList<string> TypeNames { get; } =
    [
        "Unix time (seconds)", "Unix time (milliseconds)", "ISO timestamp", "GUID",
        "Counter", "Random integer", "Random number", "One of",
    ];

    public FieldRuleViewModel()
    {
    }

    public FieldRuleViewModel(FieldRule rule)
    {
        Path = rule.Path;
        TypeIndex = (int)rule.Type;
        Start = rule.Start.ToString(CultureInfo.InvariantCulture);
        Step = rule.Step.ToString(CultureInfo.InvariantCulture);
        if (rule.Type is FieldType.RandomInteger or FieldType.RandomNumber)
        {
            Min = rule.Min.ToString(CultureInfo.InvariantCulture);
            Max = rule.Max.ToString(CultureInfo.InvariantCulture);
        }

        Decimals = rule.Decimals.ToString(CultureInfo.InvariantCulture);
        Values = string.Join(", ", rule.Values.Select(v => v?.ToJsonString() ?? "null"));
    }

    [ObservableProperty]
    public partial string Path { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowCounter), nameof(ShowRange), nameof(ShowDecimals), nameof(ShowValues))]
    public partial int TypeIndex { get; set; }

    [ObservableProperty]
    public partial string Start { get; set; } = "1";

    [ObservableProperty]
    public partial string Step { get; set; } = "1";

    [ObservableProperty]
    public partial string Min { get; set; } = "0";

    [ObservableProperty]
    public partial string Max { get; set; } = "100";

    [ObservableProperty]
    public partial string Decimals { get; set; } = "2";

    [ObservableProperty]
    public partial string Values { get; set; } = "";

    public FieldType Type => (FieldType)Math.Max(TypeIndex, 0);

    public bool ShowCounter => Type == FieldType.Counter;

    public bool ShowRange => Type is FieldType.RandomInteger or FieldType.RandomNumber;

    public bool ShowDecimals => Type == FieldType.RandomNumber;

    public bool ShowValues => Type == FieldType.OneOf;

    /// <exception cref="FormatException">A setting is missing or not a number.</exception>
    public FieldRule ToRule()
    {
        var path = Path.Trim();
        if (path.Length == 0)
            throw new FormatException("Every varying field needs a field path");

        var rule = new FieldRule(path, Type);
        rule = Type switch
        {
            FieldType.Counter => rule with
            {
                Start = Whole(Start, "start"),
                Step = Whole(Step, "step"),
            },
            FieldType.RandomInteger => rule with
            {
                Min = Whole(Min, "minimum"),
                Max = Whole(Max, "maximum"),
            },
            FieldType.RandomNumber => rule with
            {
                Min = Number(Min, "minimum"),
                Max = Number(Max, "maximum"),
                Decimals = (int)Whole(Decimals, "decimals"),
            },
            FieldType.OneOf => rule with { Values = ParseValues() },
            _ => rule,
        };

        if (MessageSettings.CheckRule(rule) is { } problem)
            throw new FormatException($"Field {path}: {problem.Replace("\"", "")}");
        return rule;

        long Whole(string text, string name)
            => long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException($"Field {path}: the {name} must be a whole number");

        double Number(string text, string name)
            => double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new FormatException($"Field {path}: the {name} must be a number");
    }

    // Each comma-separated item is read as JSON when it is JSON (42, true, "text"), otherwise as text.
    private JsonNode?[] ParseValues()
    {
        return Values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item =>
            {
                try
                {
                    return JsonNode.Parse(item);
                }
                catch (JsonException)
                {
                    return JsonValue.Create(item);
                }
            })
            .ToArray();
    }
}
