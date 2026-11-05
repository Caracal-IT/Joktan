using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Mqtt.Proxy;
using MQTTnet;
using Xunit;

namespace Joktan.Mqtt.Tests.Proxy;

/// <summary>A real proxy between real MQTT clients and a real broker, all inside the test process.</summary>
public sealed class MqttProxyTests : IAsyncLifetime
{
    private readonly ConcurrentQueue<ActivityEntry> _log = new();
    private TestBroker _broker = null!;
    private MqttProxy _proxy = null!;
    private int _port;

    public async ValueTask InitializeAsync()
    {
        _broker = await TestBroker.StartAsync();
        _proxy = new MqttProxy(_log.Enqueue);
        _port = TestBroker.FreePort();
        Assert.Null(await _proxy.StartAsync(new ProxyListeners(_port, null, null, false), new BrokerEndpoint("127.0.0.1", _broker.Port, "proxy-under-test", null, null)));
        Assert.True(_proxy.Upstream.IsConnected);
    }

    public async ValueTask DisposeAsync()
    {
        await _proxy.DisposeAsync();
        await _broker.DisposeAsync();
    }

    private async Task<IMqttClient> Device(string id, ConcurrentQueue<(string Topic, string Payload)>? received = null)
    {
        var client = new MqttClientFactory().CreateMqttClient();
        if (received is not null)
        {
            client.ApplicationMessageReceivedAsync += e =>
            {
                received.Enqueue((e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString()));
                return Task.CompletedTask;
            };
        }

        await client.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", _port).WithClientId(id).Build());
        return client;
    }

