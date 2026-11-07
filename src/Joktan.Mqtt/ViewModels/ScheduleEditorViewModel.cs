using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// Edits a schedule: once, at an interval or daily. A folder message's settings use it, with Manual among the
/// choices, and so does a workflow's schedule trigger, without it.
/// </summary>
public partial class ScheduleEditorViewModel : ViewModelBase
{
    public const string DateDisplayFormat = "yyyy-MM-dd HH:mm:ss";
    public const string DateExample = "2026-11-02 08:30:00";

    /// <param name="allowsManual">Whether Manual is a choice; without it, a manual schedule is shown as the first other choice.</param>
    /// <param name="whenLabel">The label over the choice of schedule, such as When to send.</param>
    /// <param name="maximumLabel">What the most times an interval schedule may fire is called, such as Maximum sends (optional).</param>
    public ScheduleEditorViewModel(MessageSchedule schedule, bool allowsManual = true, string? whenLabel = null, string? maximumLabel = null)
    {
        AllowsManual = allowsManual;
        WhenLabel = whenLabel ?? Strings.MessageSettingsWindow_WhenToSend;
        MaximumLabel = maximumLabel ?? Strings.MessageSettingsWindow_MaximumSendsOptional;
        TypeChoices =
        [
            .. allowsManual ? [Strings.MessageSettingsWindow_ManualOnlyWhenIPressSend] : Array.Empty<string>(),
            Strings.MessageSettingsWindow_OnceAtADateAndTime,
            Strings.MessageSettingsWindow_RecurringAtAFixedInterval,
            Strings.MessageSettingsWindow_RecurringDailyAtSetTimes,
        ];
        Load(schedule);
    }

    public bool AllowsManual { get; }

    public string WhenLabel { get; }

    public string MaximumLabel { get; }

    public IReadOnlyList<string> TypeChoices { get; }

    /// <summary>Which of <see cref="TypeChoices"/> is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleType), nameof(IsOnce), nameof(IsInterval), nameof(IsDaily))]
    public partial int ScheduleTypeIndex { get; set; }

    [ObservableProperty]
    public partial string OnceAt { get; set; } = "";

    [ObservableProperty]
    public partial string Every { get; set; } = "5";

    [ObservableProperty]
    public partial int UnitIndex { get; set; }

    [ObservableProperty]
    public partial string IntervalStart { get; set; } = "";

    [ObservableProperty]
    public partial string IntervalEnd { get; set; } = "";

    [ObservableProperty]
    public partial string MaxSends { get; set; } = "";

    [ObservableProperty]
    public partial string DailyTimes { get; set; } = "08:00";

    [ObservableProperty]
    public partial bool Monday { get; set; } = true;

    [ObservableProperty]
    public partial bool Tuesday { get; set; } = true;

    [ObservableProperty]
    public partial bool Wednesday { get; set; } = true;

    [ObservableProperty]
    public partial bool Thursday { get; set; } = true;

    [ObservableProperty]
    public partial bool Friday { get; set; } = true;

    [ObservableProperty]
    public partial bool Saturday { get; set; }

    [ObservableProperty]
    public partial bool Sunday { get; set; }

    public ScheduleType ScheduleType => (ScheduleType)(Math.Max(ScheduleTypeIndex, 0) + (AllowsManual ? 0 : 1));

    public bool IsOnce => ScheduleType == ScheduleType.Once;

    public bool IsInterval => ScheduleType == ScheduleType.Interval;

    public bool IsDaily => ScheduleType == ScheduleType.Daily;

