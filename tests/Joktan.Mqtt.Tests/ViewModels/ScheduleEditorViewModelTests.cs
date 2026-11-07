using System;
using Joktan.Mqtt.ViewModels;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class ScheduleEditorViewModelTests
{
    [Fact]
    public void With_manual_the_choices_follow_the_schedule_types()
    {
        var editor = new ScheduleEditorViewModel(MessageSchedule.Manual);

        Assert.Equal(4, editor.TypeChoices.Count);
        Assert.Equal(0, editor.ScheduleTypeIndex);
        Assert.Equal(ScheduleType.Manual, editor.Build().Type);
        Assert.Equal("When to send", editor.WhenLabel);
        Assert.Equal("Maximum sends (optional)", editor.MaximumLabel);
    }

    [Fact]
    public void Without_manual_the_first_choice_is_once()
    {
        var editor = new ScheduleEditorViewModel(MessageSchedule.Manual, allowsManual: false, "When to start", "Maximum runs (optional)");

        Assert.Equal(3, editor.TypeChoices.Count);
        Assert.Equal(0, editor.ScheduleTypeIndex);
        Assert.True(editor.IsOnce);
        Assert.Equal("When to start", editor.WhenLabel);
        Assert.Equal("Maximum runs (optional)", editor.MaximumLabel);

        editor.ScheduleTypeIndex = 1;
        editor.Every = "15";
        editor.UnitIndex = (int)IntervalUnit.Minutes;
        var schedule = editor.Build();
        Assert.Equal(ScheduleType.Interval, schedule.Type);
        Assert.Equal(TimeSpan.FromMinutes(15), schedule.Period);
    }

    [Fact]
    public void Loading_a_schedule_shows_it()
    {
        var editor = new ScheduleEditorViewModel(MessageSchedule.Manual, allowsManual: false);

        editor.Load(new MessageSchedule { Type = ScheduleType.Daily, Times = [new TimeOnly(6, 5)], Days = [DayOfWeek.Sunday] });

        Assert.True(editor.IsDaily);
        Assert.Equal("06:05", editor.DailyTimes);
        Assert.True(editor.Sunday);
        Assert.False(editor.Monday);
    }
}
