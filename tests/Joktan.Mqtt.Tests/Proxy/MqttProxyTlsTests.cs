using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Joktan.Certificates;
using Joktan.Mqtt.Proxy;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.Proxy;

/// <summary>
/// Devices reaching the proxy with TLS and mutual TLS, with certificates made as on the
/// Certificates page, through Joktan's own client, which checks the proxy's certificate and name.
/// </summary>
public sealed class MqttProxyTlsTests : IAsyncLifetime
{
    private readonly TempFolder _folder = new();
    private CertificateStore _store = null!;
    private StoredCertificate _root = null!;
    private StoredCertificate _server = null!;
    private StoredCertificate _client = null!;
    private StoredCertificate _stranger = null!;
    private TestBroker _broker = null!;
    private MqttProxy _proxy = null!;
    private int _tls;
    private int _mutual;

    private static CertificateSpec Spec(string name, CertificateKind kind, StoredCertificate? issuer = null, string names = "") => new()
    {
        Kind = kind, Name = name, CommonName = name, IssuerId = issuer?.Id, AlternativeNames = names, NotAfter = DateTimeOffset.UtcNow.AddYears(1),
    };

    public async ValueTask InitializeAsync()
    {
        _store = CertificateStore.Load(_folder.Path);
        _root = _store.Create(Spec("root", CertificateKind.RootAuthority));
        _server = _store.Create(Spec("localhost", CertificateKind.Server, _root, "localhost, 127.0.0.1"));
        _client = _store.Create(Spec("device", CertificateKind.Client, _root));
        var other = _store.Create(Spec("other", CertificateKind.RootAuthority));
        _stranger = _store.Create(Spec("stranger", CertificateKind.Client, other));

        _broker = await TestBroker.StartAsync();
        _proxy = new MqttProxy(_ => { });
        _tls = TestBroker.FreePort();
        _mutual = TestBroker.FreePort();
        var authorities = new X509Certificate2Collection();
        authorities.ImportFromPemFile(_server.AuthorityPath);
        var listeners = new ProxyListeners(null, _tls, _mutual, false, X509Certificate2.CreateFromPemFile(_server.CertificatePath, _server.KeyPath), authorities);
        Assert.Null(await _proxy.StartAsync(listeners, new BrokerEndpoint("127.0.0.1", _broker.Port, "proxy", null, null)));
    }

    public async ValueTask DisposeAsync()
    {
        await _proxy.DisposeAsync();
        await _broker.DisposeAsync();
        _folder.Dispose();
    }

    private async Task<MqttConnection> Device(int port, StoredCertificate? certificate = null)
    {
        var connection = new MqttConnection();
        await connection.ConnectAsync(new BrokerEndpoint("localhost", port, "device-" + Guid.NewGuid().ToString("N")[..6], null, null,
            new TlsEndpoint(_root.AuthorityPath, certificate?.CertificatePath, certificate?.KeyPath, null, false)));
        return connection;
    }

    private async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(25);
        Assert.True(condition());
    }

    [Fact]
    public async Task A_device_connects_with_tls_trusting_the_proxys_certificate()
    {
        var device = await Device(_tls);
        Assert.Equal(ConnectionState.Connected, device.State);

        await device.PublishAsync("secure/topic", Encoding.UTF8.GetBytes("1"), 0, false);

        await Until(() => _broker.PublishedTopics.Contains("secure/topic"));
        await device.DisconnectAsync();
    }

    [Fact]
    public async Task Mutual_tls_takes_a_certificate_signed_by_the_ca_and_nothing_else()
    {
        var signed = await Device(_mutual, _client);
        Assert.Equal(ConnectionState.Connected, signed.State);
        await signed.PublishAsync("mutual/topic", Encoding.UTF8.GetBytes("1"), 0, false);
        await Until(() => _broker.PublishedTopics.Contains("mutual/topic"));
        await signed.DisconnectAsync();

        var none = await Device(_mutual);
        Assert.Equal(ConnectionState.Failed, none.State);
        var stranger = await Device(_mutual, _stranger);
        Assert.Equal(ConnectionState.Failed, stranger.State);
        Assert.Equal(1, _proxy.Forwarded);
    }

    [Fact]
    public async Task Devices_on_different_ports_get_each_others_messages_through_the_broker()
    {
        var watcher = await Device(_tls);
        var received = new System.Collections.Concurrent.ConcurrentQueue<string>();
        watcher.MessageReceived += m => received.Enqueue(m.Topic);
        await watcher.SetSubscriptionsAsync("test", ["shared/#"]);
        await Task.Delay(200);

        var sender = await Device(_mutual, _client);
        await sender.PublishAsync("shared/a", Encoding.UTF8.GetBytes("1"), 0, false);

        await Until(() => received.Contains("shared/a"));
        Assert.Equal(2, _proxy.ClientCount);
        await watcher.DisconnectAsync();
        await sender.DisconnectAsync();
    }

    [Fact]
    public void Only_a_certificate_that_chains_to_the_chosen_cas_is_signed_by_them()
    {
        var roots = new X509Certificate2Collection();
        roots.ImportFromPemFile(_root.AuthorityPath);
        using var client = X509Certificate2.CreateFromPem(System.IO.File.ReadAllText(_client.CertificatePath));
        using var stranger = X509Certificate2.CreateFromPem(System.IO.File.ReadAllText(_stranger.CertificatePath));

        Assert.True(MqttProxy.IsSignedBy(client, roots));
        Assert.False(MqttProxy.IsSignedBy(stranger, roots));
        Assert.False(MqttProxy.IsSignedBy(null, roots));
        Assert.False(MqttProxy.IsSignedBy(client, []));
    }
}
