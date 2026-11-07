using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

public class RecordingTests
{
    private static RecordedMessage Message(string payload, double at = 0, string topic = "plant/line1")
        => new(TimeSpan.FromSeconds(at), topic, Encoding.UTF8.GetBytes(payload), 0, false);

    private static string Text(byte[] payload) => Encoding.UTF8.GetString(payload);

    [Fact]
    public void Saves_and_reads_back_text_binary_and_gzipped_payloads_and_the_fields()
    {
        using var folder = new TempFolder();
        var gzipped = Gzip.Compress(Encoding.UTF8.GetBytes("{\"a\":1}"));
        var recording = new Recording
        {
            Id = "one",
            Name = "Morning run",
            Created = new DateTime(2026, 11, 7, 9, 30, 0),
            Client = "Local",
            Filters = "#",
            Length = TimeSpan.FromSeconds(12.5),
            Messages =
            [
                Message("{\"ts\":1}", 0.25),
                new(TimeSpan.FromSeconds(1), "bin", [0xff, 0x00, 0xfe], 1, true),
                new(TimeSpan.FromSeconds(2), "zip", gzipped, 2, false),
            ],
            Fields = [new FieldRule("ts", FieldType.UnixSeconds)],
        };

        recording.Save(folder.Path);
        var read = Assert.Single(Recording.LoadAll(folder.Path));

        Assert.Equal("one", read.Id);
        Assert.Equal("Morning run", read.Name);
        Assert.Equal(recording.Created, read.Created);
        Assert.Equal("Local", read.Client);
        Assert.Equal(TimeSpan.FromSeconds(12.5), read.Length);
        Assert.Equal(TimeSpan.FromSeconds(0.25), read.Messages[0].At);
        Assert.Equal("{\"ts\":1}", Text(read.Messages[0].Payload));
        Assert.Equal(new byte[] { 0xff, 0x00, 0xfe }, read.Messages[1].Payload);
        Assert.Equal((1, true), (read.Messages[1].Qos, read.Messages[1].Retain));
        Assert.Equal(gzipped, read.Messages[2].Payload);
        Assert.Equal(FieldType.UnixSeconds, Assert.Single(read.Fields).Type);
        Assert.Equal(3, read.TopicCount);
    }

    [Fact]
    public void Each_message_is_a_file_of_its_own_in_the_recordings_folder()
    {
        using var folder = new TempFolder();
        new Recording { Id = "r", Name = "Run", Messages = [Message("{\"ts\":1}", 0.5, "a"), Message("two", 1, "b")] }.Save(folder.Path);

        var info = JsonNode.Parse(File.ReadAllText(folder.At("r", Recording.InfoFile)))!;
        var first = JsonNode.Parse(File.ReadAllText(folder.At("r", "000001.json")))!;
        var second = JsonNode.Parse(File.ReadAllText(folder.At("r", "000002.json")))!;

        Assert.Equal("Run", info["name"]!.GetValue<string>());
        Assert.Equal(2, info["messages"]!.GetValue<int>());
        Assert.Equal((0.5, "a", "{\"ts\":1}"), (first["at"]!.GetValue<double>(), first["topic"]!.GetValue<string>(), first["text"]!.GetValue<string>()));
        Assert.Equal((1.0, "b", "two"), (second["at"]!.GetValue<double>(), second["topic"]!.GetValue<string>(), second["text"]!.GetValue<string>()));
        Assert.Equal(3, Directory.GetFiles(folder.At("r")).Length);
    }

    [Fact]
    public void Saving_again_writes_only_the_info_file()
    {
        using var folder = new TempFolder();
        var recording = new Recording { Id = "r", Name = "Run", Messages = [Message("1")] };
        recording.Save(folder.Path);
        var written = File.GetLastWriteTimeUtc(folder.At("r", "000001.json"));
        File.SetLastWriteTimeUtc(folder.At("r", "000001.json"), written.AddDays(-1));

        recording.Name = "Renamed";
        recording.Save(folder.Path);

        Assert.Equal(written.AddDays(-1), File.GetLastWriteTimeUtc(folder.At("r", "000001.json")));
        Assert.Equal("Renamed", Assert.Single(Recording.LoadAll(folder.Path)).Name);
    }

