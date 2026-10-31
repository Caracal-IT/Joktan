using System;
using System.Collections.Generic;
using Joktan.Mqtt.ViewModels;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class FolderMessageRowViewModelTests
{
    private static readonly MessageSchedule Interval = new() { Type = ScheduleType.Interval, Every = 5 };

    private readonly List<bool> _switched = [];

    private FolderMessageRowViewModel Row(MessageSettings? settings = null, string? error = null, string topic = "a/b")
    {
        var message = new FolderMessage
        {
            FilePath = "/a/m.json",
            FileName = "m.json",
            Topic = topic,
            Settings = settings ?? MessageSettings.Default,
            Error = error,
        };
        message.State = message.RestingState;
        return new FolderMessageRowViewModel(message, (_, on) => _switched.Add(on));
    }

    [Fact]
    public void Shows_a_dash_for_what_is_not_there()
    {
        var row = Row(topic: "");

        Assert.Equal("–", row.Topic);
        Assert.Equal("–", row.NextSend);
        Assert.Equal("–", row.LastSent);
        Assert.Equal("0", row.Sent);
        Assert.Equal("m.json", row.File);
    }

    [Fact]
    public void Shows_the_schedule_of_a_valid_message_only()
    {
        Assert.Equal("Every 5 seconds", Row(new MessageSettings { Schedule = Interval }).Schedule);
        Assert.Equal("–", Row(error: "wrong").Schedule);
    }

    [Fact]
    public void Shows_a_time_today_without_the_date()
    {
        var row = Row();
        var today = DateTime.Today.AddHours(8).AddMinutes(5);
        row.Message.NextDue = today;
        row.Message.LastSent = new DateTime(2020, 1, 2, 3, 4, 0);

        Assert.Equal("08:05:00", row.NextSend);
        Assert.Equal("2020-01-02 03:04", row.LastSent);
    }

    [Theory]
    [InlineData(FolderMessageState.Manual, "No schedule")]
    [InlineData(FolderMessageState.Waiting, "Waiting for connection")]
    [InlineData(FolderMessageState.Off, "Off")]
    [InlineData(FolderMessageState.Scheduled, "Scheduled")]
    [InlineData(FolderMessageState.Finished, "Finished")]
    [InlineData(FolderMessageState.Missed, "Missed")]
    public void Describes_the_state(FolderMessageState state, string expected)
    {
        var row = Row();
        row.Message.State = state;

        Assert.Equal(expected, row.State);
    }

    [Fact]
    public void An_invalid_message_says_why()
    {
        var row = Row(error: "wrong");

        Assert.Equal("Invalid: wrong", row.State);
        Assert.True(row.IsInvalid);
        Assert.False(row.CanSwitch);
        Assert.Equal("This message cannot be sent", row.SwitchTip);
    }

    [Fact]
    public void Only_a_message_with_a_schedule_can_be_switched()
    {
        var manual = Row();

        manual.IsOn = true;

        Assert.False(manual.CanSwitch);
        Assert.False(manual.IsOn);
        Assert.Empty(_switched);
        Assert.Equal("Give the message a schedule with Edit to switch it on", manual.SwitchTip);
    }

    [Fact]
    public void Switching_reports_a_change_only()
    {
        var row = Row(new MessageSettings { Schedule = Interval });
        Assert.Equal("Send this message on its schedule", row.SwitchTip);

        row.IsOn = false;
        Assert.Empty(_switched);

        row.IsOn = true;
        Assert.Equal([true], _switched);
    }

    [Fact]
    public void Refreshing_tells_every_column()
    {
        var row = Row();
        string? changed = "none";
        row.PropertyChanged += (_, e) => changed = e.PropertyName;

        row.Refresh();

        Assert.Equal("", changed);
    }
}
