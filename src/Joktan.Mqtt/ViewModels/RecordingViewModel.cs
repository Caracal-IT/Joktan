using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Joktan.Core.ViewModels;
using Joktan.Mqtt.Resources;

namespace Joktan.Mqtt.ViewModels;

/// <summary>
/// One recording on the recorder page: its messages, the fields changed when it plays, and playing it
/// through a client at a speed that can be changed while it plays.
/// </summary>
public partial class RecordingViewModel : ViewModelBase
{
    private const int DefaultSpeed = 3;

    private readonly string? _folder;
    private RecordingPlayer? _player;
    private CancellationTokenSource? _stop;
    private IReadOnlyList<RecordedMessageRow>? _rows;
    private bool _changed;

    /// <param name="clients">The clients it can be played through.</param>
    /// <param name="folder">Where it is saved; the recordings folder of the app otherwise.</param>
    public RecordingViewModel(Recording recording, ObservableCollection<MqttClientViewModel> clients, string? folder = null)
    {
        Recording = recording;
        Clients = clients;
        _folder = folder;
        Name = recording.Name;
        PlayClient = clients.FirstOrDefault(c => c.Name == recording.Client) ?? clients.FirstOrDefault();
        foreach (var rule in recording.Fields)
            Fields.Add(Tracked(new FieldRuleViewModel(rule)));
        UpdateProgress();
    }

    public Recording Recording { get; }

    public ObservableCollection<MqttClientViewModel> Clients { get; }

    [ObservableProperty]
    public partial string Name { get; set; }

    public string Summary => string.Join(" · ",
        Duration(Recording.Length),
        Recording.Messages.Count == 1 ? Strings.Recorder_1Message : string.Format(Strings.Recorder_Messages, Recording.Messages.Count),
        Recording.TopicCount == 1 ? Strings.Recorder_1Topic : string.Format(Strings.Recorder_Topics, Recording.TopicCount));

    public string Details => string.Format(Strings.Recorder_RecordedOnThrough, Recording.Created, Recording.Client, Recording.Filters);

    /// <summary>The client it is played through.</summary>
    [ObservableProperty]
    public partial MqttClientViewModel? PlayClient { get; set; }

    /// <summary>Fields changed in every message that has them, as on folder messages.</summary>
    public ObservableCollection<FieldRuleViewModel> Fields { get; } = [];

    /// <summary>The paths of the fields in the recorded messages, offered when a field is named.</summary>
    public IReadOnlyList<string> BodyPaths => field ??= Recording.BodyPaths();

