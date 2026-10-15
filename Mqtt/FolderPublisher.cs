using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Joktan.Mqtt;

/// <summary>
/// Holds the messages of the chosen folder and sends them, by hand or on their schedules.
/// Schedules run between <see cref="Start"/> and <see cref="Stop"/>, which follow the broker
/// connection, and only for messages that are switched on. Not thread-safe: call it from one thread.
/// </summary>
public sealed class FolderPublisher
{
    private readonly MqttConnection _connection;
    private readonly Action<ActivityEntry> _log;
    private List<FolderMessage> _messages = [];

    public FolderPublisher(MqttConnection connection, Action<ActivityEntry> log)
    {
        _connection = connection;
        _log = log;
    }

    public IReadOnlyList<FolderMessage> Messages => _messages;

    /// <summary>Why the folder could not be read, or null.</summary>
    public string? FolderError { get; private set; }

    public bool IsRunning { get; private set; }

    /// <summary>Reads the folder again, keeping the progress of messages that are still there.</summary>
    public void Load(string? folder, DateTime now)
    {
        FolderError = null;
        List<FolderMessage> scanned = [];

        if (!string.IsNullOrEmpty(folder))
        {
            try
            {
                if (Directory.Exists(folder))
                    scanned = FolderScanner.Scan(folder);
                else
                    FolderError = "The folder does not exist";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                FolderError = $"The folder cannot be read: {e.Message}";
            }
        }

        var previous = _messages.ToDictionary(m => m.FilePath);
        foreach (var message in scanned)
        {
            message.State = message.RestingState;
            if (!previous.TryGetValue(message.FilePath, out var old))
            {
                if (IsRunning)
                    Schedule(message, now);
                continue;
            }

            message.CarryOver(old);
            if (!IsRunning)
                continue;

            var wasOn = old.State is FolderMessageState.Scheduled or FolderMessageState.Finished or FolderMessageState.Missed;
            if (message.RestingState == FolderMessageState.Waiting && wasOn && SameSchedule(message, old))
            {
                // An unchanged schedule keeps its place.
                message.State = old.State;
                message.NextDue = old.NextDue;
                message.ScheduledSends = old.ScheduledSends;
            }
            else
            {
                // Switched on just now, or its schedule changed: it starts again from now. A message
                // that is switched off keeps its count until it is switched on again.
                if (!wasOn && message.RestingState == FolderMessageState.Waiting)
                    message.ResetProgress();
                Schedule(message, now);
            }
        }

        _messages = scanned;
    }

    public void Start(DateTime now)
    {
        IsRunning = true;
        foreach (var message in _messages)
        {
            message.ResetProgress();
            Schedule(message, now);
        }
    }

    public void Stop()
    {
        IsRunning = false;
        foreach (var message in _messages)
        {
            message.NextDue = null;
            message.State = message.RestingState;
        }
    }

    /// <summary>Sends every message that has fallen due. Returns whether anything changed.</summary>
    public async Task<bool> TickAsync(DateTime now)
    {
        if (!IsRunning)
            return false;

        var changed = false;
        foreach (var message in _messages.ToArray())
        {
            if (message.State != FolderMessageState.Scheduled || message.NextDue is not { } due || due > now)
                continue;

            changed = true;
            var sent = false;
            if (_connection.IsConnected)
            {
                await PublishAsync(message, now);
                message.ScheduledSends++;
                sent = true;
            }
            else
            {
                MqttMetrics.RecordSkipped();
                _log(new ActivityEntry(now, "Skipped", message.Topic, message.FileName, "No connection"));
            }

            Advance(message, due, now, sent);
        }

        return changed;
    }

    public Task SendNowAsync(FolderMessage message, DateTime now)
        => message.IsValid ? PublishAsync(message, now) : Task.CompletedTask;

    private static void Schedule(FolderMessage message, DateTime now)
    {
        message.ScheduledSends = 0;
        message.NextDue = null;
        message.State = message.RestingState;

        var schedule = message.Settings.Schedule;
        if (message.State != FolderMessageState.Waiting)
            return;

        if (schedule.FirstDue(now) is { } first)
        {
            message.NextDue = first;
            message.State = FolderMessageState.Scheduled;
        }
        else
        {
            message.State = schedule.IsRecurring ? FolderMessageState.Finished : FolderMessageState.Missed;
        }
    }

    private static void Advance(FolderMessage message, DateTime due, DateTime now, bool sent)
    {
        var schedule = message.Settings.Schedule;
        var next = message.ScheduledSends >= schedule.MaxSends ? null : schedule.NextDue(due, now);

        message.NextDue = next;
        if (next is not null)
            message.State = FolderMessageState.Scheduled;
        else if (schedule.IsRecurring || sent)
            message.State = FolderMessageState.Finished;
        else
            message.State = FolderMessageState.Missed;
    }

    private async Task PublishAsync(FolderMessage message, DateTime now)
    {
        var settings = message.Settings;
        var stamp = DateTimeOffset.Now;

        // A message with no varying fields is sent exactly as the file is written.
        var body = settings.Fields.Count == 0
            ? message.Text
            : VaryingFields.Apply(message.Body!, settings.Fields, message.Counters, stamp)
                .ToJsonString(JsonFormats.Compact);
        var payload = Encoding.UTF8.GetBytes(body);

        // A topic field can produce a value that is not allowed in a topic, such as "+".
        var topic = TopicTemplate.Resolve(message.Topic, settings.TopicFields, message.Counters, stamp);
        var (ok, error) = TopicRules.Validate(topic) is { } topicError
            ? (false, $"Topic {topic}: {topicError}")
            : await _connection.PublishAsync(topic, payload, settings.Qos, settings.Retain);
        if (ok)
        {
            message.LastSent = now;
            message.SentCount++;
            MqttMetrics.RecordSent(manual: false);
            _log(new ActivityEntry(now, "Sent", topic, message.FileName, $"{payload.Length:N0} bytes", body));
        }
        else
        {
            MqttMetrics.RecordFailed();
            _log(new ActivityEntry(now, "Failed", topic, message.FileName, error ?? "Unknown error"));
        }
    }

    private static bool SameSchedule(FolderMessage a, FolderMessage b)
        => MessageSettings.ScheduleToJson(a.Settings.Schedule).ToJsonString()
           == MessageSettings.ScheduleToJson(b.Settings.Schedule).ToJsonString();
}
