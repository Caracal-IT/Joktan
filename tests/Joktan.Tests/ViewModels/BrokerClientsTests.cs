using System;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Certificates;
using Joktan.Containers;
using Joktan.Containers.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Joktan.ViewModels;
using Xunit;

namespace Joktan.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class BrokerClientsTests : DataFolderTest
{
    private readonly CertificateStore _store;
    private readonly StoredCertificate _root;
    private readonly StoredCertificate _server;
    private readonly StoredCertificate _client;
    private readonly StoredCertificate _other;

    public BrokerClientsTests()
    {
        _store = CertificateStore.Load(Data.At("certificates"));
        _root = _store.Create(Spec("root", CertificateKind.RootAuthority));
        _server = _store.Create(Spec("localhost", CertificateKind.Server, _root, "localhost"));
        _other = _store.Create(Spec("other", CertificateKind.RootAuthority));
        _client = _store.Create(Spec("client", CertificateKind.Client, _root));
    }

    private static CertificateSpec Spec(string name, CertificateKind kind, StoredCertificate? issuer = null, string names = "") => new()
    {
        Kind = kind, Name = name, CommonName = name, IssuerId = issuer?.Id, AlternativeNames = names, NotAfter = DateTimeOffset.UtcNow.AddYears(1),
    };

    [Fact]
    public void Each_port_is_a_choice_plain_first()
    {
        var broker = new MosquittoBroker { Name = "b", Port = 1884, TlsPort = 8885, MutualTlsPort = 8886, ServerCertificateId = _server.Id, Username = "alice" };

        var choices = BrokerClients.ChoicesFor([broker], _store, _ => "secret");

        Assert.Equal(["b · plain MQTT 1884", "b · TLS 8885", "b · mutual TLS 8886"], choices.Select(c => c.Text));
        Assert.All(choices, c => Assert.Equal((broker.Id, "b", "secret", "localhost", "alice"), (c.BrokerId, c.BrokerName, c.Password, c.Connection.Host, c.Connection.Username)));
        Assert.False(choices[0].Connection.UseTls);
        Assert.Equal((true, _root.Id, ""), (choices[1].Connection.UseTls, choices[1].Connection.CaCertificateId, choices[1].Connection.ClientCertificate));
        Assert.Equal((_root.Id, _client.Id, _client.KeyPath), (choices[2].Connection.CaCertificateId, choices[2].Connection.ClientCertificateId, choices[2].Connection.ClientKey));
        Assert.Equal("", BrokerClients.ChoicesFor([new MosquittoBroker { Name = "x", Port = 1 }], _store, _ => null)[0].Password);
    }

    [Fact]
    public void Mutual_tls_without_a_certificate_the_broker_accepts_has_none()
    {
        var other = BrokerClients.ProfileFor(new MosquittoBroker { Name = "b", MutualTlsPort = 8886, ServerCertificateId = _server.Id, ClientAuthorityId = _other.Id }, _store, BrokerListener.MutualTls)!;
        Assert.Equal(("", null), (other.ClientCertificate, other.ClientCertificateId));

        var unsigned = BrokerClients.ProfileFor(new MosquittoBroker { Name = "b", MutualTlsPort = 8886 }, _store, BrokerListener.MutualTls)!;
        Assert.Equal(("", ""), (unsigned.CaCertificate, unsigned.ClientCertificate));
        Assert.Null(BrokerClients.ProfileFor(new MosquittoBroker { Name = "b", Port = 1 }, _store, BrokerListener.Tls));
    }

    [Fact]
    public Task Brokers_there_at_the_start_get_clients_that_follow_their_certificates() => Headless.Run(() =>
    {
        var broker = new MosquittoBroker { Name = "secure", TlsPort = 8885, ServerCertificateId = _server.Id };
        new ComposeSettings { ComposeFiles = [], BrokersFolder = Data.At("brokers"), Brokers = [broker] }.Save(Data.At("settings"));
        var containers = new ContainersViewModel(Data.At("settings"), _store, new FakeCredentialStore());
        var mqtt = new MqttViewModel(_store, new MqttSettings { Clients = [new ClientProfile { Name = "mine", Host = "localhost", ClientId = "a" }] }, new FakeCredentialStore());

        BrokerClients.Link(containers.Brokers, mqtt);

        var client = mqtt.Clients.Single(c => c.Name == "secure");
        Assert.Equal(("8885", true, _root.Id), (client.Profile.Port, client.Profile.UseTls, client.Profile.CaCertificateId));
        // The CA is used from a copy, away from the folder that holds its key.
        Assert.NotEqual(_root.AuthorityPath, client.Profile.CaCertificate);
        Assert.Equal(System.IO.File.ReadAllText(_root.AuthorityPath), System.IO.File.ReadAllText(client.Profile.CaCertificate));
        Assert.Single(mqtt.BrokerChoices);

        // The card shows how its client is doing.
        Assert.Equal("MQTT: Disconnected", containers.Brokers.Items[0].ConnectionText);

        // A certificate deleted on the Certificates page is let go of.
        _store.Delete(_root);
        Assert.Equal((null, ""), (client.Profile.CaCertificateId, client.Profile.CaCertificate));
        containers.Shutdown();
    });

    [Fact]
    public Task Removing_a_broker_removes_its_client() => Headless.Run(async () =>
    {
        var broker = new MosquittoBroker { Name = "gone", Port = 1884 };
        new ComposeSettings { ComposeFiles = [], BrokersFolder = Data.At("brokers"), Brokers = [broker] }.Save(Data.At("settings"));
        var containers = new ContainersViewModel(Data.At("settings"), _store, new FakeCredentialStore());
        var mqtt = new MqttViewModel(_store, new MqttSettings
        {
            Clients =
            [
                new ClientProfile { Name = "mine", Host = "localhost", ClientId = "a" },
                new ClientProfile { Name = "gone", Host = "localhost", ClientId = "b", BrokerId = broker.Id },
            ],
        }, new FakeCredentialStore());
        BrokerClients.Link(containers.Brokers, mqtt);

        await containers.Brokers.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Equal(["mine"], mqtt.Clients.Select(c => c.Name));
        containers.Shutdown();
    });
}