    /// <summary>The messages, in the order they are played.</summary>
    public IReadOnlyList<RecordedMessageRow> Rows => _rows ??= Recording.Messages.Select(m => new RecordedMessageRow(m)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpeedText))]
    [NotifyCanExecuteChangedFor(nameof(SlowerCommand), nameof(FasterCommand))]
    public partial int SpeedIndex { get; set; } = DefaultSpeed;

    public double Speed => RecordingPlayer.Speeds[SpeedIndex];

    public string SpeedText => string.Format(CultureInfo.CurrentCulture, Strings.Recorder_Speed, Speed);

    [ObservableProperty]
    public partial bool Loop { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsPlaying { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayText))]
    public partial bool IsPaused { get; private set; }

    /// <summary>"Play", "Pause" while playing, and "Resume" while paused.</summary>
    public string PlayText => !IsPlaying ? Strings.Recorder_Play : IsPaused ? Strings.Recorder_Resume : Strings.Recorder_Pause;

    /// <summary>How far it has played, from 0 to 1.</summary>
    [ObservableProperty]
    public partial double Progress { get; private set; }

    [ObservableProperty]
    public partial string ProgressText { get; private set; } = "";

    [ObservableProperty]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    public partial bool IsConfirmingDelete { get; set; }

    /// <summary>Removes it from the page, once its file is deleted. Set by the page.</summary>
    public Action<RecordingViewModel>? Deleted { get; set; }

    private bool CanSlower => SpeedIndex > 0;

    private bool CanFaster => SpeedIndex < RecordingPlayer.Speeds.Count - 1;

    [RelayCommand(CanExecute = nameof(CanSlower))]
    private void Slower() => SpeedIndex = Math.Max(SpeedIndex - 1, 0);

    [RelayCommand(CanExecute = nameof(CanFaster))]
    private void Faster() => SpeedIndex = Math.Min(SpeedIndex + 1, RecordingPlayer.Speeds.Count - 1);

    [RelayCommand]
    private void ResetSpeed() => SpeedIndex = DefaultSpeed;

    partial void OnSpeedIndexChanged(int value)
    {
        if (_player is not null)
            _player.Speed = Speed;
    }

    partial void OnLoopChanged(bool value)
    {
        if (_player is not null)
            _player.Loop = value;
    }

    partial void OnNameChanged(string value)
    {
        if (value.Trim().Length > 0 && value.Trim() != Recording.Name)
        {
            Recording.Name = value.Trim();
            _changed = true;
        }
    }

    /// <summary>Plays from the start, pauses, or carries on after a pause.</summary>
    [RelayCommand]
    private async Task PlayPauseAsync()
    {
        if (_player is { } playing)
        {
            playing.IsPaused = IsPaused = !IsPaused;
            return;
        }

        Error = null;
        if (PlayClient is not { } client)
        {
            Error = Strings.Recorder_ChooseAClientToPlay;
            return;
        }

        IReadOnlyList<FieldRule> rules;
        try
        {
            rules = Fields.Select(f => f.ToRule()).ToList();
        }
        catch (FormatException e)
        {
            Error = e.Message;
            return;
        }

        Recording.Fields = rules;
        _changed = true;
        Save();

        if (!client.IsConnected)
        {
            await client.ConnectCommand.ExecuteAsync(null);
            if (!client.IsConnected)
            {
                Error = string.Format(Strings.Recorder_CouldNotConnect, client.Name, client.Connection.FailureReason ?? client.StateText);
                return;
            }
        }

        var counters = new Dictionary<string, long>();
        var player = _player = new RecordingPlayer(Recording, Send) { Speed = Speed, Loop = Loop };
        var stop = _stop = new CancellationTokenSource();
        IsPaused = false;
        IsPlaying = true;
        try
        {
            await Task.Run(() => player.RunAsync(stop.Token));
        }
        finally
        {
            UpdateProgress();
            _player = null;
            _stop = null;
            IsPaused = false;
            IsPlaying = false;
            stop.Dispose();
        }

        async Task<bool> Send(RecordedMessage message)
        {
            var payload = Recording.Vary(message, rules, counters, DateTimeOffset.Now);
            var (ok, _) = await client.Connection.PublishAsync(message.Topic, payload, message.Qos, message.Retain);
            if (ok)
                MqttMetrics.RecordSent(manual: false);
            else
                MqttMetrics.RecordFailed();
            return ok;
        }
    }

    [RelayCommand(CanExecute = nameof(IsPlaying))]
    private void Stop() => _stop?.Cancel();

    [RelayCommand]
    private void AddField()
    {
        Fields.Add(Tracked(new FieldRuleViewModel()));
        _changed = true;
    }

    // A change to a field is saved with the rest.
    private FieldRuleViewModel Tracked(FieldRuleViewModel field)
    {
        field.PropertyChanged += (_, _) => _changed = true;
        return field;
    }

    [RelayCommand]
    private void RemoveField(FieldRuleViewModel field)
    {
        Fields.Remove(field);
        _changed = true;
    }

    [RelayCommand]
    private void Delete()
    {
        if (!IsConfirmingDelete)
        {
            IsConfirmingDelete = true;
            return;
        }

        Stop();
        Recording.Delete(_folder);
        Deleted?.Invoke(this);
    }

    [RelayCommand]
    private void CancelDelete() => IsConfirmingDelete = false;

    /// <summary>Shows how far it has played. Called by the page while it plays.</summary>
    public void UpdateProgress()
    {
        var length = Recording.Length;
        var player = _player;
        var position = player?.Position ?? TimeSpan.Zero;
        if (position > length)
            position = length;

        Progress = length > TimeSpan.Zero ? position / length : player is null ? 0 : 1;
        var total = Recording.Messages.Count;
        var text = string.Format(Strings.Recorder_PositionOf, Duration(position), Duration(length));
        if (player is not null)
        {
            text += " · " + (player.Loop || player.Sent > total
                ? string.Format(Strings.Recorder_SentInAll, player.Sent)
                : string.Format(Strings.Recorder_SentOf, player.Done, total));
            if (player.Failed > 0)
                text += " · " + string.Format(Strings.Recorder_Failed, player.Failed);
        }

        ProgressText = text;
    }

    /// <summary>Writes the name and fields to its file, if they have changed. Fields that are not valid are left as they were.</summary>
    public void Save()
    {
        if (!_changed)
            return;

        try
        {
            Recording.Fields = Fields.Select(f => f.ToRule()).ToList();
        }
        catch (FormatException)
        {
            // Saved once they are put right, or when played.
        }

        Recording.Save(_folder);
        _changed = false;
    }

    /// <summary>Stops playing, when the page closes.</summary>
    public void Shutdown()
    {
        _stop?.Cancel();
        Save();
    }

    /// <summary>A time as minutes and seconds, with tenths.</summary>
    public static string Duration(TimeSpan time)
        => time.TotalHours >= 1
            ? time.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture)
            : time.ToString(@"m\:ss\.f", CultureInfo.InvariantCulture);
}

/// <summary>One message in a recording's list.</summary>
public sealed class RecordedMessageRow(RecordedMessage message)
{
    private readonly ReceivedMessage _shown = new(message.Topic, message.Payload, message.Qos, message.Retain, DateTime.MinValue);

    public string AtText => "+" + RecordingViewModel.Duration(message.At);

    public string Topic => message.Topic;

    public string Preview => _shown.Preview;
}
