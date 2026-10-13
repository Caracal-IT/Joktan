using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;

namespace Joktan.Mqtt;

public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Reconnecting,
    Failed,
}

public sealed record BrokerEndpoint(string Host, int Port, string ClientId, string? Username, string? Password);

/// <summary>
/// A connection to one MQTT broker. A connection that drops is retried until it comes back or
/// <see cref="DisconnectAsync"/> is called.
/// </summary>
public sealed class MqttConnection
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
    private MqttClientOptions? _options;
    private CancellationTokenSource? _session;
    private string[] _filters = [];

    public MqttConnection()
    {
        _client.DisconnectedAsync += OnDisconnectedAsync;
        _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
    }

    public ConnectionState State { get; private set; }

    /// <summary>Why the last attempt failed; set while <see cref="State"/> is Failed.</summary>
    public string? FailureReason { get; private set; }

    public bool IsConnected => State == ConnectionState.Connected;

    /// <summary>Raised on an arbitrary thread whenever <see cref="State"/> changes.</summary>
    public event Action? StateChanged;

    /// <summary>Raised on an arbitrary thread for every message that matches a subscription.</summary>
    public event Action<ReceivedMessage>? MessageReceived;

    public async Task ConnectAsync(BrokerEndpoint endpoint)
    {
        await DisconnectAsync();

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(endpoint.Host, endpoint.Port)
            .WithClientId(endpoint.ClientId)
            .WithTimeout(ConnectTimeout);
        if (!string.IsNullOrEmpty(endpoint.Username))
            builder = builder.WithCredentials(endpoint.Username, endpoint.Password ?? "");
        _options = builder.Build();

        var session = _session = new CancellationTokenSource();
        Set(ConnectionState.Connecting);

        var error = await TryConnectAsync(session.Token);
        if (session.IsCancellationRequested)
            return;

        if (error is null)
        {
            await SubscribeAsync(_filters);
            Set(ConnectionState.Connected);
        }
        else
        {
            _session = null;
            Set(ConnectionState.Failed, error);
        }
    }

    public async Task DisconnectAsync()
    {
        var session = Interlocked.Exchange(ref _session, null);
        if (session is null && State is ConnectionState.Disconnected or ConnectionState.Failed)
        {
            Set(ConnectionState.Disconnected);
            return;
        }

        session?.Cancel();
        try
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
        }
        catch (Exception)
        {
            // The connection is being dropped either way.
        }

        Set(ConnectionState.Disconnected);
    }

    /// <summary>
    /// Replaces the topic filters this connection listens to. They are applied now if connected,
    /// and again after every connect. Returns why a filter was refused, or null.
    /// </summary>
    public async Task<string?> SetSubscriptionsAsync(IEnumerable<string> filters)
    {
        var previous = _filters;
        _filters = filters.Distinct().ToArray();
        if (!IsConnected)
            return null;

        try
        {
            foreach (var removed in previous.Except(_filters))
                await _client.UnsubscribeAsync(removed);
        }
        catch (Exception e)
        {
            return e.Message;
        }

        return await SubscribeAsync(_filters.Except(previous));
    }

    public async Task<(bool Ok, string? Error)> PublishAsync(string topic, byte[] payload, int qos, bool retain)
    {
        if (!IsConnected)
            return (false, "Not connected");

        try
        {
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)qos)
                .WithRetainFlag(retain)
                .Build();

            var result = await _client.PublishAsync(message);
            return result.IsSuccess
                ? (true, null)
                : (false, string.IsNullOrEmpty(result.ReasonString) ? result.ReasonCode.ToString() : result.ReasonString);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }

    private async Task<string?> TryConnectAsync(CancellationToken session)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(session);
        timeout.CancelAfter(ConnectTimeout);

        try
        {
            var result = await _client.ConnectAsync(_options!, timeout.Token);
            return result.ResultCode == MqttClientConnectResultCode.Success ? null : Describe(result.ResultCode);
        }
        catch (OperationCanceledException)
        {
            return "No answer from the broker within 10 seconds";
        }
        catch (Exception e)
        {
            return $"Could not connect: {(e.InnerException ?? e).Message}";
        }
    }

    private Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
    {
        // Failed attempts raise this too; only a drop of an established connection is retried.
        var session = _session;
        if (session is null || session.IsCancellationRequested || State != ConnectionState.Connected)
            return Task.CompletedTask;

        Set(ConnectionState.Reconnecting);
        _ = ReconnectAsync(session.Token);
        return Task.CompletedTask;
    }

    private async Task ReconnectAsync(CancellationToken session)
    {
        try
        {
            while (!session.IsCancellationRequested)
            {
                await Task.Delay(ReconnectDelay, session);
                if (await TryConnectAsync(session) is null && !session.IsCancellationRequested)
                {
                    // Subscriptions do not survive a new session, so they are made again.
                    await SubscribeAsync(_filters);
                    Set(ConnectionState.Connected);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disconnect was requested.
        }
    }

    private async Task<string?> SubscribeAsync(IEnumerable<string> filters)
    {
        try
        {
            foreach (var filter in filters)
            {
                // QoS 2 here means messages arrive at whatever QoS they were published with.
                var result = await _client.SubscribeAsync(filter, MqttQualityOfServiceLevel.ExactlyOnce);
                if (result.Items.Any(i => i.ResultCode > MqttClientSubscribeResultCode.GrantedQoS2))
                    return $"The broker refused the filter {filter}";
            }

            return null;
        }
        catch (Exception e)
        {
            return e.Message;
        }
    }

    private Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var message = args.ApplicationMessage;
        MessageReceived?.Invoke(new ReceivedMessage(
            message.Topic, message.Payload.ToArray(), (int)message.QualityOfServiceLevel, message.Retain, DateTime.Now));
        return Task.CompletedTask;
    }

    private void Set(ConnectionState state, string? failureReason = null)
    {
        if (State == state && FailureReason == failureReason)
            return;

        State = state;
        FailureReason = failureReason;
        MqttMetrics.SetConnected(state == ConnectionState.Connected);
        StateChanged?.Invoke();
    }

    private static string Describe(MqttClientConnectResultCode code) => code switch
    {
        MqttClientConnectResultCode.BadUserNameOrPassword => "Bad username or password",
        MqttClientConnectResultCode.NotAuthorized => "Not authorized",
        MqttClientConnectResultCode.ClientIdentifierNotValid => "The broker rejected the client ID",
        MqttClientConnectResultCode.ServerUnavailable => "The broker is unavailable",
        MqttClientConnectResultCode.UnsupportedProtocolVersion => "The broker does not support this MQTT version",
        _ => $"The broker refused the connection: {code}",
    };
}
