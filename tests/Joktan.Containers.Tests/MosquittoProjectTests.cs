using System;
using System.Collections.Generic;
using System.IO;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests;

public class MosquittoProjectTests : IDisposable
{
    private readonly TempFolder _folder = new();
    private readonly TestCertificates _certificates;
    private readonly Dictionary<string, string> _passwords = [];

    public MosquittoProjectTests() => _certificates = new TestCertificates(_folder.At("certificates"));

    public void Dispose() => _folder.Dispose();

    private string Project => _folder.At("project");

    private void Write(params MosquittoBroker[] brokers)
        => MosquittoProject.Write(Project, "test-brokers", MosquittoProject.DefaultImage, brokers, b => _passwords.GetValueOrDefault(b.Id), _certificates.Store);

    private string Read(params string[] parts) => File.ReadAllText(Path.Combine([Project, .. parts]));

    private bool Exists(params string[] parts) => File.Exists(Path.Combine([Project, .. parts]));

    [Fact]
    public void An_open_broker_gets_a_service_a_dockerfile_and_a_configuration()
    {
        Write(new MosquittoBroker { Name = "open", Port = 1883 });

        var compose = Read("docker-compose.yml");
        Assert.Contains("name: test-brokers\n", compose);
        Assert.Contains("  open:\n    build: ./open\n    pull_policy: build\n    image: localhost/test-brokers-open:latest\n", compose);
        Assert.Contains("      - \"1883:1883\" # plain MQTT\n", compose);
        Assert.Contains("healthcheck:", compose);
        Assert.Contains("$$SYS/broker/uptime", compose);

        var config = Read("open", "mosquitto.conf");
        Assert.Contains("allow_anonymous true\n", config);
        Assert.Contains("listener 1883\n", config);
        Assert.DoesNotContain("listener 8883", config);

        var dockerfile = Read("open", "Dockerfile");
        Assert.Contains("FROM docker.io/library/eclipse-mosquitto:2\n", dockerfile);
        Assert.Contains("COPY --chown=mosquitto:mosquitto mosquitto.conf /mosquitto/config/mosquitto.conf\n", dockerfile);
        Assert.DoesNotContain("passwd", dockerfile);
        Assert.DoesNotContain("certs", dockerfile);
        Assert.False(Exists("open", "passwd"));
        Assert.False(Directory.Exists(Path.Combine(Project, "open", "certs")));
    }

    [Fact]
    public void Ports_on_the_host_are_published_on_the_brokers_own()
    {
        Write(new MosquittoBroker { Name = "b", Port = 1783, TlsPort = 8783, MutualTlsPort = 8784, ServerCertificateId = _certificates.Server.Id });

        var compose = Read("docker-compose.yml");
        Assert.Contains("      - \"1783:1883\" # plain MQTT\n      - \"8783:8883\" # TLS\n      - \"8784:8884\" # mutual TLS\n", compose);
    }

