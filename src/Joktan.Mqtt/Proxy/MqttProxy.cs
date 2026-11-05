using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Core;
using Joktan.Mqtt.Resources;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;
using MQTTnet.Server;

namespace Joktan.Mqtt.Proxy;

/// <summary>
/// An MQTT proxy: a broker on this computer that devices connect to, which passes what they
/// publish on to a target broker through one connection of its own, and what the target sends
/// for their subscriptions back to them. A device's message on the topic of a
/// <see cref="Selected"/> folder message is intercepted: one without a schedule has the folder
/// message merged into it, and one with a schedule is blocked, as the proxy sends that folder
/// message on its schedule instead. An intercepted message may go to the broker on another
/// topic, and what the broker sends there comes back to the devices on its own. Everything else
/// is passed on as it is.
/// </summary>
public sealed class MqttProxy : IAsyncDisposable
{
    /// <summary>Who messages from the target broker come from, when they are handed to the devices.</summary>
    private const string FromBroker = "joktan-proxy";

    private readonly Action<ActivityEntry> _log;
    private readonly Dictionary<string, HashSet<string>> _subscriptions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _clients = new(StringComparer.Ordinal);
    private readonly List<MqttServer> _servers = [];
    private BrokerEndpoint? _target;
    private IReadOnlyList<Interception> _selected = [];
    private long _forwarded;
    private long _modified;
    private long _blocked;
    private long _delivered;

    /// <param name="log">Told of everything that happens, on an arbitrary thread.</param>
    public MqttProxy(Action<ActivityEntry> log)
    {
        _log = log;
        Upstream.MessageReceived += OnFromBroker;
        Upstream.StateChanged += () => Changed?.Invoke();
    }

    /// <summary>How devices sign in: as anyone, with <see cref="Username"/> and <see cref="Password"/>, or as the target broker would let them.</summary>
    public DeviceLogin Login { get; set; } = DeviceLogin.Broker;

    /// <summary>The username devices sign in with, with <see cref="DeviceLogin.Own"/>.</summary>
    public string Username { get; set; } = "";

    /// <summary>The password devices sign in with, with <see cref="DeviceLogin.Own"/>.</summary>
    public string Password { get; set; } = "";

    /// <summary>The proxy's one connection to the target broker.</summary>
    public MqttConnection Upstream { get; } = new();

    /// <summary>Raised on an arbitrary thread when the clients, counts or the connection change.</summary>
    public event Action? Changed;

    public bool IsListening
    {
        get
        {
            lock (_servers)
                return _servers.Count > 0;
        }
    }

    // Devices on different ports are told apart, as their client IDs may be the same.
    private static string Key(ProxyListener kind, string clientId) => $"{kind}:{clientId}";

    private static string Who(ProxyListener kind, string clientId) => kind == ProxyListener.Plain ? clientId : $"{clientId} ({kind.Text()})";

    public int ClientCount
    {
        get
        {
            lock (_clients)
                return _clients.Count;
        }
    }

    public long Forwarded => Interlocked.Read(ref _forwarded);

    public long Modified => Interlocked.Read(ref _modified);

    public long Blocked => Interlocked.Read(ref _blocked);

    public long Delivered => Interlocked.Read(ref _delivered);

    /// <summary>
    /// The folder messages that are intercepted, and their topics at the broker; only valid ones
    /// count. Changing them subscribes again at the target, for the topics that changed.
    /// </summary>
    public IReadOnlyList<Interception> Selected
    {
        get => Volatile.Read(ref _selected);
        set
        {
            Volatile.Write(ref _selected, value.ToList());
            _ = ResubscribeAllAsync();
        }
    }

    /// <summary>
    /// The topic a device's topic goes to the broker on, given the payload sent: another only for an
    /// intercepted message that has one, its fields filled in from the topic and the payload. Null
    /// when a field has no value.
    /// </summary>
    public string? ToBroker(string topic, JsonNode? payload)
        => InterceptFor(topic) is { BrokerTopic.Length: > 0 } interception
            ? TopicRewrite.Rewrite(interception.Message.Topic, interception.BrokerTopic, topic, payload)
            : topic;

