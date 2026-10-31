using System;
using Joktan.Mqtt;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class MessageScheduleTests
{
    // A Monday.
    private static readonly DateTime Monday = new(2026, 11, 2);
    private static readonly DateTime Start = Monday.AddHours(8);

    private static MessageSchedule Every10Seconds(DateTime? start = null, DateTime? end = null) => new()
    {
        Type = ScheduleType.Interval,
        Every = 10,
        Start = start,
        End = end,
    };

    private static MessageSchedule DailyOnMondays(params TimeOnly[] times) => new()
    {
        Type = ScheduleType.Daily,
        Times = times,
        Days = [DayOfWeek.Monday],
    };

    [Theory]
    [InlineData(1, IntervalUnit.Seconds, 1)]
    [InlineData(5, IntervalUnit.Seconds, 5)]
    [InlineData(2, IntervalUnit.Minutes, 120)]
    [InlineData(3, IntervalUnit.Hours, 10800)]
    public void The_period_follows_the_unit(int every, IntervalUnit unit, int seconds)
    {
        var schedule = new MessageSchedule { Type = ScheduleType.Interval, Every = every, Unit = unit };

        Assert.Equal(TimeSpan.FromSeconds(seconds), schedule.Period);
    }

    [Theory]
    [InlineData(ScheduleType.Manual, false)]
    [InlineData(ScheduleType.Once, false)]
    [InlineData(ScheduleType.Interval, true)]
    [InlineData(ScheduleType.Daily, true)]
    public void Interval_and_daily_schedules_recur(ScheduleType type, bool expected)
        => Assert.Equal(expected, new MessageSchedule { Type = type }.IsRecurring);

    [Fact]
    public void A_manual_schedule_is_never_due()
    {
        Assert.Null(MessageSchedule.Manual.FirstDue(Start));
        Assert.Null(MessageSchedule.Manual.NextDue(Start, Start));
    }

    [Fact]
    public void Once_is_due_at_its_time_unless_that_has_passed()
    {
        var schedule = new MessageSchedule { Type = ScheduleType.Once, At = Start };

        Assert.Equal(Start, schedule.FirstDue(Start.AddSeconds(-1)));
        Assert.Equal(Start, schedule.FirstDue(Start));
        Assert.Null(schedule.FirstDue(Start.AddSeconds(1)));
    }

    [Fact]
    public void Once_has_no_next_send()
    {
        var schedule = new MessageSchedule { Type = ScheduleType.Once, At = Start };

        Assert.Null(schedule.NextDue(Start, Start));
    }

    [Fact]
    public void An_interval_without_a_start_begins_with_the_run()
    {
        Assert.Equal(Start, Every10Seconds().FirstDue(Start));
    }

    [Fact]
    public void An_interval_starting_later_waits_for_its_start()
    {
        var later = Start.AddMinutes(1);

        Assert.Equal(later, Every10Seconds(later).FirstDue(Start));
    }

    [Theory]
    [InlineData(25, 30)]
    [InlineData(30, 30)]
    [InlineData(31, 40)]
    public void An_interval_that_started_earlier_keeps_to_its_grid(int runAfter, int expectedAfter)
    {
        Assert.Equal(Start.AddSeconds(expectedAfter), Every10Seconds(Start).FirstDue(Start.AddSeconds(runAfter)));
    }

    [Fact]
    public void An_interval_that_has_ended_has_no_first_send()
    {
        Assert.Null(Every10Seconds(end: Start.AddSeconds(-1)).FirstDue(Start));
        Assert.Equal(Start, Every10Seconds(end: Start).FirstDue(Start));
    }

    [Fact]
    public void An_interval_steps_by_its_period()
    {
        Assert.Equal(Start.AddSeconds(10), Every10Seconds().NextDue(Start, Start));
        Assert.Equal(Start.AddSeconds(10), Every10Seconds().NextDue(Start, Start.AddSeconds(10)));
    }

    [Fact]
    public void An_interval_skips_sends_already_in_the_past()
    {
        Assert.Equal(Start.AddSeconds(40), Every10Seconds().NextDue(Start, Start.AddSeconds(35)));
    }

    [Fact]
    public void An_interval_stops_at_its_end()
    {
        var schedule = Every10Seconds(end: Start.AddSeconds(15));

        Assert.Equal(Start.AddSeconds(10), schedule.NextDue(Start, Start));
        Assert.Null(schedule.NextDue(Start.AddSeconds(10), Start.AddSeconds(10)));
    }

    [Fact]
    public void Daily_finds_the_first_time_at_or_after_the_run_start()
    {
        var schedule = DailyOnMondays(new TimeOnly(12, 0), new TimeOnly(8, 0));

        Assert.Equal(Monday.AddHours(8), schedule.FirstDue(Monday));
        Assert.Equal(Monday.AddHours(8), schedule.FirstDue(Monday.AddHours(8)));
        Assert.Equal(Monday.AddHours(12), schedule.FirstDue(Monday.AddHours(8).AddSeconds(1)));
    }

    [Fact]
    public void Daily_goes_on_to_the_next_chosen_day()
    {
        var schedule = DailyOnMondays(new TimeOnly(8, 0));

        Assert.Equal(Monday.AddDays(7).AddHours(8), schedule.FirstDue(Monday.AddHours(9)));
        Assert.Equal(Monday.AddDays(7).AddHours(8), schedule.FirstDue(Monday.AddDays(1)));
    }

    [Fact]
    public void Daily_never_repeats_the_send_just_made()
    {
        var schedule = DailyOnMondays(new TimeOnly(8, 0), new TimeOnly(12, 0));

        Assert.Equal(Monday.AddHours(12), schedule.NextDue(Monday.AddHours(8), Monday.AddHours(8)));
    }

    [Fact]
    public void Daily_counts_from_the_previous_send_if_the_clock_is_behind_it()
    {
        var schedule = DailyOnMondays(new TimeOnly(8, 0), new TimeOnly(12, 0));

        Assert.Equal(Monday.AddHours(12), schedule.NextDue(Monday.AddHours(8), Monday.AddHours(7)));
    }

    [Fact]
    public void Daily_skips_sends_already_in_the_past()
    {
        var schedule = DailyOnMondays(new TimeOnly(8, 0), new TimeOnly(12, 0));

        Assert.Equal(Monday.AddDays(7).AddHours(8), schedule.NextDue(Monday.AddHours(8), Monday.AddHours(13)));
    }

    [Fact]
    public void Daily_without_times_or_days_is_never_due()
    {
        Assert.Null(new MessageSchedule { Type = ScheduleType.Daily, Days = [DayOfWeek.Monday] }.FirstDue(Monday));
        Assert.Null(new MessageSchedule { Type = ScheduleType.Daily, Times = [new TimeOnly(8, 0)] }.FirstDue(Monday));
    }

    [Fact]
    public void Describes_a_manual_schedule() => Assert.Equal("Manual", MessageSchedule.Manual.Describe());

    [Fact]
    public void Describes_a_single_send()
    {
        var schedule = new MessageSchedule { Type = ScheduleType.Once, At = Monday.AddHours(8).AddMinutes(30) };

        Assert.Equal("Once at 2026-11-02 08:30:00", schedule.Describe());
    }

    [Theory]
    [InlineData(1, IntervalUnit.Seconds, null, "Every second")]
    [InlineData(1, IntervalUnit.Minutes, null, "Every minute")]
    [InlineData(1, IntervalUnit.Hours, 3, "Every hour, 3 times")]
    [InlineData(5, IntervalUnit.Minutes, null, "Every 5 minutes")]
    [InlineData(30, IntervalUnit.Seconds, 2, "Every 30 seconds, 2 times")]
    public void Describes_an_interval(int every, IntervalUnit unit, int? maxSends, string expected)
    {
        var schedule = new MessageSchedule { Type = ScheduleType.Interval, Every = every, Unit = unit, MaxSends = maxSends };

        Assert.Equal(expected, schedule.Describe());
    }

    [Fact]
    public void Describes_daily_times_in_order()
    {
        var schedule = new MessageSchedule
        {
            Type = ScheduleType.Daily,
            Times = [new TimeOnly(12, 0), new TimeOnly(8, 5)],
            Days = [DayOfWeek.Wednesday, DayOfWeek.Monday],
        };

        Assert.Equal("Daily at 08:05, 12:00, Mon, Wed", schedule.Describe());
    }

    [Fact]
    public void Describes_common_sets_of_days_briefly()
    {
        var times = new[] { new TimeOnly(8, 0) };
        var every = new MessageSchedule { Type = ScheduleType.Daily, Times = times, Days = Enum.GetValues<DayOfWeek>() };
        var working = new MessageSchedule
        {
            Type = ScheduleType.Daily,
            Times = times,
            Days = [DayOfWeek.Friday, DayOfWeek.Thursday, DayOfWeek.Wednesday, DayOfWeek.Tuesday, DayOfWeek.Monday],
        };
        var weekend = new MessageSchedule { Type = ScheduleType.Daily, Times = times, Days = [DayOfWeek.Sunday, DayOfWeek.Saturday] };

        Assert.Equal("Daily at 08:00, every day", every.Describe());
        Assert.Equal("Daily at 08:00, Mon–Fri", working.Describe());
        Assert.Equal("Daily at 08:00, Sat, Sun", weekend.Describe());
    }
}