    [Fact]
    public void A_login_writes_a_password_file_with_only_the_hash()
    {
        var broker = new MosquittoBroker { Name = "locked", Port = 1883, Username = "alice" };
        _passwords[broker.Id] = "secret";

        Write(broker);

        var line = Read("locked", "passwd");
        Assert.True(MosquittoPassword.Matches(line, "alice", "secret"));
        Assert.DoesNotContain("secret", line);
        Assert.Contains("allow_anonymous false\npassword_file /mosquitto/config/passwd\n", Read("locked", "mosquitto.conf"));
        Assert.Contains("COPY --chown=mosquitto:mosquitto passwd /mosquitto/config/passwd\n", Read("locked", "Dockerfile"));
        Assert.Contains("RUN chmod 0600 /mosquitto/config/passwd\n", Read("locked", "Dockerfile"));

        // The health check cannot sign in, so there is none.
        Assert.DoesNotContain("healthcheck", Read("docker-compose.yml"));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(Project, "locked", "passwd")));
    }

    [Fact]
    public void The_password_file_is_kept_while_the_password_is_the_same()
    {
        var broker = new MosquittoBroker { Name = "locked", Port = 1883, Username = "alice" };
        _passwords[broker.Id] = "secret";
        Write(broker);
        var first = Read("locked", "passwd");

        Write(broker);
        Assert.Equal(first, Read("locked", "passwd"));

        _passwords[broker.Id] = "changed";
        Write(broker);
        Assert.True(MosquittoPassword.Matches(Read("locked", "passwd"), "alice", "changed"));

        broker.Username = "bob";
        Write(broker);
        Assert.True(MosquittoPassword.Matches(Read("locked", "passwd"), "bob", "changed"));

        broker.Username = "";
        Write(broker);
        Assert.False(Exists("locked", "passwd"));
    }

    [Fact]
    public void Tls_copies_the_server_certificate_with_its_chain_and_key()
    {
        Write(new MosquittoBroker { Name = "tls", TlsPort = 8883, ServerCertificateId = _certificates.Server.Id });

        Assert.Equal(File.ReadAllText(_certificates.Server.ChainPath), Read("tls", "certs", "cert.pem"));
        Assert.Equal(File.ReadAllText(_certificates.Server.KeyPath), Read("tls", "certs", "key.pem"));
        Assert.False(Exists("tls", "certs", "ca.pem"));

        var config = Read("tls", "mosquitto.conf");
        Assert.Contains("listener 8883\ncertfile /mosquitto/certs/cert.pem\nkeyfile /mosquitto/certs/key.pem\n", config);
        Assert.DoesNotContain("require_certificate", config);
        Assert.DoesNotContain("listener 1883", config);
        Assert.Contains("COPY --chown=mosquitto:mosquitto certs/ /mosquitto/certs/\nRUN chmod 0600 /mosquitto/certs/key.pem\n", Read("tls", "Dockerfile"));
        Assert.DoesNotContain("healthcheck", Read("docker-compose.yml"));
    }

    [Fact]
    public void Mutual_tls_trusts_the_server_certificates_cas_or_the_one_chosen()
    {
        var broker = new MosquittoBroker { Name = "mtls", MutualTlsPort = 8884, ServerCertificateId = _certificates.Server.Id };
        Write(broker);

        Assert.Equal(File.ReadAllText(_certificates.Server.AuthorityPath), Read("mtls", "certs", "ca.pem"));
        Assert.Contains("listener 8884\ncafile /mosquitto/certs/ca.pem\ncertfile /mosquitto/certs/cert.pem\nkeyfile /mosquitto/certs/key.pem\nrequire_certificate true\n",
            Read("mtls", "mosquitto.conf"));

        broker.ClientAuthorityId = _certificates.Other.Id;
        Write(broker);
        Assert.Equal(File.ReadAllText(_certificates.Other.AuthorityPath), Read("mtls", "certs", "ca.pem"));

        broker.MutualTlsPort = null;
        broker.Port = 1883;
        Write(broker);
        Assert.False(Directory.Exists(Path.Combine(Project, "mtls", "certs")));
    }

    [Fact]
    public void A_broker_taken_away_has_its_folder_removed_but_nothing_else_is()
    {
        Write(new MosquittoBroker { Name = "a", Port = 1 }, new MosquittoBroker { Name = "b", Port = 2 });
        File.WriteAllText(Path.Combine(Project, "b", "notes.txt"), "mine");
        _folder.Write("project/theirs/Dockerfile", "FROM somewhere\n");

        Write(new MosquittoBroker { Name = "a", Port = 1 });

        Assert.DoesNotContain("  b:", Read("docker-compose.yml"));
        Assert.False(Exists("b", "Dockerfile"));
        Assert.True(Exists("b", "notes.txt"));
        Assert.True(Exists("theirs", "Dockerfile"));
    }

    [Fact]
    public void With_no_brokers_the_file_has_no_services()
    {
        Write();

        Assert.EndsWith("services: {}\n", Read("docker-compose.yml"));
    }

    [Fact]
    public void Delete_removes_only_what_was_written()
    {
        Write(new MosquittoBroker { Name = "a", Port = 1, TlsPort = 2, ServerCertificateId = _certificates.Server.Id });
        MosquittoProject.Delete(Project);
        Assert.False(Directory.Exists(Project));

        Write(new MosquittoBroker { Name = "a", Port = 1 });
        _folder.Write("project/README.md", "mine");
        MosquittoProject.Delete(Project);
        Assert.True(Exists("README.md"));
        Assert.False(Exists("docker-compose.yml"));
        Assert.False(Directory.Exists(Path.Combine(Project, "a")));

        MosquittoProject.Delete(_folder.At("never"));
    }

    [Fact]
    public void A_broker_that_cannot_be_written_stops_the_whole_project()
    {
        var broker = new MosquittoBroker { Name = "locked", Port = 1883, Username = "alice" };

        var problem = Assert.Throws<FormatException>(() => Write(broker));
        Assert.StartsWith("locked: ", problem.Message);
        Assert.False(Exists("docker-compose.yml"));

        Assert.Throws<FormatException>(() => MosquittoProject.Write(Project, "Bad Name", MosquittoProject.DefaultImage, [], _ => null, _certificates.Store));
        Assert.Throws<FormatException>(() => MosquittoProject.Write(Project, "ok", "", [], _ => null, _certificates.Store));
        Assert.Throws<FormatException>(() => MosquittoProject.Write("relative", "ok", MosquittoProject.DefaultImage, [], _ => null, _certificates.Store));
    }

    [Fact]
    public void A_folder_that_is_a_file_cannot_be_written_to()
    {
        var file = _folder.Write("file", "");

        Assert.NotNull(MosquittoProject.CheckFolder(file));
        Assert.Null(MosquittoProject.CheckFolder(""));
        Assert.Null(MosquittoProject.CheckFolder(_folder.Path));

        // A folder inside a file cannot be made.
        Assert.Throws<FormatException>(() => MosquittoProject.Write(Path.Combine(file, "inside"), "ok", MosquittoProject.DefaultImage, [], _ => null, _certificates.Store));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("joktan-mosquitto", true)]
    [InlineData("Joktan", false)]
    [InlineData("_x", false)]
    public void Project_names_are_lower_case(string name, bool valid)
    {
        Assert.Equal(valid, MosquittoProject.CheckProjectName(name) is null);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("docker.io/library/eclipse-mosquitto:2.0.20", true)]
    [InlineData("eclipse mosquitto", false)]
    public void An_image_is_a_name_without_spaces(string image, bool valid)
    {
        Assert.Equal(valid, MosquittoProject.CheckImage(image) is null);
    }
}