    private static Task Publish(IMqttClient client, string topic, string payload)
        => client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(payload).Build());

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    private static FolderMessage Message(string topic, string body, MessageSettings? settings = null) => new()
    {
        FilePath = "/nowhere/" + topic.Replace('/', '-') + ".json",
        FileName = topic.Replace('/', '-') + ".json",
        Topic = topic,
        Text = body,
        Body = JsonNode.Parse(body),
        Settings = settings ?? MessageSettings.Default,
    };

    [Fact]
    public async Task What_a_device_publishes_reaches_the_broker()
    {
        await using var _ = new Disconnect(await Device("sensor"));
        var device = _.Client;

        await Publish(device, "plant/temperature", "21");

        await Until(() => _broker.Published.Any(p => p.Topic == "plant/temperature"));
        var sent = _broker.Published.Single(p => p.Topic == "plant/temperature");
        Assert.Equal(("21", "proxy-under-test"), (Encoding.UTF8.GetString(sent.Payload), sent.ClientId));
        await Until(() => _proxy.Forwarded == 1 && _proxy.ClientCount == 1);
        Assert.Contains(_log, e => e.Event == "Forwarded" && e.Topic == "plant/temperature" && e.Source == "sensor");
    }

    [Fact]
    public async Task An_intercepted_message_goes_to_its_topic_at_the_broker_and_comes_back_on_its_own()
    {
        // {line} comes from the device's topic, {id} from the payload after the merge.
        _proxy.Selected = [new Interception(Message("plant/{line}/status", """{"id":"cell-7"}"""), "lab/{line}/{id}/status")];
        var received = new ConcurrentQueue<(string Topic, string Payload)>();
        await using var _ = new Disconnect(await Device("watcher", received));
        await _.Client.SubscribeAsync("plant/#", cancellationToken: TestContext.Current.CancellationToken);
        await Until(() => _log.Any(e => e.Event == "Subscribed"));
        Assert.Contains(_log, e => e.Event == "Subscribed" && e.Detail == "also as lab/+/+/status");

        await Publish(_.Client, "plant/line1/status", """{"mode":"run"}""");
        await Until(() => _broker.PublishedTopics.Contains("lab/line1/cell-7/status"));

        // Messages not intercepted go as they are.
        await Publish(_.Client, "plant/line1/other", "1");
        await Until(() => _broker.PublishedTopics.Contains("plant/line1/other"));

        // The broker's message on that topic reaches the device on the message's own, once; a
        // field it has no level for there leaves it as the broker sent it.
        await _broker.SendAsync("lab/line2/x/status", "off");
        await Until(() => received.Any(r => r.Topic == "plant/line2/status"));
        Assert.Equal("off", received.Single(r => r.Topic == "plant/line2/status").Payload);
        await Until(() => received.Any(r => r.Topic == "plant/line1/other"));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Single(received, r => r.Topic == "plant/line1/other");
        Assert.True(_proxy.Delivered >= 2);
    }

    [Fact]
    public async Task A_topic_field_with_no_value_stops_the_message()
    {
        _proxy.Selected = [new Interception(Message("plant/status", """{"a":1}"""), "lab/{missing}")];
        await using var _ = new Disconnect(await Device("plc"));

        await Publish(_.Client, "plant/status", """{"b":2}""");

        await Until(() => _log.Any(e => e.Event == ActivityEntry.FailedEvent));
        Assert.Contains("lab/{missing}", _log.First(e => e.Event == ActivityEntry.FailedEvent).Detail);
        Assert.Equal(0, _proxy.Forwarded);
    }

    [Fact]
    public void Topics_map_only_for_intercepted_messages_that_can_go_both_ways()
    {
        _proxy.Selected =
        [
            new Interception(Message("a/{x}", "{}"), "b/{x}"),
            new Interception(Message("c/{x}", """{"id":1}"""), "d/{id}"),
            new Interception(Message("e", "{}")),
        ];

        Assert.Equal("b/1", _proxy.ToBroker("a/1", (System.Text.Json.Nodes.JsonNode?)null));
        Assert.Equal("d/1", _proxy.ToBroker("c/9", """{"id":1}"""));
        Assert.Null(_proxy.ToBroker("c/9", "not json"));
        Assert.Equal("e", _proxy.ToBroker("e", "{}"));
        Assert.Equal("z/1", _proxy.ToBroker("z/1", "{}"));
        Assert.Equal("a/1", _proxy.ToDevices("b/1"));
        Assert.Equal("d/1", _proxy.ToDevices("d/1"));
        Assert.Equal(["a/#", "b/+"], _proxy.FiltersAtBroker("a/#"));
        Assert.Equal(["#", "b/+"], _proxy.FiltersAtBroker("#"));
        Assert.Equal(["c/1"], _proxy.FiltersAtBroker("c/1"));
    }

    [Fact]
    public async Task A_selected_message_without_a_schedule_is_merged_into_what_the_device_sends()
    {
        var settings = MessageSettings.Default with { Fields = [new FieldRule("seq", FieldType.Counter)] };
        _proxy.Selected = [new Interception(Message("plant/{line}/status", """{"mode":"test","extra":{"b":2},"seq":0}""", settings))];
        await using var _ = new Disconnect(await Device("plc"));

        await Publish(_.Client, "plant/line1/status", """{"mode":"run","extra":{"a":1},"keep":true}""");
        await Publish(_.Client, "plant/line1/status", "not json");
        await Publish(_.Client, "plant/line1/other", """{"mode":"run"}""");

        await Until(() => _broker.Published.Count(p => p.Topic.StartsWith("plant/line1/", StringComparison.Ordinal)) == 3);
        var sent = _broker.Published.Where(p => p.Topic == "plant/line1/status").Select(p => JsonNode.Parse(p.Payload)!).ToList();
        Assert.Equal("""{"mode":"test","extra":{"a":1,"b":2},"keep":true,"seq":1}""", sent[0].ToJsonString());
        Assert.Equal("""{"mode":"test","extra":{"b":2},"seq":2}""", sent[1].ToJsonString());
        Assert.Equal("""{"mode":"run"}""", Encoding.UTF8.GetString(_broker.Published.Single(p => p.Topic == "plant/line1/other").Payload));
        Assert.Equal(2, _proxy.Modified);
        Assert.Contains(_log, e => e.Event == "Modified" && e.Body is not null);
    }

    [Fact]
    public async Task A_selected_message_with_a_schedule_blocks_what_devices_send_on_its_topic()
    {
        var scheduled = MessageSettings.Default with { Schedule = new MessageSchedule { Type = ScheduleType.Interval, Every = 1 } };
        _proxy.Selected = [new Interception(Message("plant/temperature", """{"t":20}""", scheduled))];
        await using var _ = new Disconnect(await Device("sensor"));

        await Publish(_.Client, "plant/temperature", """{"t":99}""");
        await Publish(_.Client, "plant/pressure", "1");

        await Until(() => _broker.PublishedTopics.Contains("plant/pressure"));
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("plant/temperature", _broker.PublishedTopics);
        Assert.Equal(1, _proxy.Blocked);
        Assert.Contains(_log, e => e.Event == "Blocked" && e.Source == "sensor");
    }

    [Fact]
    public async Task Unsubscribing_and_leaving_stop_the_broker_sending_to_the_device()
    {
        var received = new ConcurrentQueue<(string Topic, string Payload)>();
        await using var _ = new Disconnect(await Device("watcher", received));
        await _.Client.SubscribeAsync("a/#", cancellationToken: TestContext.Current.CancellationToken);
        await Until(() => _log.Any(e => e.Event == "Subscribed"));
        await _.Client.UnsubscribeAsync("a/#", TestContext.Current.CancellationToken);
        await Until(() => _log.Any(e => e.Event == "Unsubscribed"));

        await _broker.SendAsync("a/b", "x");
        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.Empty(received);

        await _.Client.DisconnectAsync(cancellationToken: TestContext.Current.CancellationToken);
        await Until(() => _proxy.ClientCount == 0);
        Assert.Contains(_log, e => e.Event == "Device disconnected");
    }

    [Fact]
    public async Task Without_the_broker_nothing_is_forwarded_and_it_says_why()
    {
        await _proxy.Upstream.DisconnectAsync();
        await using var _ = new Disconnect(await Device("sensor"));

        await Publish(_.Client, "x", "1");

        await Until(() => _log.Any(e => e.Event == ActivityEntry.FailedEvent));
        Assert.Equal(0, _proxy.Forwarded);
    }

    [Fact]
    public async Task A_port_in_use_is_reported_and_stopping_twice_is_harmless()
    {
        await using var other = new MqttProxy(_ => { });
        var problem = await other.StartAsync(new ProxyListeners(_port, null, null, false), new BrokerEndpoint("127.0.0.1", _broker.Port, "other", null, null));

        Assert.Contains(_port.ToString(), problem);
        Assert.False(other.IsListening);
        await _proxy.StopAsync();
        await _proxy.StopAsync();
        Assert.False(_proxy.IsListening);
    }

    [Fact]
    public void Modify_keeps_gzip_and_follows_the_messages_own_setting()
    {
        var message = Message("t", """{"b":2}""");
        var zipped = MqttProxy.Modify(message, Gzip.Compress(Encoding.UTF8.GetBytes("""{"a":1}""")), DateTimeOffset.Now);
        Assert.Equal("""{"a":1,"b":2}""", Encoding.UTF8.GetString(Gzip.TryDecompress(zipped)!));

        message.Settings = message.Settings with { Gzip = true };
        Assert.True(Gzip.LooksZipped(MqttProxy.Modify(message, Encoding.UTF8.GetBytes("{}"), DateTimeOffset.Now)));
    }

    [Fact]
    public async Task Without_a_port_or_the_certificates_tls_needs_it_does_not_start()
    {
        await using var other = new MqttProxy(_ => { });
        var target = new BrokerEndpoint("127.0.0.1", _broker.Port, "other", null, null);

        Assert.NotNull(await other.StartAsync(new ProxyListeners(null, null, null, false), target));
        Assert.NotNull(await other.StartAsync(new ProxyListeners(null, TestBroker.FreePort(), null, false), target));
        Assert.False(other.IsListening);
    }

    [Fact]
    public async Task A_device_must_sign_in_as_the_broker_would_let_it()
    {
        _broker.RequiredPassword = "secret";
        async Task<bool> Connect(string? username, string? password)
        {
            var device = new MqttConnection();
            await device.ConnectAsync(new BrokerEndpoint("127.0.0.1", _port, "dev-" + Guid.NewGuid().ToString("N")[..6], username, password));
            var connected = device.IsConnected;
            await device.DisconnectAsync();
            return connected;
        }

        Assert.False(await Connect("alice", "wrong"));
        Assert.False(await Connect(null, null));
        Assert.True(await Connect("alice", "secret"));
        Assert.Contains(_log, e => e.Event == "Refused" && e.Detail.Contains("alice"));
        Assert.Contains(_log, e => e.Event == "Refused" && e.Detail.Contains("no username"));

        // Anyone may connect, whatever they give.
        _proxy.Login = DeviceLogin.Anyone;
        Assert.True(await Connect("alice", "wrong"));
    }

    [Fact]
    public async Task With_its_own_login_devices_sign_in_with_that_and_the_proxy_with_the_brokers()
    {
        // The broker wants "secret"; devices give the proxy something else entirely.
        _broker.RequiredPassword = "secret";
        _proxy.Login = DeviceLogin.Own;
        _proxy.Username = "device";
        _proxy.Password = "letmein";
        async Task<MqttConnection> Connect(string? username, string? password)
        {
            var device = new MqttConnection();
            await device.ConnectAsync(new BrokerEndpoint("127.0.0.1", _port, "dev-" + Guid.NewGuid().ToString("N")[..6], username, password));
            return device;
        }

        var good = await Connect("device", "letmein");
        Assert.True(good.IsConnected);
        await good.PublishAsync("own/login", Encoding.UTF8.GetBytes("1"), 0, false);
        await Until(() => _broker.PublishedTopics.Contains("own/login"));
        await good.DisconnectAsync();

        foreach (var (username, password) in new (string?, string?)[] { ("device", "secret"), ("device", "wrong"), ("other", "letmein"), (null, null) })
        {
            var refused = await Connect(username, password);
            Assert.False(refused.IsConnected);
        }

        Assert.Contains(_log, e => e.Event == "Refused" && e.Detail == "Not the proxy's login: other");
    }

    [Fact]
    public async Task Its_own_login_must_be_set_before_it_starts()
    {
        await using var other = new MqttProxy(_ => { }) { Login = DeviceLogin.Own, Username = "device" };

        Assert.NotNull(await other.StartAsync(new ProxyListeners(TestBroker.FreePort(), null, null, false), new BrokerEndpoint("127.0.0.1", _broker.Port, "o", null, null)));
        Assert.False(other.IsListening);
    }

    private sealed class Disconnect(IMqttClient client) : IAsyncDisposable
    {
        public IMqttClient Client => client;

        public async ValueTask DisposeAsync()
        {
            if (client.IsConnected)
                await client.DisconnectAsync();
            client.Dispose();
        }
    }
}