    [Fact]
    public void A_single_file_recording_moves_to_a_folder_of_its_own()
    {
        using var folder = new TempFolder();
        folder.Write("old.json", """{ "name": "Old", "messages": [ { "at": 1, "topic": "b", "text": "2" }, { "at": 0, "topic": "a", "text": "1" } ] }""");

        var read = Assert.Single(Recording.LoadAll(folder.Path));

        Assert.Equal(("old", "Old"), (read.Id, read.Name));
        Assert.Equal(["a", "b"], read.Messages.Select(m => m.Topic));
        Assert.False(File.Exists(folder.At("old.json")));
        Assert.True(File.Exists(folder.At("old", "000002.json")));
    }

    [Fact]
    public void A_file_or_folder_that_is_not_a_recording_is_left_out()
    {
        using var folder = new TempFolder();
        folder.Write("broken.json", "not json");
        folder.Write(Path.Combine("bad", Recording.InfoFile), "{}");
        folder.Write(Path.Combine("bad", "000001.json"), "not json");
        Directory.CreateDirectory(folder.At("empty"));
        new Recording { Id = "good", Messages = [Message("1")] }.Save(folder.Path);

        Assert.Equal(["good"], Recording.LoadAll(folder.Path).Select(r => r.Id));
    }

    [Fact]
    public void Delete_removes_its_folder()
    {
        using var folder = new TempFolder();
        var recording = new Recording { Id = "r", Messages = [Message("1")] };
        recording.Save(folder.Path);

        recording.Delete(folder.Path);

        Assert.False(Directory.Exists(folder.At("r")));
    }

    [Fact]
    public void A_new_id_does_not_reuse_one_in_the_folder()
    {
        using var folder = new TempFolder();
        var time = new DateTime(2026, 11, 7, 9, 30, 15);
        var first = Recording.NewId(folder.Path, time);
        new Recording { Id = first }.Save(folder.Path);

        Assert.Equal("2026-11-07-093015", first);
        Assert.Equal("2026-11-07-093015-2", Recording.NewId(folder.Path, time));
    }

    [Fact]
    public void Varying_changes_the_fields_a_message_has()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var rules = new[] { new FieldRule("ts", FieldType.UnixSeconds), new FieldRule("n", FieldType.Counter) };
        var counters = new Dictionary<string, long>();

        var first = Recording.Vary(Message("{\"ts\":1,\"n\":0,\"v\":7}"), rules, counters, now);
        var second = Recording.Vary(Message("{\"ts\":1,\"n\":0,\"v\":7}"), rules, counters, now);

        Assert.Equal("{\"ts\":1800000000,\"n\":1,\"v\":7}", Text(first));
        Assert.Equal("{\"ts\":1800000000,\"n\":2,\"v\":7}", Text(second));
    }

    [Theory]
    [InlineData("{ \"other\": 1 }")]
    [InlineData("not json")]
    [InlineData("42")]
    public void A_message_without_the_fields_is_sent_exactly_as_recorded(string payload)
    {
        var message = Message(payload);

        var sent = Recording.Vary(message, [new FieldRule("ts", FieldType.UnixSeconds)], new Dictionary<string, long>(), DateTimeOffset.Now);

        Assert.Same(message.Payload, sent);
    }

    [Fact]
    public void A_gzipped_payload_is_changed_and_zipped_again()
    {
        var message = new RecordedMessage(TimeSpan.Zero, "t", Gzip.Compress(Encoding.UTF8.GetBytes("{\"ts\":1}")), 0, false);

        var sent = Recording.Vary(message, [new FieldRule("ts", FieldType.Fixed) { Value = 5 }], new Dictionary<string, long>(), DateTimeOffset.Now);

        Assert.True(Gzip.LooksZipped(sent));
        Assert.Equal("{\"ts\":5}", Text(Gzip.TryDecompress(sent)!));
    }

    [Fact]
    public void The_paths_offered_come_from_every_topic()
    {
        var recording = new Recording
        {
            Messages = [Message("{\"a\":1}", topic: "x"), Message("{\"b\":{\"c\":2}}", topic: "y"), Message("text", topic: "z")],
        };

        Assert.Equal(["a", "b.c"], recording.BodyPaths());
    }
}