    /// <summary>Shows a schedule, replacing what was typed.</summary>
    public void Load(MessageSchedule schedule)
    {
        ScheduleTypeIndex = AllowsManual ? (int)schedule.Type : Math.Max((int)schedule.Type - 1, 0);
        OnceAt = FormatDate(schedule.At);
        Every = schedule.Every.ToString(CultureInfo.InvariantCulture);
        UnitIndex = (int)schedule.Unit;
        IntervalStart = FormatDate(schedule.Start);
        IntervalEnd = FormatDate(schedule.End);
        MaxSends = schedule.MaxSends?.ToString(CultureInfo.InvariantCulture) ?? "";

        if (schedule.Type == ScheduleType.Daily)
        {
            DailyTimes = string.Join(", ", schedule.Times.Order().Select(t => t.ToString("HH:mm", CultureInfo.InvariantCulture)));
            Monday = schedule.Days.Contains(DayOfWeek.Monday);
            Tuesday = schedule.Days.Contains(DayOfWeek.Tuesday);
            Wednesday = schedule.Days.Contains(DayOfWeek.Wednesday);
            Thursday = schedule.Days.Contains(DayOfWeek.Thursday);
            Friday = schedule.Days.Contains(DayOfWeek.Friday);
            Saturday = schedule.Days.Contains(DayOfWeek.Saturday);
            Sunday = schedule.Days.Contains(DayOfWeek.Sunday);
        }
    }

    /// <summary>The schedule as typed, or a <see cref="FormatException"/> saying what is wrong with it.</summary>
    public MessageSchedule Build()
    {
        switch (ScheduleType)
        {
            case ScheduleType.Once:
                return new MessageSchedule
                {
                    Type = ScheduleType.Once,
                    At = ParseDate(OnceAt, Strings.MessageSettings_DateAndTime)
                         ?? throw new FormatException(string.Format(Strings.MessageSettings_EnterTheDateAndTimeTo, DateExample)),
                };

            case ScheduleType.Interval:
                if (!int.TryParse(Every.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var every) || every < 1)
                    throw new FormatException(Strings.MessageSettings_TheIntervalMustBeAWhole);

                int? maxSends = null;
                if (MaxSends.Trim().Length > 0)
                {
                    if (!int.TryParse(MaxSends.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var max) || max < 1)
                        throw new FormatException(Strings.MessageSettings_TheMaximumNumberOfSendsMust);
                    maxSends = max;
                }

                var start = ParseDate(IntervalStart, Strings.MessageSettings_Start);
                var end = ParseDate(IntervalEnd, Strings.MessageSettings_End);
                if (start > end)
                    throw new FormatException(Strings.MessageSettings_TheEndMustNotBeBefore);

                return new MessageSchedule
                {
                    Type = ScheduleType.Interval,
                    Every = every,
                    Unit = (IntervalUnit)Math.Max(UnitIndex, 0),
                    Start = start,
                    End = end,
                    MaxSends = maxSends,
                };

            case ScheduleType.Daily:
                var times = DailyTimes.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => TimeOnly.TryParseExact(t, ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var time)
                        ? time
                        : throw new FormatException(string.Format(Strings.MessageSettings_IsNotATimeOfDaySuch, t)))
                    .Distinct()
                    .ToArray();
                if (times.Length == 0)
                    throw new FormatException(Strings.MessageSettings_EnterAtLeastOneTimeOf);

                (bool Chosen, DayOfWeek Day)[] week =
                [
                    (Monday, DayOfWeek.Monday), (Tuesday, DayOfWeek.Tuesday), (Wednesday, DayOfWeek.Wednesday),
                    (Thursday, DayOfWeek.Thursday), (Friday, DayOfWeek.Friday), (Saturday, DayOfWeek.Saturday),
                    (Sunday, DayOfWeek.Sunday),
                ];
                var days = week.Where(d => d.Chosen).Select(d => d.Day).ToArray();
                if (days.Length == 0)
                    throw new FormatException(Strings.MessageSettings_ChooseAtLeastOneDay);

                return new MessageSchedule { Type = ScheduleType.Daily, Times = times, Days = days };

            default:
                return MessageSchedule.Manual;
        }
    }

    private static string FormatDate(DateTime? value)
        => value?.ToString(DateDisplayFormat, CultureInfo.InvariantCulture) ?? "";

    private static DateTime? ParseDate(string text, string name)
    {
        if (text.Trim().Length == 0)
            return null;

        return DateTime.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value
            : throw new FormatException(string.Format(Strings.MessageSettings_TheIsNotADateAnd, name, DateExample));
    }
}
