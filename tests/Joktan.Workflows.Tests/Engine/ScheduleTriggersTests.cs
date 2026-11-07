using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Joktan.Mqtt;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Joktan.Workflows.Model;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

[Collection(ProcessStateCollection.Name)]
public class ScheduleTriggersTests : EngineTest
{
    private DateTime _clock = new(2030, 1, 7, 8, 0, 0);

    public ScheduleTriggersTests()
    {
        // The engine's clock is the test's, which moves only when the test moves it.
        Engine.Dispose();
        Engine = new WorkflowEngine(Library, Settings, Services, Store, now: () => _clock);
    }

    private static readonly MessageSchedule EveryMinute = new() { Type = ScheduleType.Interval, Every = 1, Unit = IntervalUnit.Minutes };

    private Flow Scheduled(MessageSchedule schedule, string input = "")
    {
        var flow = new Flow("Timed").Then(Flow.End("{{input}}"));
        flow.Workflow.Samples = [new NamedInput { Name = "Tank", Input = """{"tank": 7}""" }];
        flow.Workflow.Triggers = new WorkflowTriggers { Scheduled = true, Schedule = schedule, ScheduleInput = input };
        Library.Save(flow.Workflow);
        return flow;
    }

    private void At(DateTime time)
    {
        _clock = time;
        Engine.CheckSchedules();
    }

    [Fact]
    public async Task An_interval_schedule_starts_a_run_at_each_time_with_its_sample_input()
    {
        var flow = Scheduled(EveryMinute with { MaxSends = 2 }, "Tank");
        Engine.Restore();
        Assert.Equal(_clock, Engine.NextScheduledRun(flow.Workflow.Id));

        At(_clock.AddSeconds(30));
        Assert.Single(Engine.Runs);
        Assert.Equal(new DateTime(2030, 1, 7, 8, 1, 0), Engine.NextScheduledRun(flow.Workflow.Id));

        At(_clock.AddSeconds(10));
        Assert.Single(Engine.Runs);

        // Two minutes on, the minute in between is skipped rather than run late.
        At(new DateTime(2030, 1, 7, 8, 2, 30));
        await Until(() => Engine.Runs.Count == 2 && Engine.Runs.All(r => r.Status.IsFinished()), "The schedule did not start two runs.");
        Assert.Null(Engine.NextScheduledRun(flow.Workflow.Id));

        var run = Engine.Runs[0];
        Assert.Equal(RunTrigger.Schedule, run.Trigger);
        Assert.Equal("2030-01-07 08:00:00", run.TriggerDetail);
        Assert.Equal("""{"tank":7}""", JsonText.Of(run.Output));
        Assert.Contains(run.Log, l => l.Text.Contains("on its schedule"));

        At(_clock.AddHours(1));
        Assert.Equal(2, Engine.Runs.Count);
    }

    [Fact]
    public void A_daily_schedule_waits_for_its_time()
    {
        var flow = Scheduled(new MessageSchedule { Type = ScheduleType.Daily, Times = [new TimeOnly(9, 30)], Days = [DayOfWeek.Tuesday] });
        Engine.Restore();

        Assert.Equal(new DateTime(2030, 1, 8, 9, 30, 0), Engine.NextScheduledRun(flow.Workflow.Id));
        At(new DateTime(2030, 1, 8, 9, 29, 59));
        Assert.Empty(Engine.Runs);
        At(new DateTime(2030, 1, 8, 9, 30, 0));
        Assert.Single(Engine.Runs);
        Assert.Equal(new DateTime(2030, 1, 15, 9, 30, 0), Engine.NextScheduledRun(flow.Workflow.Id));
    }

    [Fact]
    public void A_time_that_has_passed_starts_nothing()
    {
        var flow = Scheduled(new MessageSchedule { Type = ScheduleType.Once, At = _clock.AddDays(-1) });
        Engine.Restore();

        At(_clock.AddMinutes(1));

        Assert.Empty(Engine.Runs);
        Assert.True(Engine.IsScheduleActive(flow.Workflow.Id));
        Assert.Null(Engine.NextScheduledRun(flow.Workflow.Id));
    }

    [Fact]
    public void A_changed_schedule_starts_again_from_now_and_an_unchanged_one_keeps_its_place()
    {
        var flow = Scheduled(EveryMinute with { Start = _clock.AddMinutes(10) });
        Engine.Restore();
        Assert.Equal(_clock.AddMinutes(10), Engine.NextScheduledRun(flow.Workflow.Id));

        _clock = _clock.AddMinutes(3);
        flow.Workflow.Description = "Only the description changed.";
        Library.Save(flow.Workflow);
        Assert.Equal(new DateTime(2030, 1, 7, 8, 10, 0), Engine.NextScheduledRun(flow.Workflow.Id));

        flow.Workflow.Triggers.Schedule = EveryMinute;
        Library.Save(flow.Workflow);
        Assert.Equal(_clock, Engine.NextScheduledRun(flow.Workflow.Id));

        flow.Workflow.Triggers.Scheduled = false;
        Library.Save(flow.Workflow);
        Assert.False(Engine.IsScheduleActive(flow.Workflow.Id));
        At(_clock.AddMinutes(5));
        Assert.Empty(Engine.Runs);
    }

    [Fact]
    public void Schedules_stop_when_the_settings_turn_them_off()
    {
        var flow = Scheduled(EveryMinute);
        Engine.Restore();

        Settings.ScheduleTriggers = false;
        Engine.RefreshTriggers();
        At(_clock.AddMinutes(1));

        Assert.False(Engine.IsScheduleActive(flow.Workflow.Id));
        Assert.Empty(Engine.Runs);
    }

    [Fact]
    public void A_sample_input_that_is_not_json_is_reported_and_starts_no_run()
    {
        var flow = Scheduled(EveryMinute, "Tank");
        flow.Workflow.Samples[0].Input = "{nope";
        Library.Save(flow.Workflow);
        Engine.Restore();

        At(_clock);

        Assert.Empty(Engine.Runs);
        Assert.Contains("not JSON", Engine.ScheduleProblems[flow.Workflow.Id]);
    }

    [Fact]
    public void A_schedule_is_written_as_a_folder_message_writes_it()
    {
        var flow = Scheduled(new MessageSchedule { Type = ScheduleType.Daily, Times = [new TimeOnly(8, 0)], Days = [DayOfWeek.Monday] }, "Tank");

        var json = JsonSerializer.Serialize(flow.Workflow);
        Assert.Contains("\"Schedule\":{\"type\":\"daily\",\"times\":[\"08:00\"],\"days\":[\"mon\"]}", json);

        var read = new WorkflowLibrary(Data.At("definitions")).Find(flow.Workflow.Id)!;
        Assert.True(read.Triggers.Scheduled);
        Assert.Equal("Tank", read.Triggers.ScheduleInput);
        Assert.Equal(WorkflowSchedules.Key(flow.Workflow.Triggers.Schedule), WorkflowSchedules.Key(read.Triggers.Schedule));
    }

    [Fact]
    public void A_workflow_saved_before_schedules_reads_with_the_default_schedule_off()
    {
        var workflow = JsonSerializer.Deserialize<Workflow>("""{"Id": "old", "Name": "Old", "Triggers": {"Rest": true}}""")!;

        Assert.False(workflow.Triggers.Scheduled);
        Assert.Equal(WorkflowSchedules.Key(WorkflowSchedules.Default), WorkflowSchedules.Key(workflow.Triggers.Schedule));
    }
}
