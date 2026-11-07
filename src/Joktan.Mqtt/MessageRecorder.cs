using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Joktan.Mqtt;

/// <summary>
/// Records what arrives on a connection's topic filters, with the time of each message, until stopped.
/// </summary>
public sealed class MessageRecorder
{
    /// <summary>The most messages one recording holds; past this, the rest are dropped.</summary>
    public const int MaxMessages = 100_000;

    private const string SubscriptionOwner = "recorder";

    private readonly MqttConnection _connection;
    private readonly List<RecordedMessage> _messages = [];
    private readonly Stopwatch _clock = new();
    private string[] _filters = [];
    private DateTime _started;

    public MessageRecorder(MqttConnection connection)
    {
        _connection = connection;
    }

    public bool IsRecording { get; private set; }

    public int Count
    {
        get
        {
            lock (_messages)
                return _messages.Count;
        }
    }

    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>True once <see cref="MaxMessages"/> have been recorded.</summary>
    public bool IsFull => Count >= MaxMessages;

    /// <summary>Subscribes to the filters and starts recording. Returns why a filter was refused, or null.</summary>
    public async Task<string?> StartAsync(IReadOnlyList<string> filters)
    {
        if (filters.Select(TopicRules.ValidateFilter).FirstOrDefault(e => e is not null) is { } invalid)
            return invalid;

        lock (_messages)
            _messages.Clear();
        _filters = filters.ToArray();
        _started = DateTime.Now;
        _clock.Restart();
        IsRecording = true;

        // Before subscribing, as retained values arrive as soon as the broker has the subscription.
        _connection.MessageReceived += OnMessageReceived;
        var error = await _connection.SetSubscriptionsAsync(SubscriptionOwner, _filters);
        if (error is not null)
            await StopListeningAsync();
        return error;
    }

    /// <summary>Stops recording and returns what was recorded.</summary>
    public async Task<Recording> StopAsync(string id, string name, string client)
    {
        var length = _clock.Elapsed;
        await StopListeningAsync();

        lock (_messages)
        {
            return new Recording
            {
                Id = id,
                Name = name,
                Created = _started,
                Client = client,
                Filters = string.Join(", ", _filters),
                Length = length,
                Messages = _messages.ToList(),
            };
        }
    }

    private async Task StopListeningAsync()
    {
        IsRecording = false;
        _clock.Stop();
        _connection.MessageReceived -= OnMessageReceived;
        await _connection.SetSubscriptionsAsync(SubscriptionOwner, []);
    }

    // Arrives on the MQTT client's thread. The connection also delivers what other pages subscribe to.
    private void OnMessageReceived(ReceivedMessage message)
    {
        if (!IsRecording || !_filters.Any(f => TopicRules.Matches(f, message.Topic)))
            return;

        lock (_messages)
        {
            if (_messages.Count < MaxMessages)
                _messages.Add(new RecordedMessage(_clock.Elapsed, message.Topic, message.RawPayload, message.Qos, message.Retain));
        }
    }
}
