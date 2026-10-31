using System;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class FolderMessageTests
{
    private static readonly MessageSchedule Interval = new() { Type = ScheduleType.Interval, Every = 1 };

    private static FolderMessage Message(MessageSettings? settings = null, string? error = null) => new()
    {
        FilePath = "/a/m.json",
        FileName = "m.json",
        Topic = "a",
        Settings = settings ?? MessageSettings.Default,
        Error = error,
    };

    [Fact]
    public void An_invalid_message_rests_as_invalid_and_cannot_be_switched_on()
    {
        var message = Message(new MessageSettings { Enabled = true, Schedule = Interval }, "wrong");

        Assert.Equal(FolderMessageState.Invalid, message.RestingState);
        Assert.False(message.IsValid);
        Assert.False(message.HasSchedule);
    }

    [Fact]
    public void Resting_states_follow_the_schedule_and_the_switch()
    {
        Assert.Equal(FolderMessageState.Manual, Message().RestingState);
        Assert.Equal(FolderMessageState.Off, Message(new MessageSettings { Schedule = Interval }).RestingState);
        Assert.Equal(FolderMessageState.Waiting, Message(new MessageSettings { Enabled = true, Schedule = Interval }).RestingState);
    }

    [Fact]
    public void Carries_over_what_was_sent()
    {
        var previous = Message();
        previous.LastSent = new DateTime(2026, 11, 2);
        previous.SentCount = 3;
        previous.ScheduledSends = 2;
        previous.Counters["n"] = 5;

        var next = Message();
        next.CarryOver(previous);

        Assert.Equal(previous.LastSent, next.LastSent);
        Assert.Equal(3, next.SentCount);
        Assert.Equal(0, next.ScheduledSends);
        Assert.Equal(5, next.Counters["n"]);
    }

    [Fact]
    public void Resets_its_progress_but_not_when_it_was_last_sent()
    {
        var message = Message();
        var sent = new DateTime(2026, 11, 2);
        message.LastSent = sent;
        message.SentCount = 3;
        message.ScheduledSends = 2;
        message.Counters["n"] = 5;

        message.ResetProgress();

        Assert.Equal(0, message.SentCount);
        Assert.Equal(0, message.ScheduledSends);
        Assert.Empty(message.Counters);
        Assert.Equal(sent, message.LastSent);
    }
}