    /// <summary>As <see cref="ToBroker(string, JsonNode?)"/>, with the payload as JSON text; one that is not JSON has no paths.</summary>
    public string? ToBroker(string topic, string body) => ToBroker(topic, TryParse(body));

    /// <summary>
    /// The topic devices get a broker's message on: an intercepted message's own, where it went to
    /// the broker on another that holds every field of its own.
    /// </summary>
    public string ToDevices(string topic)
        => Reversible().FirstOrDefault(i => TopicRewrite.MatchesTemplate(i.BrokerTopic, topic)) is { } interception
            ? TopicRewrite.Rewrite(interception.BrokerTopic, interception.Message.Topic, topic) ?? topic
            : topic;

    private IEnumerable<Interception> Reversible()
        => Selected.Where(i => i.Message.IsValid && i.BrokerTopic.Length > 0 && TopicRewrite.CanReverse(i.Message.Topic, i.BrokerTopic));

    private static JsonNode? TryParse(string body)
    {
        try
        {
            return JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// What a device's filter is subscribed as at the broker: itself, and the topic at the broker
    /// of every intercepted message it takes in that goes there on another.
    /// </summary>
    public IEnumerable<string> FiltersAtBroker(string filter)
        => Reversible().Where(i => TopicRewrite.Overlaps(filter, i.Message.Topic))
            .Select(i => TopicRewrite.AsFilter(i.BrokerTopic))
            .Prepend(filter)
            .Distinct();

    /// <summary>
    /// Starts listening on each of the ports in <paramref name="listeners"/>, and connects to the
    /// target, which is retried while it does not answer. Returns why the proxy could not listen,
    /// or null; it then listens on none of them.
    /// </summary>
    public async Task<string?> StartAsync(ProxyListeners listeners, BrokerEndpoint target)
    {
        await StopAsync();
        if (!listeners.Ports().Any())
            return Strings.MqttProxy_ChooseAtLeastOnePort;
        if (Login == DeviceLogin.Own && (Username.Length == 0 || Password.Length == 0))
            return Strings.MqttProxy_SetTheProxysUsernameAndPassword;
        if ((listeners.TlsPort is not null || listeners.MutualTlsPort is not null) && listeners.ServerCertificate is null)
            return Strings.MqttProxy_TlsNeedsAServerCertificate;
        if (listeners.MutualTlsPort is not null && listeners.ClientAuthorities is not { Count: > 0 })
            return Strings.MqttProxy_MutualTlsNeedsACa;

        var factory = new MqttServerFactory();
        var started = new List<(ProxyListener Kind, int Port, MqttServer Server)>();
        foreach (var (kind, port) in listeners.Ports())
        {
            var server = factory.CreateMqttServer(Options(factory, kind, port, listeners));
            server.ValidatingConnectionAsync += e => ValidateAsync(kind, e);
            server.InterceptingPublishAsync += e => OnFromDeviceAsync(kind, e);
            server.ClientSubscribedTopicAsync += e => SubscriptionChangedAsync(Key(kind, e.ClientId), e.ClientId, kind, e.TopicFilter.Topic, true);
            server.ClientUnsubscribedTopicAsync += e => SubscriptionChangedAsync(Key(kind, e.ClientId), e.ClientId, kind, e.TopicFilter, false);
            server.ClientConnectedAsync += e => ClientChangedAsync(Key(kind, e.ClientId), e.ClientId, kind, true);
            server.ClientDisconnectedAsync += e => ClientChangedAsync(Key(kind, e.ClientId), e.ClientId, kind, false);
            try
            {
                await server.StartAsync();
            }
            catch (Exception e) when (e is SocketException or InvalidOperationException or System.IO.IOException or System.Security.Authentication.AuthenticationException)
            {
                server.Dispose();
                foreach (var (_, _, other) in started)
                {
                    await other.StopAsync();
                    other.Dispose();
                }

                return string.Format(Strings.MqttProxy_CannotListenOnPort, port, e.Message);
            }

            started.Add((kind, port, server));
        }

        lock (_servers)
            _servers.AddRange(started.Select(s => s.Server));
        foreach (var (kind, port, _) in started)
        {
            AppLog.For("Mqtt").LogInformation("The MQTT proxy is listening on port {Port} ({Kind})", port, kind);
            _log(new ActivityEntry(DateTime.Now, Strings.MqttProxy_Listening, Detail: string.Format(Strings.MqttProxy_OnPort, port, kind.Text())));
        }

        _target = target;
        Changed?.Invoke();
        await Upstream.ConnectAsync(target);
        return null;
    }

    private async Task ValidateAsync(ProxyListener kind, ValidatingConnectionEventArgs e)
    {
        var username = string.IsNullOrEmpty(e.UserName) ? null : e.UserName;
        switch (Login)
        {
            case DeviceLogin.Anyone:
                return;
            case DeviceLogin.Own:
                if (IsOwnLogin(username, e.Password))
                    return;
                e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
                _log(new ActivityEntry(DateTime.Now, Strings.MqttProxy_Refused, Source: Who(kind, e.ClientId),
                    Detail: string.Format(Strings.MqttProxy_NotTheProxysLogin, username ?? Strings.MqttProxy_NoUsername)));
                Changed?.Invoke();
                return;
        }

        if (_target is not { } target)
            return;

        // The broker decides: a connection of its own, with the device's login and the proxy's TLS settings, is tried and dropped.
        var trial = new MqttConnection();
        await trial.ConnectAsync(target with { ClientId = "joktan-check-" + Guid.NewGuid().ToString("N")[..8], Username = username, Password = username is null ? null : e.Password });
        var accepted = trial.IsConnected;
        var reason = trial.FailureReason;
        await trial.DisconnectAsync();
        if (accepted)
            return;

        e.ReasonCode = MqttConnectReasonCode.BadUserNameOrPassword;
        _log(new ActivityEntry(DateTime.Now, Strings.MqttProxy_Refused, Source: Who(kind, e.ClientId),
            Detail: string.Format(Strings.MqttProxy_TheBrokerRefusedTheLogin, username ?? Strings.MqttProxy_NoUsername, reason)));
        Changed?.Invoke();
    }

    private static MqttServerOptions Options(MqttServerFactory factory, ProxyListener kind, int port, ProxyListeners listeners)
    {
        var builder = factory.CreateServerOptionsBuilder();
        if (kind == ProxyListener.Plain)
        {
            builder = builder.WithDefaultEndpoint().WithDefaultEndpointPort(port);
            if (!listeners.AllowOtherComputers)
                builder = builder.WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointBoundIPV6Address(IPAddress.IPv6Loopback);
            return builder.Build();
        }

        // A certificate made from PEM files is given its key again through PFX, which the TLS stack on every system takes.
        var certificate = listeners.ServerCertificate ?? throw new InvalidOperationException(Strings.MqttProxy_TlsNeedsAServerCertificate);
        builder = builder.WithoutDefaultEndpoint().WithEncryptedEndpoint().WithEncryptedEndpointPort(port)
            .WithEncryptionCertificate(X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null));
        if (!listeners.AllowOtherComputers)
            builder = builder.WithEncryptedEndpointBoundIPAddress(IPAddress.Loopback).WithEncryptedEndpointBoundIPV6Address(IPAddress.IPv6Loopback);
        var options = builder.Build();
        options.TlsEndpointOptions.SslProtocol = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13;
        if (kind == ProxyListener.MutualTls)
        {
            var authorities = listeners.ClientAuthorities ?? [];
            options.TlsEndpointOptions.ClientCertificateRequired = true;
            options.TlsEndpointOptions.RemoteCertificateValidationCallback = (_, presented, _, _) => IsSignedBy(presented, authorities);
        }

        return options;
    }

    /// <summary>Whether a client's certificate chains to one of the CAs, and nothing but them, and is valid now.</summary>
    public static bool IsSignedBy(X509Certificate? presented, X509Certificate2Collection authorities)
    {
        if (presented is null || authorities.Count == 0)
            return false;

        using var certificate = X509CertificateLoader.LoadCertificate(presented.GetRawCertData());
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        foreach (var authority in authorities)
        {
            if (authority.SubjectName.RawData.AsSpan().SequenceEqual(authority.IssuerName.RawData))
                chain.ChainPolicy.CustomTrustStore.Add(authority);
            else
                chain.ChainPolicy.ExtraStore.Add(authority);
        }

        if (!chain.Build(certificate))
            return false;

        // An intermediate chosen alone must be in the chain, not only a root above it.
        return chain.ChainElements.Skip(1).Any(e => authorities.Any(a => a.Thumbprint == e.Certificate.Thumbprint));
    }

    // Compared in fixed time, so how long it takes says nothing about the password.
    private bool IsOwnLogin(string? username, string? password)
        => username == Username && Username.Length > 0
           && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(password ?? ""), Encoding.UTF8.GetBytes(Password));

