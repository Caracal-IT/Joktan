using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System;
using Joktan.Mqtt;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

// Without a broker the connection stays down, which is what these rely on: nothing leaves the machine.
public class FolderPublisherTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 2, 8, 0, 0);

    private readonly TempFolder _folder = new();
    private readonly List<ActivityEntry> _log = [];
    private readonly FolderPublisher _publisher;

    public FolderPublisherTests()
    {
        _publisher = new FolderPublisher(new MqttConnection(), _log.Add);
    }

    public void Dispose() => _folder.Dispose();

    private void WriteMessage(string name = "m", string body = """{"a":1}""", string? settings = null)
    {
        _folder.Write($"topic/{name}.json", body);
        if (settings is not null)
            _folder.Write($"topic/{name}.send.json", settings);
    }

    private FolderMessage Load()
    {
        _publisher.Apply(FolderPublisher.Read(_folder.Path), Now);
        return _publisher.Messages[0];
    }

    private const string Every10Seconds = """{"enabled":true,"schedule":{"type":"interval","every":10}}""";

    [Fact]
    public void Reading_no_folder_gives_no_messages_and_no_problem()
    {
        foreach (var folder in new[] { null, "" })
        {
            var scan = FolderPublisher.Read(folder);

            Assert.Empty(scan.Messages);
            Assert.Null(scan.Error);
        }
    }

    [Fact]
    public void Reading_a_missing_folder_says_so()
    {
        var scan = FolderPublisher.Read(_folder.At("missing"));

        Assert.Empty(scan.Messages);
        Assert.Equal("The folder does not exist", scan.Error);
    }

    [Fact]
    public void Applying_a_scan_takes_its_problem_and_messages()
    {
        WriteMessage();

        _publisher.Apply(FolderPublisher.Read(_folder.At("missing")), Now);
        Assert.Equal("The folder does not exist", _publisher.FolderError);
        Assert.Empty(_publisher.Messages);

        _publisher.Apply(FolderPublisher.Read(_folder.Path), Now);
        Assert.Null(_publisher.FolderError);
        Assert.Single(_publisher.Messages);
    }

    [Fact]
    public void Before_the_run_messages_rest()
    {
        WriteMessage(settings: Every10Seconds);

        var message = Load();

        Assert.False(_publisher.IsRunning);
        Assert.Equal(FolderMessageState.Waiting, message.State);
        Assert.Null(message.NextDue);
    }

    [Fact]
    public void Starting_schedules_what_is_switched_on()
    {
        WriteMessage(settings: Every10Seconds);
        var message = Load();

        _publisher.Start(Now);

        Assert.True(_publisher.IsRunning);
        Assert.Equal(FolderMessageState.Scheduled, message.State);
        Assert.Equal(Now, message.NextDue);
    }

    [Fact]
    public void Starting_leaves_alone_what_is_switched_off_or_has_no_schedule()
    {
        WriteMessage("a", settings: """{"schedule":{"type":"interval","every":10}}""");
        WriteMessage("b");
        Load();

        _publisher.Start(Now);

        Assert.Equal(FolderMessageState.Off, _publisher.Messages[0].State);
        Assert.Equal(FolderMessageState.Manual, _publisher.Messages[1].State);
    }

    [Fact]
    public void A_single_send_already_past_is_missed()
    {
        WriteMessage(settings: """{"enabled":true,"schedule":{"type":"once","at":"2026-11-01T00:00:00"}}""");
        var message = Load();

        _publisher.Start(Now);

        Assert.Equal(FolderMessageState.Missed, message.State);
    }

    [Fact]
    public void An_interval_already_over_is_finished()
    {
        WriteMessage(settings: """{"enabled":true,"schedule":{"type":"interval","every":10,"end":"2026-11-01T00:00:00"}}""");
        var message = Load();

        _publisher.Start(Now);

        Assert.Equal(FolderMessageState.Finished, message.State);
    }

    [Fact]
    public void Starting_starts_the_count_again()
    {
        WriteMessage(settings: Every10Seconds);
        var message = Load();
        message.SentCount = 5;
        message.Counters["n"] = 5;

        _publisher.Start(Now);

        Assert.Equal(0, message.SentCount);
        Assert.Empty(message.Counters);
    }

    [Fact]
    public void Stopping_returns_messages_to_rest()
    {
        WriteMessage(settings: Every10Seconds);
        var message = Load();
        _publisher.Start(Now);

        _publisher.Stop();

        Assert.False(_publisher.IsRunning);
        Assert.Equal(FolderMessageState.Waiting, message.State);
        Assert.Null(message.NextDue);
    }

    [Fact]
    public async Task A_tick_does_nothing_unless_running()
    {
        WriteMessage(settings: Every10Seconds);
        Load();

        Assert.False(await _publisher.TickAsync(Now));
        Assert.Empty(_log);
    }

    [Fact]
    public async Task A_tick_does_nothing_before_a_message_is_due()
    {
        WriteMessage(settings: Every10Seconds);
        Load();
        _publisher.Start(Now);

        Assert.False(await _publisher.TickAsync(Now.AddSeconds(-1)));
        Assert.Empty(_log);
    }

    [Fact]
    public async Task Without_a_connection_a_due_interval_send_is_skipped_and_the_schedule_goes_on()
    {
        WriteMessage(settings: Every10Seconds);
        var message = Load();
        _publisher.Start(Now);

        Assert.True(await _publisher.TickAsync(Now));

        var entry = Assert.Single(_log);
        Assert.Equal("Skipped", entry.Event);
        Assert.Equal("topic", entry.Topic);
        Assert.Equal("m.json", entry.Source);
        Assert.Equal("No connection", entry.Detail);
        Assert.Equal(FolderMessageState.Scheduled, message.State);
        Assert.Equal(Now.AddSeconds(10), message.NextDue);
        Assert.Equal(0, message.ScheduledSends);
        Assert.Equal(0, message.SentCount);
    }

    [Fact]
    public async Task Without_a_connection_a_single_send_is_missed()
    {
        WriteMessage(settings: """{"enabled":true,"schedule":{"type":"once","at":"2026-11-02T09:00:00"}}""");
        var message = Load();
        _publisher.Start(Now);

        await _publisher.TickAsync(Now.AddHours(1));

        Assert.Equal(FolderMessageState.Missed, message.State);
        Assert.Null(message.NextDue);
    }

    [Fact]
    public async Task An_interval_that_reaches_its_end_finishes()
    {
        WriteMessage(settings: """{"enabled":true,"schedule":{"type":"interval","every":10,"end":"2026-11-02T08:00:05"}}""");
        var message = Load();
        _publisher.Start(Now);

        await _publisher.TickAsync(Now);

        Assert.Equal(FolderMessageState.Finished, message.State);
    }

    [Fact]
    public void Switching_on_while_running_schedules_from_now()
    {
        WriteMessage(settings: """{"schedule":{"type":"interval","every":10}}""");
        var message = Load();
        _publisher.Start(Now);
        message.SentCount = 3;

        _publisher.SetEnabled(message, true, Now);

        Assert.True(message.Settings.Enabled);
        Assert.Equal(FolderMessageState.Scheduled, message.State);
        Assert.Equal(Now, message.NextDue);
        Assert.Equal(0, message.SentCount);
    }

    [Fact]
    public void Switching_off_keeps_the_count()
    {
        WriteMessage(settings: Every10Seconds);
        var message = Load();
        _publisher.Start(Now);
        message.SentCount = 3;

        _publisher.SetEnabled(message, false, Now);

        Assert.Equal(FolderMessageState.Off, message.State);
        Assert.Null(message.NextDue);
        Assert.Equal(3, message.SentCount);
    }

    [Fact]
    public void Switching_on_while_stopped_waits_for_the_connection()
    {
        WriteMessage(settings: """{"schedule":{"type":"interval","every":10}}""");
        var message = Load();

        _publisher.SetEnabled(message, true, Now);

        Assert.Equal(FolderMessageState.Waiting, message.State);
    }

    [Fact]
    public void A_message_without_a_schedule_has_no_switch()
    {
        WriteMessage();
        var message = Load();

        _publisher.SetEnabled(message, true, Now);

        Assert.False(message.Settings.Enabled);
    }

    [Fact]
    public async Task Sending_an_invalid_message_does_nothing()
    {
        WriteMessage(body: "not json");
        var message = Load();

        await _publisher.SendNowAsync(message, Now);

        Assert.Empty(_log);
    }

    [Fact]
    public async Task Sending_without_a_connection_fails_and_says_so()
    {
        WriteMessage();
        var message = Load();

        await _publisher.SendNowAsync(message, Now);

        var entry = Assert.Single(_log);
        Assert.Equal("Failed", entry.Event);
        Assert.Equal("Not connected", entry.Detail);
        Assert.Equal(0, message.SentCount);
        Assert.Null(message.LastSent);
    }

    [Fact]
    public async Task A_topic_field_can_make_a_topic_that_cannot_be_sent_to()
    {
        WriteMessage(settings: """{"topic":"a/{t}","topicFields":{"t":{"type":"fixed","value":"+"}}}""");
        var message = Load();
        Assert.True(message.IsValid, message.Error);

        await _publisher.SendNowAsync(message, Now);

        var entry = Assert.Single(_log);
        Assert.Equal("Failed", entry.Event);
        Assert.Equal("a/+", entry.Topic);
        Assert.Equal("Topic a/+: A topic may not contain + or #", entry.Detail);
    }

    [Fact]
    public void A_new_scan_keeps_the_progress_of_messages_still_there()
    {
        WriteMessage(settings: Every10Seconds);
        var first = Load();
        first.SentCount = 2;
        first.LastSent = Now;
        first.Counters["n"] = 9;

        var second = Load();

        Assert.NotSame(first, second);
        Assert.Equal(2, second.SentCount);
        Assert.Equal(Now, second.LastSent);
        Assert.Equal(9, second.Counters["n"]);
    }

    [Fact]
    public void A_new_scan_while_running_keeps_an_unchanged_schedule_in_its_place()
    {
        WriteMessage(settings: Every10Seconds);
        var first = Load();
        _publisher.Start(Now);
        first.NextDue = Now.AddSeconds(30);
        first.ScheduledSends = 3;

        var second = Load();

        Assert.Equal(FolderMessageState.Scheduled, second.State);
        Assert.Equal(Now.AddSeconds(30), second.NextDue);
        Assert.Equal(3, second.ScheduledSends);
    }

    [Fact]
    public void A_new_scan_while_running_starts_a_changed_schedule_again()
    {
        WriteMessage(settings: Every10Seconds);
        var first = Load();
        _publisher.Start(Now);
        first.NextDue = Now.AddSeconds(30);
        first.ScheduledSends = 3;
        first.SentCount = 3;

        WriteMessage(settings: """{"enabled":true,"schedule":{"type":"interval","every":20}}""");
        var second = Load();

        Assert.Equal(Now, second.NextDue);
        Assert.Equal(0, second.ScheduledSends);
        Assert.Equal(3, second.SentCount);
    }

    [Fact]
    public void A_new_scan_while_running_starts_a_message_just_switched_on_from_nothing()
    {
        WriteMessage(settings: """{"schedule":{"type":"interval","every":10}}""");
        var first = Load();
        _publisher.Start(Now);
        first.SentCount = 3;

        WriteMessage(settings: Every10Seconds);
        var second = Load();

        Assert.Equal(FolderMessageState.Scheduled, second.State);
        Assert.Equal(0, second.SentCount);
    }

    [Fact]
    public void A_new_message_found_while_running_is_scheduled()
    {
        _publisher.Start(Now);
        WriteMessage(settings: Every10Seconds);

        var message = Load();

        Assert.Equal(FolderMessageState.Scheduled, message.State);
        Assert.Equal(Now, message.NextDue);
    }
}
