using System;
using System.IO;
using System.Linq;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

public class MessageSettingsViewModelTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private FolderMessage Message(string body = """{"a":1,"text":"x","list":[{"b":2}]}""", string? settings = null, string relative = "topic/a")
    {
        _folder.Write($"{relative}/m.json", body);
        if (settings is not null)
            _folder.Write($"{relative}/m.send.json", settings);
        return FolderPublisher.Read(_folder.Path).Messages.Single();
    }

    private MessageSettingsViewModel Editor(string? settings = null, string[]? groups = null) => new(Message(settings: settings), groups);

    [Fact]
    public void Starts_from_a_message_with_no_settings()
    {
        var vm = Editor(groups: ["G"]);

        Assert.Null(vm.Error);
        Assert.True(vm.SaveCommand.CanExecute(null));
        Assert.Equal("topic/a · m.json", vm.Title);
        Assert.Contains("a", vm.BodyPaths);
        Assert.Equal(["list"], vm.ArrayPaths);
        Assert.Equal(["G"], vm.GroupNames);
        Assert.Equal("topic/a (the folder path)", vm.TopicPlaceholder);
        Assert.False(vm.IsOnce);
        Assert.False(vm.IsInterval);
        Assert.False(vm.IsDaily);
        Assert.Contains("\"a\": 1", vm.Preview);
        Assert.Equal("topic/a", vm.TopicPreview);
        Assert.Empty(new MessageSettingsViewModel(Message()).GroupNames);
    }

    [Fact]
    public void Reads_a_settings_file_with_every_kind_of_schedule()
    {
        var once = Editor("""{"enabled":true,"group":"G","topic":"t","qos":1,"retain":true,"gzip":true,"schedule":{"type":"once","at":"2030-01-02T03:04:05"}}""");
        Assert.True(once.Enabled);
        Assert.Equal("G", once.Group);
        Assert.Equal("t", once.Topic);
        Assert.Equal(1, once.QosIndex);
        Assert.True(once.Retain);
        Assert.True(once.Gzip);
        Assert.True(once.IsOnce);
        Assert.Equal("2030-01-02 03:04:05", once.OnceAt);

        var interval = Editor("""{"schedule":{"type":"interval","every":7,"unit":"minutes","maxSends":3,"start":"2030-01-01T00:00:00","end":"2030-01-02T00:00:00"}}""");
        Assert.True(interval.IsInterval);
        Assert.Equal("7", interval.Every);
        Assert.Equal("3", interval.MaxSends);
        Assert.NotEmpty(interval.IntervalStart);
        Assert.NotEmpty(interval.IntervalEnd);

        var daily = Editor("""{"schedule":{"type":"daily","times":["12:30","08:00"],"days":["mon","sun"]}}""");
        Assert.True(daily.IsDaily);
        Assert.Equal("08:00, 12:30", daily.DailyTimes);
        Assert.True(daily.Monday);
        Assert.True(daily.Sunday);
        Assert.False(daily.Tuesday);
        Assert.False(daily.Wednesday);
        Assert.False(daily.Thursday);
        Assert.False(daily.Friday);
        Assert.False(daily.Saturday);
    }

    [Fact]
    public void Reads_its_fields_topic_fields_and_arrays()
    {
        var vm = Editor("""
            {"topic":"t/{x}",
             "topicFields":{"x":{"type":"cycle","values":["1","2"]}},
             "fields":{"a":{"type":"counter"}},
             "arrays":{"list":{"count":2}}}
            """);

        Assert.Single(vm.Fields);
        Assert.Single(vm.TopicFields);
        Assert.Single(vm.Arrays);
        Assert.Equal(["x"], vm.TopicNames);
        Assert.Null(vm.Error);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("")]
    public void A_once_schedule_needs_a_date(string at)
    {
        var vm = Editor();

        vm.ScheduleTypeIndex = (int)ScheduleType.Once;
        vm.OnceAt = at;

        Assert.NotNull(vm.Error);
        Assert.False(vm.SaveCommand.CanExecute(null));

        vm.OnceAt = "2030-01-01 00:00:00";
        Assert.Null(vm.Error);
    }

    [Fact]
    public void An_interval_schedule_is_checked()
    {
        var vm = Editor();
        vm.ScheduleTypeIndex = (int)ScheduleType.Interval;

        vm.Every = "0";
        Assert.StartsWith("The interval must be", vm.Error);
        vm.Every = "5";
        Assert.Null(vm.Error);

        vm.MaxSends = "0";
        Assert.StartsWith("The maximum number of sends", vm.Error);
        vm.MaxSends = "3";
        Assert.Null(vm.Error);

        vm.IntervalStart = "nonsense";
        Assert.StartsWith("The start is not a date", vm.Error);
        vm.IntervalStart = "2030-02-01";
        vm.IntervalEnd = "nonsense";
        Assert.StartsWith("The end is not a date", vm.Error);
        vm.IntervalEnd = "2030-01-01";
        Assert.Equal("The end must not be before the start", vm.Error);
        vm.IntervalEnd = "2030-03-01";
        Assert.Null(vm.Error);
    }

    [Fact]
    public void A_daily_schedule_is_checked()
    {
        var vm = Editor();
        vm.ScheduleTypeIndex = (int)ScheduleType.Daily;
        Assert.Null(vm.Error);

        vm.DailyTimes = "nope";
        Assert.Equal("nope is not a time of day such as 08:30", vm.Error);
        vm.DailyTimes = " ";
        Assert.StartsWith("Enter at least one time", vm.Error);
        vm.DailyTimes = "8:00; 12:30:00, 08:00";
        Assert.Null(vm.Error);

        vm.Monday = vm.Tuesday = vm.Wednesday = vm.Thursday = vm.Friday = false;
        Assert.Equal("Choose at least one day", vm.Error);
        vm.Saturday = true;
        Assert.Null(vm.Error);
        vm.Saturday = false;
        vm.Sunday = true;
        Assert.Null(vm.Error);
    }

    [Fact]
    public void A_list_clearing_its_choice_means_the_first()
    {
        var vm = Editor();

        vm.ScheduleTypeIndex = -1;

        Assert.Equal((ScheduleType)0, vm.ScheduleType);
    }

    [Fact]
    public void New_fields_suggest_those_with_no_rule_yet()
    {
        var vm = Editor();

        vm.AddFieldCommand.Execute(null);
        vm.AddFieldCommand.Execute(null);

        Assert.Equal(vm.BodyPaths[0], vm.Fields[0].Path);
        Assert.Equal(vm.BodyPaths[1], vm.Fields[1].Path);

        foreach (var _ in vm.BodyPaths)
            vm.AddFieldCommand.Execute(null);
        Assert.Contains(vm.Fields, f => f.Path == "");

        var first = vm.Fields[0];
        vm.RemoveFieldCommand.Execute(first);
        Assert.DoesNotContain(first, vm.Fields);
    }

    [Fact]
    public void Fields_are_checked_against_the_body()
    {
        var vm = Editor();
        vm.AddFieldCommand.Execute(null);

        vm.Fields[0].Path = "not.there";
        Assert.Equal("Field not.there is not in the message body", vm.Error);

        vm.Fields[0].Path = "a";
        Assert.Null(vm.Error);

        vm.AddFieldCommand.Execute(null);
        vm.Fields[1].Path = "a";
        Assert.Equal("Field a is listed more than once", vm.Error);
    }

    [Fact]
    public void A_fixed_field_starts_as_its_value_in_the_body()
    {
        var vm = Editor();
        vm.AddFieldCommand.Execute(null);
        var field = vm.Fields[0];
        field.Path = "text";

        field.TypeIndex = (int)FieldType.Fixed;

        Assert.Equal("x", field.FixedText);

        // Topic fields have no value in the body to start from.
        vm.AddTopicFieldCommand.Execute(null);
        vm.TopicFields[0].TypeIndex = (int)FieldType.Fixed;
        Assert.Equal("", vm.TopicFields[0].FixedText);
    }

    [Fact]
    public void Arrays_can_be_added_checked_and_removed()
    {
        var vm = Editor();

        vm.AddArrayCommand.Execute(null);
        Assert.Equal("list", vm.Arrays[0].Path);
        Assert.Null(vm.Error);

        vm.AddArrayCommand.Execute(null);
        Assert.Equal("", vm.Arrays[1].Path);
        Assert.NotNull(vm.Error);

        vm.Arrays[1].Path = "list";
        Assert.Equal("Array list is listed more than once", vm.Error);

        vm.RemoveArrayCommand.Execute(vm.Arrays[1]);
        Assert.Null(vm.Error);

        vm.Arrays[0].Path = "a";
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public void Topic_fields_are_named_in_the_topic()
    {
        var vm = Editor();
        vm.Topic = "t/{one}/{two}";
        Assert.Equal(["one", "two"], vm.TopicNames);
        Assert.NotNull(vm.Error);

        vm.AddTopicFieldCommand.Execute(null);
        vm.AddTopicFieldCommand.Execute(null);
        Assert.Equal("one", vm.TopicFields[0].Path);
        Assert.Equal("two", vm.TopicFields[1].Path);
        vm.TopicFields[0].Values = "a,b";
        vm.TopicFields[1].Values = "c";
        Assert.Null(vm.Error);
        Assert.Equal("t/a/c", vm.TopicPreview);

        vm.TopicFields[1].Path = "one";
        Assert.Equal("Topic field one is listed more than once", vm.Error);

        vm.TopicFields[1].Path = "not a name";
        Assert.StartsWith("not a name cannot be a topic field name", vm.Error);

        vm.AddTopicFieldCommand.Execute(null);
        Assert.Contains(vm.TopicFields, f => f.Path == "two" || f.Path == "");
    }

    [Fact]
    public void A_file_outside_a_topic_folder_needs_a_topic()
    {
        var vm = new MessageSettingsViewModel(Message(relative: "."));

        Assert.StartsWith("Required", vm.TopicPlaceholder);
        Assert.Equal("The file is not inside a topic folder, so enter a topic", vm.Error);

        vm.Topic = "chosen";
        Assert.Null(vm.Error);
    }

    [Fact]
    public void A_message_with_no_body_has_nothing_to_check_or_preview()
    {
        var vm = new MessageSettingsViewModel(Message(body: "{ not json"));

        Assert.Equal("", vm.Preview);
        vm.AddFieldCommand.Execute(null);
        vm.Fields[0].Path = "anything";
        vm.AddArrayCommand.Execute(null);
        vm.Arrays[0].Path = "anything";
        Assert.Null(vm.Error);
    }

    [Fact]
    public void Saving_writes_the_settings_file_and_closes()
    {
        var message = Message();
        var vm = new MessageSettingsViewModel(message);
        bool? closed = null;
        vm.CloseRequested += saved => closed = saved;
        vm.Enabled = true;
        vm.Group = "G";
        vm.ScheduleTypeIndex = (int)ScheduleType.Interval;
        vm.UnitIndex = -1;

        vm.SaveCommand.Execute(null);

        Assert.True(closed);
        Assert.Contains("\"G\"", File.ReadAllText(MessageSettings.PathFor(message.FilePath)));
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_reported()
    {
        var message = Message();
        Directory.CreateDirectory(MessageSettings.PathFor(message.FilePath));
        var vm = new MessageSettingsViewModel(message);
        bool? closed = null;
        vm.CloseRequested += saved => closed = saved;

        vm.SaveCommand.Execute(null);

        Assert.Null(closed);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public void Cancelling_closes_without_saving()
    {
        var message = Message();
        var vm = new MessageSettingsViewModel(message);
        vm.CancelCommand.Execute(null);
        bool? closed = null;
        vm.CloseRequested += saved => closed = saved;

        vm.CancelCommand.Execute(null);

        Assert.False(closed);
        Assert.False(File.Exists(MessageSettings.PathFor(message.FilePath)));
    }
}