    public async Task StopAsync()
    {
        MqttServer[] servers;
        lock (_servers)
        {
            servers = _servers.ToArray();
            _servers.Clear();
        }

        foreach (var server in servers)
        {
            try
            {
                await server.StopAsync();
            }
            catch (Exception)
            {
                // It is going either way.
            }

            server.Dispose();
        }

        if (servers.Length > 0)
            _log(new ActivityEntry(DateTime.Now, Strings.MqttProxy_Stopped));

        lock (_subscriptions)
            _subscriptions.Clear();
        lock (_clients)
            _clients.Clear();
        _target = null;
        await Upstream.DisconnectAsync();
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    /// <summary>
    /// What an intercepted message without a schedule becomes: the device's JSON with the folder
    /// message merged in and its varying fields applied, or the folder message itself when the
    /// device sent something that is not JSON. Gzipped when the device's was, or the folder message says so.
    /// </summary>
    public static byte[] Modify(FolderMessage message, byte[] payload, DateTimeOffset now)
    {
        var unzipped = Gzip.LooksZipped(payload) ? Gzip.TryDecompress(payload) : null;
        JsonNode? sent = null;
        try
        {
            sent = JsonNode.Parse(unzipped ?? payload);
        }
        catch (JsonException)
        {
            // Not JSON, so the folder message takes its place.
        }

        var settings = message.Settings;
        var merged = sent is null ? message.Body!.DeepClone() : JsonMerge.Merge(sent, message.Body) ?? message.Body!.DeepClone();
        lock (message.Counters)
            merged = VaryingFields.Apply(merged, settings.Arrays, settings.Fields, message.Counters, now);
        var bytes = Encoding.UTF8.GetBytes(merged.ToJsonString(JsonFormats.Compact));
        return unzipped is not null || settings.Gzip ? Gzip.Compress(bytes) : bytes;
    }

    /// <summary>The intercepted folder message a device's topic belongs to, if any.</summary>
    public Interception? InterceptFor(string topic)
        => Selected.FirstOrDefault(i => i.Message.IsValid && TopicRewrite.MatchesTemplate(i.Message.Topic, topic));

    private async Task OnFromDeviceAsync(ProxyListener kind, InterceptingPublishEventArgs e)
    {
        // What the target sent is handed to the devices that subscribed, as any broker would.
        if (e.ClientId == FromBroker)
            return;

        // Everything else goes to the target, which sends it back to those who subscribed.
        e.ProcessPublish = false;
        var who = Who(kind, e.ClientId);
        var message = e.ApplicationMessage;
        var topic = message.Topic;
        var payload = message.Payload.ToArray();
        var now = DateTime.Now;

        var intercepted = InterceptFor(topic)?.Message;
        if (intercepted is { HasSchedule: true })
        {
            Interlocked.Increment(ref _blocked);
            _log(new ActivityEntry(now, Strings.MqttProxy_Blocked, topic, who, string.Format(Strings.MqttProxy_SentOnItsScheduleInstead, intercepted.FileName)));
            Changed?.Invoke();
            return;
        }

        // What was sent, for the topic's fields and the activity.
        string? body = null;
        if (intercepted is not null)
        {
            payload = Modify(intercepted, payload, DateTimeOffset.Now);
            body = Encoding.UTF8.GetString(Gzip.TryDecompress(payload) ?? payload);
        }

        // Only an intercepted message with a topic of its own at the broker needs the payload read for it.
        var brokerTopic = InterceptFor(topic)?.BrokerTopic ?? "";
        var target = brokerTopic.Length == 0 ? topic
            : ToBroker(topic, body ?? Encoding.UTF8.GetString(Gzip.TryDecompress(payload) ?? payload));
        if (target is null)
        {
            _log(new ActivityEntry(now, ActivityEntry.FailedEvent, topic, who, string.Format(Strings.MqttProxy_NoValueForAField, brokerTopic)));
            Changed?.Invoke();
            return;
        }

        var (ok, error) = await Upstream.PublishAsync(target, payload, (int)message.QualityOfServiceLevel, message.Retain);
        if (ok)
        {
            Interlocked.Increment(ref _forwarded);
            if (intercepted is not null)
                Interlocked.Increment(ref _modified);
            var detail = target == topic ? "" : string.Format(Strings.MqttProxy_As, target);
            _log(intercepted is null
                ? new ActivityEntry(now, Strings.MqttProxy_Forwarded, topic, who, detail)
                : new ActivityEntry(now, Strings.MqttProxy_Modified, topic, who,
                    string.Join(" · ", new[] { string.Format(Strings.MqttProxy_MergedWith, intercepted.FileName), detail }.Where(t => t.Length > 0)), body));
        }
        else
        {
            _log(new ActivityEntry(now, ActivityEntry.FailedEvent, topic, who, error ?? Strings.MqttProxy_NotForwarded));
        }

        Changed?.Invoke();
    }

    private void OnFromBroker(ReceivedMessage received)
    {
        MqttServer[] servers;
        lock (_servers)
            servers = _servers.ToArray();
        if (servers.Length == 0)
            return;

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(ToDevices(received.Topic))
            .WithPayload(received.RawPayload)
            .WithQualityOfServiceLevel((MqttQualityOfServiceLevel)received.Qos)
            .WithRetainFlag(received.Retain)
            .Build();
        // Each port is a server of its own, and each hands the message to its own devices.
        foreach (var server in servers)
            _ = server.InjectApplicationMessage(new InjectedMqttApplicationMessage(message) { SenderClientId = FromBroker });
        Interlocked.Increment(ref _delivered);
        Changed?.Invoke();
    }

    private Task SubscriptionChangedAsync(string key, string clientId, ProxyListener kind, string filter, bool subscribed)
    {
        lock (_subscriptions)
        {
            if (!_subscriptions.TryGetValue(key, out var filters))
                _subscriptions[key] = filters = new HashSet<string>(StringComparer.Ordinal);
            if (subscribed)
                filters.Add(filter);
            else
                filters.Remove(filter);
        }

        _log(new ActivityEntry(DateTime.Now, subscribed ? Strings.MqttProxy_Subscribed : Strings.MqttProxy_Unsubscribed, filter, Who(kind, clientId),
            FiltersAtBroker(filter).Skip(1).ToList() is { Count: > 0 } also ? string.Format(Strings.MqttProxy_AlsoAs, string.Join(", ", also)) : ""));
        return ResubscribeAsync(key);
    }

    private async Task ClientChangedAsync(string key, string clientId, ProxyListener kind, bool connected)
    {
        lock (_clients)
        {
            if (connected)
                _clients.Add(key);
            else
                _clients.Remove(key);
        }

        _log(new ActivityEntry(DateTime.Now, connected ? Strings.MqttProxy_DeviceConnected : Strings.MqttProxy_DeviceDisconnected, Source: Who(kind, clientId)));
        if (!connected)
        {
            lock (_subscriptions)
                _subscriptions.Remove(key);
            await ResubscribeAsync(key);
        }

        Changed?.Invoke();
    }

    // Each device's filters are subscribed at the target under its own name, so they come and go with it.
    private async Task ResubscribeAsync(string clientId)
    {
        string[] filters;
        lock (_subscriptions)
            filters = _subscriptions.TryGetValue(clientId, out var set) ? set.SelectMany(FiltersAtBroker).Distinct().ToArray() : [];
        if (await Upstream.SetSubscriptionsAsync("device:" + clientId, filters) is { } error)
            _log(new ActivityEntry(DateTime.Now, ActivityEntry.FailedEvent, string.Join(", ", filters), clientId[(clientId.IndexOf(':') + 1)..], error));
    }

    private async Task ResubscribeAllAsync()
    {
        string[] clients;
        lock (_subscriptions)
            clients = _subscriptions.Keys.ToArray();
        foreach (var client in clients)
            await ResubscribeAsync(client);
    }
}
