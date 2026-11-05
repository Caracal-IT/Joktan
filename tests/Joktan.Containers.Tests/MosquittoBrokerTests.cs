using System;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests;

public class MosquittoBrokerTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly TestCertificates _certificates;

    public MosquittoBrokerTests() => _certificates = new TestCertificates(_folder.Path);

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("mosquitto")]
    [InlineData("broker-2")]
    [InlineData("a_b")]
    [InlineData("9lives")]
    public void A_name_of_lower_case_letters_digits_dashes_and_underscores_will_do(string name)
    {
        Assert.Null(MosquittoBroker.CheckName(name, []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Broker")]
    [InlineData("-broker")]
    [InlineData("my broker")]
    [InlineData("broker.1")]
    public void Other_names_are_refused(string name)
    {
        Assert.NotNull(MosquittoBroker.CheckName(name, []));
    }

    [Fact]
    public void A_name_must_be_free_and_short()
    {
        Assert.NotNull(MosquittoBroker.CheckName("taken", ["taken"]));
        Assert.NotNull(MosquittoBroker.CheckName(new string('a', MosquittoBroker.MaxNameLength + 1), []));
        Assert.Null(MosquittoBroker.CheckName(new string('a', MosquittoBroker.MaxNameLength), []));
    }

    [Theory]
    [InlineData("", null)]
    [InlineData(" 1883 ", 1883)]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    public void A_port_is_a_number_or_nothing(string text, int? expected)
    {
        Assert.Null(MosquittoBroker.ParsePort(text, out var port));
        Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("port")]
    [InlineData("18 83")]
    public void Anything_else_is_not_a_port(string text)
    {
        Assert.NotNull(MosquittoBroker.ParsePort(text, out var port));
        Assert.Null(port);
    }

    [Fact]
    public void Its_ports_are_listed_as_on_the_page()
    {
        var broker = new MosquittoBroker { Port = 1883, TlsPort = 8883, MutualTlsPort = 8884 };

        Assert.Equal("1883 · TLS 8883 · mutual TLS 8884", broker.PortsText);
        Assert.Equal("TLS 8883", new MosquittoBroker { TlsPort = 8883 }.PortsText);
        Assert.Equal([1883, 8883, 8884], broker.HostPorts);
    }

    [Fact]
    public void A_broker_needs_a_port_of_its_own()
    {
        var store = _certificates.Store;

        Assert.NotNull(new MosquittoBroker { Name = "a" }.Check([], store, false));
        Assert.NotNull(new MosquittoBroker { Name = "a", Port = 1883, TlsPort = 1883, ServerCertificateId = _certificates.Server.Id }.Check([], store, false));

        var other = new MosquittoBroker { Name = "b", Port = 1883 };
        var problem = new MosquittoBroker { Name = "a", Port = 1883 }.Check([other], store, false);
        Assert.Contains("b", problem);

        // Itself is not another broker.
        Assert.Null(other.Check([other], store, false));
    }

    [Fact]
    public void A_login_needs_a_password_and_no_colon()
    {
        var store = _certificates.Store;

        Assert.NotNull(new MosquittoBroker { Name = "a", Port = 1, Username = "alice" }.Check([], store, hasPassword: false));
        Assert.Null(new MosquittoBroker { Name = "a", Port = 1, Username = "alice" }.Check([], store, hasPassword: true));
        Assert.NotNull(new MosquittoBroker { Name = "a", Port = 1, Username = "al:ice" }.Check([], store, hasPassword: true));
    }

    [Fact]
    public void Tls_needs_a_server_certificate()
    {
        var store = _certificates.Store;

        Assert.NotNull(new MosquittoBroker { Name = "a", TlsPort = 8883 }.Check([], store, false));
        Assert.NotNull(new MosquittoBroker { Name = "a", MutualTlsPort = 8884 }.Check([], store, false));
        Assert.NotNull(new MosquittoBroker { Name = "a", TlsPort = 8883, ServerCertificateId = "gone" }.Check([], store, false));
        Assert.Contains("root", new MosquittoBroker { Name = "a", TlsPort = 8883, ServerCertificateId = _certificates.Root.Id }.Check([], store, false));
        Assert.Null(new MosquittoBroker { Name = "a", TlsPort = 8883, ServerCertificateId = _certificates.Server.Id }.Check([], store, false));
    }

    [Fact]
    public void The_ca_for_client_certificates_must_be_a_ca_that_is_still_there()
    {
        var store = _certificates.Store;
        MosquittoBroker With(string? authority) => new()
        {
            Name = "a", MutualTlsPort = 8884, ServerCertificateId = _certificates.Server.Id, ClientAuthorityId = authority,
        };

        Assert.Null(With(null).Check([], store, false));
        Assert.Null(With(_certificates.Other.Id).Check([], store, false));
        Assert.NotNull(With("gone").Check([], store, false));
        Assert.Contains("client", With(_certificates.Client.Id).Check([], store, false));

        // Without a mutual TLS port, the choice does not matter.
        Assert.Null(new MosquittoBroker { Name = "a", TlsPort = 8883, ServerCertificateId = _certificates.Server.Id, ClientAuthorityId = "gone" }.Check([], store, false));
    }

    [Fact]
    public void A_clone_is_a_copy_and_each_new_broker_has_its_own_id()
    {
        var broker = new MosquittoBroker { Name = "a", Port = 1 };
        var clone = broker.Clone();
        clone.Name = "b";

        Assert.Equal("a", broker.Name);
        Assert.Equal(broker.Id, clone.Id);
        Assert.NotEqual(broker.Id, new MosquittoBroker().Id);
        Assert.Equal($"mosquitto:{broker.Id}", broker.Account);
    }
}
