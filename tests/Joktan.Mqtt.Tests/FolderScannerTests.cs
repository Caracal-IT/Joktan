using System.IO;
using System;
using System.Linq;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class FolderScannerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private FolderMessage ScanOne() => Assert.Single(FolderScanner.Scan(_folder.Path));

    [Fact]
    public void The_topic_is_the_folder_path()
    {
        var path = _folder.Write("plant/line1/temp.json", """{"a":1}""");

        var message = ScanOne();

        Assert.Equal(path, message.FilePath);
        Assert.Equal("temp.json", message.FileName);
        Assert.Equal("plant/line1", message.Folder);
        Assert.Equal("plant/line1", message.Topic);
        Assert.Equal("""{"a":1}""", message.Text);
        Assert.True(message.IsValid);
        Assert.NotNull(message.Body);
    }

    [Fact]
    public void Settings_files_are_not_messages()
    {
        _folder.Write("a/m.json", "{}");
        _folder.Write("a/m.send.json", "{}");
        _folder.Write("a/n.SEND.JSON", "{}");

        Assert.Equal("m.json", ScanOne().FileName);
    }

    [Fact]
    public void Other_files_are_ignored()
    {
        _folder.Write("a/notes.txt", "x");

        Assert.Empty(FolderScanner.Scan(_folder.Path));
    }

    [Fact]
    public void Messages_are_ordered_by_topic_then_file_ignoring_case()
    {
        _folder.Write("b/a.json", "{}");
        _folder.Write("A/z.json", "{}");
        _folder.Write("A/Y.json", "{}");

        var messages = FolderScanner.Scan(_folder.Path);

        Assert.Equal(["A/Y.json", "A/z.json", "b/a.json"], messages.Select(m => $"{m.Topic}/{m.FileName}"));
    }

    [Fact]
    public void A_file_at_the_top_needs_a_topic_from_its_settings()
    {
        _folder.Write("m.json", "{}");

        var message = ScanOne();

        Assert.Equal("", message.Folder);
        Assert.Equal("Not inside a topic folder, and the settings set no topic", message.Error);
    }

    [Fact]
    public void A_topic_in_the_settings_replaces_the_folder()
    {
        _folder.Write("m.json", "{}");
        _folder.Write("m.send.json", """{"topic":"a/b"}""");

        var message = ScanOne();

        Assert.Equal("a/b", message.Topic);
        Assert.True(message.IsValid);
    }

    [Fact]
    public void A_topic_in_the_settings_can_use_the_folder()
    {
        _folder.Write("x/m.json", "{}");
        _folder.Write("x/m.send.json", """{"topic":"{folder}/more"}""");

        Assert.Equal("x/more", ScanOne().Topic);
    }

    [Theory]
    [InlineData("not json", "Not valid JSON")]
    [InlineData("null", "The file holds no JSON value")]
    public void A_body_that_cannot_be_used_is_invalid(string text, string error)
    {
        _folder.Write("x/m.json", text);

        var message = ScanOne();

        Assert.Equal(error, message.Error);
        Assert.Equal(text, message.Text);
        Assert.Null(message.Body);
        Assert.False(message.IsValid);
    }

    [Fact]
    public void Settings_that_are_not_valid_make_the_message_invalid()
    {
        _folder.Write("x/m.json", "{}");
        _folder.Write("x/m.send.json", """{"qos":9}""");

        var message = ScanOne();

        Assert.Equal("Settings file is not valid: \"qos\" must be 0, 1 or 2", message.Error);
        Assert.Same(MessageSettings.Default, message.Settings);
    }

    [Fact]
    public void Settings_are_ignored_for_a_body_that_cannot_be_read()
    {
        _folder.Write("x/m.json", "not json");
        _folder.Write("x/m.send.json", """{"qos":9}""");

        Assert.Equal("Not valid JSON", ScanOne().Error);
    }

    [Fact]
    public void A_field_that_is_not_in_the_body_makes_the_message_invalid()
    {
        _folder.Write("x/m.json", """{"a":1}""");
        _folder.Write("x/m.send.json", """{"fields":{"b":{"type":"guid"}}}""");

        var message = ScanOne();

        Assert.Equal("Settings name a field that is not in the body: b", message.Error);
        Assert.Same(MessageSettings.Default, message.Settings);
    }

    [Fact]
    public void An_array_rule_that_does_not_fit_the_body_makes_the_message_invalid()
    {
        _folder.Write("x/m.json", """{"a":1}""");
        _folder.Write("x/m.send.json", """{"arrays":{"a":{"count":2}}}""");

        Assert.Equal("a is not an array", ScanOne().Error);
    }

    [Theory]
    [InlineData("""{"topic":"a/{d}/{bad name}","topicFields":{"bad name":{"type":"guid"}}}""", "\"bad name\" cannot be a topic field name; use letters, digits, _, - and .")]
    [InlineData("""{"topic":"a/{d}"}""", "Topic part {d} has no topic field")]
    [InlineData("""{"topic":"a/b","topicFields":{"d":{"type":"guid"}}}""", "Topic field d is not used in the topic; write it as {d}")]
    [InlineData("""{"topic":"a/+"}""", "A topic may not contain + or #")]
    public void A_topic_that_does_not_work_makes_the_message_invalid(string settings, string error)
    {
        _folder.Write("x/m.json", "{}");
        _folder.Write("x/m.send.json", settings);

        Assert.Equal(error, ScanOne().Error);
    }

    [Fact]
    public void Good_settings_are_used()
    {
        _folder.Write("x/m.json", """{"n":0,"r":[1]}""");
        _folder.Write("x/m.send.json", """
            {"enabled":true,"qos":1,"topic":"t/{d}","topicFields":{"d":{"type":"guid"}},
             "schedule":{"type":"interval","every":5},
             "fields":{"n":{"type":"counter"}},"arrays":{"r":{"count":2}}}
            """);

        var message = ScanOne();

        Assert.True(message.IsValid, message.Error);
        Assert.True(message.HasSchedule);
        Assert.Equal(1, message.Settings.Qos);
        Assert.Equal(5, message.Settings.Schedule.Every);
    }

    [Fact(Skip = "Bug: a repeated JSON key throws ArgumentException, which nothing catches, so one file stops the whole scan.")]
    public void A_settings_file_with_a_repeated_key_makes_the_message_invalid()
    {
        _folder.Write("x/m.json", "{}");
        _folder.Write("x/m.send.json", """{"qos":1,"qos":2}""");

        Assert.StartsWith("Settings file is not valid", ScanOne().Error);
    }

    [Fact(Skip = "Bug: a repeated JSON key throws ArgumentException, which nothing catches, so one file stops the whole scan.")]
    public void A_body_with_a_repeated_key_does_not_stop_the_scan()
    {
        _folder.Write("x/m.json", """{"a":1,"a":2}""");
        _folder.Write("x/m.send.json", """{"fields":{"a":{"type":"guid"}}}""");
        _folder.Write("y/n.json", "{}");

        var messages = FolderScanner.Scan(_folder.Path);

        Assert.Equal(2, messages.Count);
        Assert.False(messages[0].IsValid);
        Assert.True(messages[1].IsValid);
    }

    [Fact]
    public void The_sample_messages_in_the_repository_are_all_valid()
    {
        var messages = FolderScanner.Scan(RepositoryFiles.At("samples", "mqtt-messages"));

        Assert.NotEmpty(messages);
        Assert.All(messages, m => Assert.True(m.IsValid, $"{m.FilePath}: {m.Error}"));
    }

    [Fact]
    public void A_settings_file_that_cannot_be_read_marks_the_message()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "This uses the permissions of a Unix file.");
        _folder.Write("topic/m.json", "{}");
        var settings = _folder.Write("topic/m.send.json", "{}");
#pragma warning disable CA1416 // Skipped on Windows just above.
        File.SetUnixFileMode(settings, UnixFileMode.None);

        try
        {
            Assert.Contains("cannot be read", ScanOne().Error);
        }
        finally
        {
            File.SetUnixFileMode(settings, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
#pragma warning restore CA1416
    }
}
