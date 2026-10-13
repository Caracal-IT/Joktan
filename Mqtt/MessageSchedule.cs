using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Joktan.Mqtt;

public enum ScheduleType
{
    Manual,
    Once,
    Interval,
    Daily,
}

public enum IntervalUnit
{
    Seconds,
    Minutes,
    Hours,
}

/// <summary>
/// When a folder message is sent. All dates and times are local.
/// </summary>
public sealed record MessageSchedule
{
    public static readonly MessageSchedule Manual = new();

    public ScheduleType Type { get; init; } = ScheduleType.Manual;

    public DateTime? At { get; init; }

    public int Every { get; init; } = 1;

    public IntervalUnit Unit { get; init; } = IntervalUnit.Seconds;

    public DateTime? Start { get; init; }

    public DateTime? End { get; init; }

    public int? MaxSends { get; init; }

    public IReadOnlyList<TimeOnly> Times { get; init; } = [];

    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];

    public bool IsRecurring => Type is ScheduleType.Interval or ScheduleType.Daily;

    public TimeSpan Period => Unit switch
    {
        IntervalUnit.Minutes => TimeSpan.FromMinutes(Every),
        IntervalUnit.Hours => TimeSpan.FromHours(Every),
        _ => TimeSpan.FromSeconds(Every),
    };

    /// <summary>The first send of a run that starts at <paramref name="runStart"/>, or null if there is none.</summary>
    public DateTime? FirstDue(DateTime runStart)
    {
        switch (Type)
        {
            case ScheduleType.Once:
                return At >= runStart ? At : null;

            case ScheduleType.Interval:
                var first = Start ?? runStart;
                if (first < runStart)
                    first = AlignForward(first, runStart);
                return WithinEnd(first);

            case ScheduleType.Daily:
                return NextDaily(runStart, inclusive: true);

            default:
                return null;
        }
    }

    /// <summary>
    /// The send that follows <paramref name="previousDue"/>. Sends that are already in the past
    /// at <paramref name="now"/> are skipped rather than sent late.
    /// </summary>
    public DateTime? NextDue(DateTime previousDue, DateTime now)
    {
        switch (Type)
        {
            case ScheduleType.Interval:
                var next = previousDue + Period;
                if (next < now)
                    next = AlignForward(previousDue, now);
                return WithinEnd(next);

            case ScheduleType.Daily:
                return NextDaily(previousDue > now ? previousDue : now, inclusive: false);

            default:
                return null;
        }
    }

    public string Describe()
    {
        switch (Type)
        {
            case ScheduleType.Once:
                return $"Once at {At:yyyy-MM-dd HH:mm:ss}";

            case ScheduleType.Interval:
                var unit = Unit.ToString().ToLowerInvariant();
                var text = Every == 1 ? $"Every {unit.TrimEnd('s')}" : $"Every {Every} {unit}";
                return MaxSends is { } max ? $"{text}, {max} times" : text;

            case ScheduleType.Daily:
                var times = string.Join(", ", Times.Order().Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)));
                return $"Daily at {times}, {DescribeDays()}";

            default:
                return "Manual";
        }
    }

    // The first slot on the anchor's grid that is not before the target.
    private DateTime AlignForward(DateTime anchor, DateTime target)
    {
        var periods = Math.Ceiling((target - anchor).Ticks / (double)Period.Ticks);
        return anchor + TimeSpan.FromTicks((long)periods * Period.Ticks);
    }

    private DateTime? WithinEnd(DateTime due) => End is { } end && due > end ? null : due;

    private DateTime? NextDaily(DateTime from, bool inclusive)
    {
        if (Times.Count == 0 || Days.Count == 0)
            return null;

        for (var offset = 0; offset <= 7; offset++)
        {
            var date = from.Date.AddDays(offset);
            if (!Days.Contains(date.DayOfWeek))
                continue;

            foreach (var time in Times.Order())
            {
                var candidate = date + time.ToTimeSpan();
                if (candidate > from || (inclusive && candidate == from))
                    return candidate;
            }
        }

        return null;
    }

    private string DescribeDays()
    {
        DayOfWeek[] week =
        [
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
            DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday,
        ];
        var chosen = week.Where(Days.Contains).ToArray();

        if (chosen.Length == 7)
            return "every day";
        if (chosen.SequenceEqual(week[..5]))
            return "Mon–Fri";
        return string.Join(", ", chosen.Select(d => d.ToString()[..3]));
    }
}
