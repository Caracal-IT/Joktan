using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Mqtt.ViewModels;
using Joktan.Mqtt.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class RecorderViewModelTests : DataFolderTest
{
    private static MqttViewModel Mqtt(int port) => new(null, new MqttSettings
    {
        Clients = [new ClientProfile { Name = "local", Host = "127.0.0.1", Port = port.ToString(), ClientId = "rec-" + Guid.NewGuid().ToString("N")[..8] }],
    }, new FakeCredentialStore());

    [Fact]
    public Task Records_then_plays_back_with_a_field_changed() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var page = new RecorderViewModel(mqtt, Data.At("recordings"));
        Assert.False(page.HasRecordings);

        // Record connects the client by itself.
        page.Filters = "plant/#";
        await page.ToggleRecordCommand.ExecuteAsync(null);
        Assert.True(page.IsRecording);
        Assert.True(mqtt.Clients[0].IsConnected);

        await broker.SendAsync("plant/a", """{"ts":1,"v":10}""");
        await broker.SendAsync("plant/b", "plain text");
        await Headless.Until(() => page.RecordStatus.Contains("2 messages"));
        await page.ToggleRecordCommand.ExecuteAsync(null);

        var recording = Assert.Single(page.Recordings);
        Assert.Same(recording, page.SelectedRecording);
        Assert.Equal(["ts", "v"], recording.BodyPaths);
        Assert.Single(Recording.LoadAll(Data.At("recordings")));

        recording.AddFieldCommand.Execute(null);
        recording.Fields[0].Path = "ts";
        recording.Fields[0].TypeIndex = (int)FieldType.Fixed;
        recording.Fields[0].SetFixed(JsonValue.Create(99));
        recording.SpeedIndex = RecordingPlayer.Speeds.Count - 1;
        broker.Published.Clear();

        await recording.PlayPauseCommand.ExecuteAsync(null);

        // The play ends when the client has sent the messages; the broker may take them a little after.
        await Headless.Until(() => broker.Published.Count(p => p.Topic.StartsWith("plant/")) == 2);
        var sent = broker.Published.Where(p => p.Topic.StartsWith("plant/")).Select(p => (p.Topic, Encoding.UTF8.GetString(p.Payload))).ToList();
        Assert.Equal([("plant/a", """{"ts":99,"v":10}"""), ("plant/b", "plain text")], sent);
        Assert.False(recording.IsPlaying);
        Assert.Equal(1, recording.Progress);

        // The field is kept with the recording.
        Assert.Equal(FieldType.Fixed, Assert.Single(Assert.Single(Recording.LoadAll(Data.At("recordings"))).Fields).Type);
        await mqtt.Clients[0].Connection.DisconnectAsync();
    });

    [Fact]
    public Task The_speed_goes_up_and_down_within_its_steps() => Headless.Run(() =>
    {
        var recording = new RecordingViewModel(new Recording { Id = "r" }, Mqtt(1).Clients, Data.At("recordings"));
        Assert.Equal("1×", recording.SpeedText);

        recording.FasterCommand.Execute(null);
        Assert.Equal("2×", recording.SpeedText);
        for (var i = 0; i < 20; i++)
            recording.SlowerCommand.Execute(null);
        Assert.Equal(0.1, recording.Speed);
        Assert.False(recording.SlowerCommand.CanExecute(null));

        recording.ResetSpeedCommand.Execute(null);
        Assert.Equal(1, recording.Speed);
    });

    [Fact]
    public Task Play_can_be_paused_and_resumed_while_it_plays() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        new Recording
        {
            Id = "r",
            Name = "Run",
            Messages = [new(TimeSpan.Zero, "plant/a", [1], 0, false), new(TimeSpan.FromSeconds(1), "plant/b", [2], 0, false)],
        }.Save(Data.At("recordings"));
        var page = new RecorderViewModel(Mqtt(broker.Port), Data.At("recordings"));
        var recording = page.SelectedRecording!;

        var playing = recording.PlayPauseCommand.ExecuteAsync(null);
        await Headless.Until(() => broker.Published.Any(p => p.Topic == "plant/a"));

        // The button pauses while it plays, rather than waiting for the play to end.
        Assert.True(recording.PlayPauseCommand.CanExecute(null));
        await recording.PlayPauseCommand.ExecuteAsync(null);
        Assert.True(recording.IsPaused);
        Assert.Equal("Resume", recording.PlayText);
        await Task.Delay(1500);
        Assert.DoesNotContain(broker.Published, p => p.Topic == "plant/b");

        await recording.PlayPauseCommand.ExecuteAsync(null);
        Assert.False(recording.IsPaused);
        await playing;
        await Headless.Until(() => broker.Published.Any(p => p.Topic == "plant/b"));
        Assert.False(recording.IsPlaying);
    });

    [Fact]
    public Task Delete_asks_once_more_before_removing_the_file() => Headless.Run(() =>
    {
        new Recording { Id = "r", Name = "Run", Messages = [new(TimeSpan.Zero, "t", [], 0, false)] }.Save(Data.At("recordings"));
        var page = new RecorderViewModel(Mqtt(1), Data.At("recordings"));
        var recording = page.SelectedRecording!;

        recording.DeleteCommand.Execute(null);
        Assert.True(recording.IsConfirmingDelete);
        Assert.Single(page.Recordings);

        recording.DeleteCommand.Execute(null);
        Assert.Empty(page.Recordings);
        Assert.Null(page.SelectedRecording);
        Assert.Empty(Recording.LoadAll(Data.At("recordings")));
    });

    private static void Saved(string folder, string id, string name, DateTime created)
        => new Recording { Id = id, Name = name, Created = created, Messages = [new(TimeSpan.Zero, "t", [1], 0, false)] }.Save(folder);

    [Fact]
    public Task Recordings_move_to_a_folder_of_their_own_and_are_kept_there() => Headless.Run(async () =>
    {
        Saved(Data.At("recordings"), "r", "Run", new DateTime(2026, 1, 1));
        var page = new RecorderViewModel(Mqtt(1), Data.At("recordings"));

        page.ToggleFolderCommand.Execute(null);
        Assert.True(page.IsChoosingFolder);
        Assert.Equal(Data.At("recordings"), page.FolderText);

        // An empty folder gets a copy of the recordings, and the originals stay.
        page.FolderText = Data.At("elsewhere");
        page.ApplyFolderCommand.Execute(null);
        Assert.False(page.IsChoosingFolder);
        Assert.Equal(Data.At("elsewhere"), page.Folder);
        Assert.Contains("copied", page.Message);
        Assert.Single(Recording.LoadAll(Data.At("elsewhere")));
        Assert.Single(Recording.LoadAll(Data.At("recordings")));
        Assert.Equal(Data.At("elsewhere"), Recording.ChosenFolder);

        // What is recorded or changed now goes there.
        page.SelectedRecording!.Name = "Renamed";
        page.SelectedRecording.Save();
        Assert.Equal("Renamed", Assert.Single(Recording.LoadAll(Data.At("elsewhere"))).Name);
        Assert.Equal("Run", Assert.Single(Recording.LoadAll(Data.At("recordings"))).Name);

        // One that holds recordings already is opened as it is.
        Saved(Data.At("other"), "o", "Other", new DateTime(2026, 2, 1));
        page.FolderText = Data.At("other");
        page.ApplyFolderCommand.Execute(null);
        Assert.Equal(["Other"], page.Recordings.Select(r => r.Name));
        Assert.Contains("Opened", page.Message);

        // The page made next uses the folder chosen.
        Assert.Equal(Data.At("other"), new RecorderViewModel(Mqtt(1)).Folder);

        page.FolderText = "relative/path";
        page.ApplyFolderCommand.Execute(null);
        Assert.NotNull(page.FolderMessage);

        page.PickFolder = () => Task.FromResult<string?>(Data.At("picked"));
        await page.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal(Data.At("picked"), page.FolderText);
        page.UseDefaultFolderCommand.Execute(null);
        Assert.Equal(page.DefaultFolder, page.FolderText);
        page.FolderText = "";
        page.ApplyFolderCommand.Execute(null);
        Assert.Equal(Path.GetFullPath(Recording.DefaultFolder), page.Folder);
        Assert.Equal(Recording.DefaultFolder, Recording.ChosenFolder);
    });

    [Fact]
    public Task An_old_recording_opened_from_anywhere_is_kept_with_the_others_and_ready_to_play() => Headless.Run(async () =>
    {
        Saved(Data.At("recordings"), "new", "Newer", new DateTime(2026, 3, 1));
        Saved(Data.At("recordings"), "old", "Oldest", new DateTime(2025, 1, 1));
        Saved(Data.At("usb"), "2026-02-01-120000", "From another computer", new DateTime(2026, 2, 1));
        var page = new RecorderViewModel(Mqtt(1), Data.At("recordings"));

        page.PickRecording = () => Task.FromResult<string?>(Data.At("usb", "2026-02-01-120000", Recording.InfoFile));
        await page.OpenRecordingCommand.ExecuteAsync(null);

        Assert.Equal(["Newer", "From another computer", "Oldest"], page.Recordings.Select(r => r.Name));
        Assert.Equal("From another computer", page.SelectedRecording!.Name);
        Assert.NotNull(page.SelectedRecording.PlayClient);
        Assert.Equal(3, Recording.LoadAll(Data.At("recordings")).Count);

        // Opening it again does not copy it twice, nor does opening one already in the folder.
        page.SelectedRecording = page.Recordings[0];
        page.OpenRecording(Data.At("usb", "2026-02-01-120000", Recording.InfoFile));
        Assert.Equal("From another computer", page.SelectedRecording!.Name);
        page.OpenRecording(Data.At("recordings", "old", Recording.InfoFile));
        Assert.Equal("Oldest", page.SelectedRecording!.Name);
        Assert.Equal(3, Recording.LoadAll(Data.At("recordings")).Count);
        Assert.Equal(3, page.Recordings.Count);


        // So can a recording kept as a single file by an older version.
        page.OpenRecording(Data.Write("single.json", """{ "name": "Single file", "created": "2024-06-01T08:00:00", "messages": [ { "at": 0, "topic": "t", "text": "1" } ] }"""));
        Assert.Equal("Single file", page.SelectedRecording!.Name);
        Assert.True(Recording.IsRecordingFolder(Data.At("recordings", "2024-06-01-080000")));

        page.OpenRecording(Data.Write("not-a-recording.json", "[1, 2"));
        Assert.Contains("Could not open", page.Message);
    });

    [Fact]
    public Task The_page_shows_a_recording() => Headless.Run(() =>
    {
        new Recording { Id = "r", Name = "Run", Messages = [new(TimeSpan.Zero, "plant/a", Encoding.UTF8.GetBytes("{\"v\":1}"), 0, false)] }.Save(Data.At("recordings"));
        var page = new RecorderViewModel(Mqtt(1), Data.At("recordings"));

        var window = Headless.Show(new RecorderView(), page);
        Headless.Settle();

        Assert.Contains(Headless.All<TextBlock>(window), t => t.Text == "plant/a" && t.IsEffectivelyVisible);
        window.Close();
    });

    [Fact]
    public Task Moves_to_a_window_of_its_own_and_back() => Headless.Run(() =>
    {
        var page = new RecorderViewModel(Mqtt(1), Data.At("recordings"));
        var window = Headless.Show(new RecorderView(), page);
        var opened = new List<RecorderWindow>();
        using var subscription = Window.WindowOpenedEvent.AddClassHandler<RecorderWindow>((w, _) => opened.Add(w));

        page.PopOutCommand.Execute(null);
        Headless.Settle();
        var own = Assert.Single(opened);
        Assert.True(page.IsPoppedOut);
        Assert.Empty(Headless.All<RecorderPanel>(window));
        Assert.NotEmpty(Headless.All<RecorderPanel>(own));

        page.PopOutCommand.Execute(null);
        Assert.Single(opened);

        page.DockCommand.Execute(null);
        Headless.Settle();
        window.UpdateLayout();
        Assert.False(page.IsPoppedOut);
        Assert.Null(page.CloseWindow);
        Assert.False(own.IsVisible);
        Assert.NotEmpty(Headless.All<RecorderPanel>(window));
        window.Close();
    });
}
