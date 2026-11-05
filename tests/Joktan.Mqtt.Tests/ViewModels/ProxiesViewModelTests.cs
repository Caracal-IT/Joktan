using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Joktan.Mqtt.Proxy;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using MQTTnet;
using Xunit;

namespace Joktan.Mqtt.Tests.ViewModels;

// The proxies' credential store is a fake, so nothing reaches the real one.
[Collection(ProcessStateCollection.Name)]
public class ProxiesViewModelTests : DataFolderTest
{
    private readonly FakeCredentialStore _credentials = new();

    private MqttViewModel Mqtt() => new(null, new MqttSettings { Clients = [new ClientProfile { Name = "a", Host = "localhost", ClientId = "a" }] }, new FakeCredentialStore());

    private ProxiesViewModel Page(MqttViewModel? mqtt = null, ProxySettings? settings = null)
        => new(mqtt ?? Mqtt(), settings ?? new ProxySettings(), _credentials, Data.At("proxies"));

    private static BrokerChoice Choice(string id, string port) => new(id, "mosquitto", BrokerListener.Plain, $"mosquitto · plain MQTT {port}", new ClientProfile { Host = "127.0.0.1", Port = port }, "");

    private string Folder()
    {
        // A message's topic is the folder it is in.
        Data.Write("messages/plant/temperature/temperature.json", """{"t":20}""");
        Data.Write("messages/plant/temperature/temperature.send.json", """{"schedule":{"type":"interval","every":1}}""");
        Data.Write("messages/plant/status/status.json", """{"mode":"test"}""");
        Data.Write("messages/plant/bad/bad.json", "{ not json");
        return Data.At("messages");
    }

    [Fact]
    public Task A_new_proxy_takes_a_free_name_and_port_and_the_first_broker_from_containers() => Headless.Run(() =>
    {
        var mqtt = Mqtt();
        mqtt.SyncBrokers([Choice("b1", "1999")], []);
        var page = Page(mqtt);
        Assert.False(page.HasProxies);

        page.AddCommand.Execute(null);
        page.AddCommand.Execute(null);

        Assert.Equal(["Proxy 1", "Proxy 2"], page.Proxies.Select(p => p.Name));
        Assert.Equal([1885, 1886], page.Proxies.Select(p => p.Profile.ListenPort));
        var first = page.Proxies[0];
        Assert.Equal(("127.0.0.1:1999", "b1", "Follows mosquitto · plain MQTT 1999 on Dev Tools / Containers"), (first.TargetText, first.Profile.Target.BrokerId, first.FollowsText));
        Assert.Same(page.Proxies[1], page.SelectedProxy);
        Assert.Equal("Devices connect to localhost: plain MQTT on 1885", first.AddressText);

        var saved = ProxySettings.Load(Data.At("proxies"));
        Assert.Equal(2, saved.Proxies.Count);
        Assert.Equal(saved.Proxies[1].Id, saved.SelectedProxy);
        Assert.Same(page.Proxies[1], Page(mqtt, saved).SelectedProxy is { } again && again.Profile.Id == saved.SelectedProxy ? page.Proxies[1] : null);
    });

    [Fact]
    public Task The_name_and_port_must_be_free_and_valid() => Headless.Run(() =>
    {
        var page = Page();
        page.AddCommand.Execute(null);
        page.AddCommand.Execute(null);
        var proxy = page.Proxies[1];

        proxy.NameText = "Proxy 1";
        Assert.NotNull(proxy.NameError);
        proxy.NameText = "bad/name";
        Assert.NotNull(proxy.NameError);
        proxy.NameText = "Lab";
        Assert.Null(proxy.NameError);
        Assert.Equal("Lab", proxy.Name);

        proxy.PortText = "1885";
        Assert.Equal("Another proxy uses port 1885.", proxy.PortError);
        proxy.PortText = "nope";
        Assert.NotNull(proxy.PortError);
        proxy.PortText = "";
        Assert.Equal("Choose at least one port for devices to connect to.", proxy.PortError);
        proxy.TlsPortText = "1990";
        proxy.PortText = "1990";
        Assert.Equal("Each port must be different.", proxy.PortError);
        proxy.TlsPortText = "";
        Assert.Null(proxy.PortError);

        proxy.AllowOtherComputers = true;
        Assert.Equal("Devices connect to this computer's address: plain MQTT on 1990", proxy.AddressText);
        var saved = ProxySettings.Load(Data.At("proxies")).Proxies[1];
        Assert.Equal(("Lab", 1990, true), (saved.Name, saved.ListenPort, saved.AllowOtherComputers));
    });

    [Fact]
    public Task An_intercepted_message_keeps_its_topic_at_the_broker_once_valid() => Headless.Run(async () =>
    {
        var page = Page(settings: new ProxySettings { Proxies = [new ProxyProfile { Name = "P", Folder = Folder(), Selected = ["plant/status/status.json"] }] });
        var proxy = page.Proxies[0];
        await Headless.Until(() => proxy.Rows.Count == 3);
        var row = proxy.Rows.Single(r => r.Key == "plant/status/status.json");
        Assert.Equal("plant/status", row.BrokerTopicPlaceholder);

        row.BrokerTopicText = "lab/#";
        Assert.NotNull(row.BrokerTopicError);
        Assert.Equal("", row.BrokerTopic);
        row.BrokerTopicText = "lab/{mode}/status";
        Assert.Null(row.BrokerTopicError);

        Assert.Equal("lab/{mode}/status", ProxySettings.Load(Data.At("proxies")).Proxies[0].BrokerTopics["plant/status/status.json"]);
        Assert.Equal("lab/test/status", proxy.Engine.ToBroker("plant/status", """{"mode":"test"}"""));

        // Read again, it keeps it; emptied, it goes.
        await proxy.ReloadCommand.ExecuteAsync(null);
        row = proxy.Rows.Single(r => r.Key == "plant/status/status.json");
        Assert.Equal("lab/{mode}/status", row.BrokerTopicText);
        row.BrokerTopicText = "";
        Assert.Empty(ProxySettings.Load(Data.At("proxies")).Proxies[0].BrokerTopics);
    });

    [Fact]
    public Task The_folder_lists_its_messages_and_switching_one_on_intercepts_it() => Headless.Run(async () =>
    {
        var folder = Folder();
        var page = Page(settings: new ProxySettings { Proxies = [new ProxyProfile { Name = "P", Folder = folder, Selected = ["plant/temperature/temperature.json"] }] });
        var proxy = page.Proxies[0];
        await Headless.Until(() => proxy.Rows.Count == 3);

        var temperature = proxy.Rows.Single(r => r.Key == "plant/temperature/temperature.json");
        var status = proxy.Rows.Single(r => r.Key == "plant/status/status.json");
        var bad = proxy.Rows.Single(r => r.Key == "plant/bad/bad.json");
        Assert.True(temperature.IsSelected);
        Assert.StartsWith("Sent by the proxy ", temperature.ModeText);
        Assert.EndsWith("devices' messages on its topic are blocked", temperature.ModeText);
        Assert.Equal("Merged into each message devices send on its topic", status.ModeText);
        Assert.False(bad.IsValid);
        Assert.False(bad.CanEdit);
        bad.IsSelected = true;
        Assert.False(bad.IsSelected);

        status.IsSelected = true;
        Assert.Equal(["plant/status/status.json", "plant/temperature/temperature.json"], proxy.Profile.Selected.Order());
        Assert.Equal(2, proxy.Engine.Selected.Count);

        // Whether it is switched on here is not the file's own setting.
        Assert.False(File.Exists(Data.At("messages/plant/status/status.send.json")));
        Assert.DoesNotContain("enabled", File.ReadAllText(Data.At("messages/plant/temperature/temperature.send.json")));

        // The editor sees the file's settings.
        MessageSettingsViewModel? edited = null;
        page.EditSettings = vm =>
        {
            edited = vm;
            return Task.FromResult(false);
        };
        await temperature.EditCommand.ExecuteAsync(null);
        Assert.False(edited!.Enabled);
    });

    [Fact]
    public Task Running_it_forwards_modifies_blocks_and_sends_on_schedule() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var port = TestBroker.FreePort();
        var page = Page(settings: new ProxySettings
        {
            Proxies =
            [
                new ProxyProfile
                {
                    Name = "P", ListenPort = port, Folder = Folder(), Selected = ["plant/temperature/temperature.json", "plant/status/status.json"],
                    Target = new ClientProfile { Host = "127.0.0.1", Port = broker.Port.ToString(), ClientId = "proxy-vm" },
                    BrokerTopics = new() { ["plant/temperature/temperature.json"] = "lab/plant/temperature", ["plant/status/status.json"] = "lab/plant/status" },
                },
            ],
        });
        var proxy = page.Proxies[0];
        await Headless.Until(() => proxy.Rows.Count == 3);

        await proxy.StartStopCommand.ExecuteAsync(null);
        Assert.True(proxy.IsRunning);
        Assert.False(proxy.CanChange);
        Assert.Equal("Stop", proxy.StartStopText);
        Assert.False(proxy.EditTargetCommand.CanExecute(null));

        var device = new MqttClientFactory().CreateMqttClient();
        await device.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port).WithClientId("plc").Build());
        await device.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("plant/status").WithPayload("""{"mode":"run","x":1}""").Build());
        await device.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("plant/temperature").WithPayload("""{"t":99}""").Build());
        await device.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("plant/other").WithPayload("1").Build());

        // The scheduled message goes on its own, every second, on its topic at the broker too.
        await Headless.Until(() => broker.PublishedTopics.Count(t => t == "lab/plant/temperature") >= 1 && broker.PublishedTopics.Contains("plant/other"));
        await proxy.FlushAsync();
        Assert.Equal("""{"mode":"test","x":1}""", Encoding.UTF8.GetString(broker.Published.Single(p => p.Topic == "lab/plant/status").Payload));
        Assert.All(broker.Published.Where(p => p.Topic == "lab/plant/temperature"), p => Assert.Equal("""{"t":20}""", Encoding.UTF8.GetString(p.Payload)));
        Assert.Contains(proxy.Entries, e => e.Event == "Blocked");
        Assert.Contains(proxy.Entries, e => e.Event == "Sent" && e.Topic == "lab/plant/temperature");
        Assert.Contains("1 devices", proxy.StatsText);
        Assert.True(proxy.IsUpstreamConnected);
        Assert.Contains("sent", proxy.Rows.Single(r => r.Key == "plant/temperature/temperature.json").StateText);

        proxy.SelectedEntry = proxy.Entries.First(e => e.Event == "Modified");
        Assert.Equal("""{"mode":"test","x":1}""", proxy.SelectedBody);
        proxy.ClearLogCommand.Execute(null);
        Assert.Empty(proxy.Entries);

        await device.DisconnectAsync();
        await proxy.StartStopCommand.ExecuteAsync(null);
        Assert.False(proxy.IsRunning);
        Assert.Equal("", proxy.Rows.Single(r => r.Key == "plant/temperature/temperature.json").StateText);
    });

    [Fact]
    public Task A_port_in_use_says_so() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var page = Page(settings: new ProxySettings { Proxies = [new ProxyProfile { Name = "P", ListenPort = broker.Port }] });

        await page.Proxies[0].StartAsync();

        Assert.False(page.Proxies[0].IsRunning);
        Assert.Contains(broker.Port.ToString(), page.Proxies[0].Error);
    });

    [Fact]
    public Task The_target_is_edited_with_the_client_editor_and_its_password_kept() => Headless.Run(async () =>
    {
        var page = Page();
        page.AddCommand.Execute(null);
        var proxy = page.Proxies[0];
        await proxy.EditTargetCommand.ExecuteAsync(null);

        ClientSettingsViewModel? editor = null;
        page.EditClientSettings = vm =>
        {
            editor = vm;
            vm.Host = "broker.example";
            vm.Port = "8883";
            vm.UseTls = true;
            vm.Username = "alice";
            vm.Password = "secret";
            return Task.FromResult(true);
        };
        await proxy.EditTargetCommand.ExecuteAsync(null);

        Assert.Equal("Proxy 1", editor!.Name);
        Assert.Equal(("broker.example:8883", "TLS", "alice"), (proxy.TargetText, proxy.TargetSecurity, proxy.TargetLogin));
        Assert.Equal("secret", _credentials.Saved[Joktan.Core.CredentialStore.AccountFor("alice", "broker.example", "8883")]);
        Assert.Null(proxy.FollowsText);
    });

    [Fact]
    public Task A_target_linked_to_a_broker_follows_it_and_lets_go_when_it_goes() => Headless.Run(() =>
    {
        var mqtt = Mqtt();
        mqtt.SyncBrokers([Choice("b1", "1999")], []);
        var page = Page(mqtt);
        page.AddCommand.Execute(null);
        var proxy = page.Proxies[0];

        mqtt.SyncBrokers([Choice("b1", "2000")], []);
        Assert.Equal("127.0.0.1:2000", proxy.TargetText);

        mqtt.SyncBrokers([], []);
        Assert.Null(proxy.Profile.Target.BrokerId);
        Assert.Equal("127.0.0.1:2000", proxy.TargetText);
        Assert.Null(proxy.FollowsText);
    });

    [Fact]
    public Task Removing_a_proxy_stops_it_and_shows_the_next() => Headless.Run(async () =>
    {
        var page = Page();
        page.AddCommand.Execute(null);
        page.AddCommand.Execute(null);
        page.SelectedProxy = page.Proxies[0];

        await page.Proxies[0].RemoveCommand.ExecuteAsync(null);

        Assert.Equal(["Proxy 2"], page.Proxies.Select(p => p.Name));
        Assert.Same(page.Proxies[0], page.SelectedProxy);
        await page.Proxies[0].RemoveCommand.ExecuteAsync(null);
        Assert.Null(page.SelectedProxy);
        Assert.Empty(ProxySettings.Load(Data.At("proxies")).Proxies);
        page.Shutdown();
    });

    [Fact]
    public Task Browsing_for_a_folder_reads_it_and_starts_with_nothing_switched_on() => Headless.Run(async () =>
    {
        var folder = Folder();
        var page = Page(settings: new ProxySettings { Proxies = [new ProxyProfile { Name = "P", Selected = ["old.json"] }] });
        var proxy = page.Proxies[0];
        Assert.Equal("No folder chosen", proxy.FolderText);
        await proxy.BrowseFolderCommand.ExecuteAsync(null);

        page.PickFolder = () => Task.FromResult<string?>(folder);
        await proxy.BrowseFolderCommand.ExecuteAsync(null);

        Assert.Equal(folder, proxy.FolderText);
        Assert.Equal(3, proxy.Rows.Count);
        Assert.Empty(proxy.Profile.Selected);
        Assert.True(proxy.HasRows);

        Directory.Delete(folder, recursive: true);
        await proxy.ReloadCommand.ExecuteAsync(null);
        Assert.NotNull(proxy.FolderError);
        Assert.False(proxy.HasRows);
    });

    [Fact]
    public void Settings_that_are_missing_parts_load_with_defaults()
    {
        Data.Write("proxies/proxies.json", """{ "Proxies": [ null, { "Id": "x", "Name": "P", "Selected": null, "BrokerTopics": null, "Target": null } ] }""");

        var proxy = Assert.Single(ProxySettings.Load(Data.At("proxies")).Proxies);

        Assert.Empty(proxy.Selected);
        Assert.Empty(proxy.BrokerTopics);
        Assert.Equal("localhost", proxy.Target.Host);
        Data.Write("proxies/proxies.json", "{ broken");
        Assert.Empty(ProxySettings.Load(Data.At("proxies")).Proxies);
    }

    [Fact]
    public Task New_message_adds_one_to_the_proxys_folder_and_opens_its_settings() => Headless.Run(async () =>
    {
        var folder = Folder();
        var page = Page(settings: new ProxySettings { Proxies = [new ProxyProfile { Name = "P", Folder = folder }] });
        var proxy = page.Proxies[0];
        await Headless.Until(() => proxy.Rows.Count == 3);
        var opened = 0;
        page.EditSettings = _ =>
        {
            opened++;
            return Task.FromResult(true);
        };

        proxy.AddMessageCommand.Execute(null);
        proxy.NewMessage!.Topic = "plant/pressure";
        await proxy.NewMessage.CreateCommand.ExecuteAsync(null);

        Assert.False(proxy.HasNewMessage);
        Assert.Contains(proxy.Rows, r => r.Key == "plant/pressure/message.json");
        Assert.Equal(1, opened);
    });

    private (Joktan.Certificates.CertificateStore Store, Joktan.Certificates.StoredCertificate Root, Joktan.Certificates.StoredCertificate Server, Joktan.Certificates.StoredCertificate Client) Certificates()
    {
        var store = Joktan.Certificates.CertificateStore.Load(Data.At("certs"));
        Joktan.Certificates.CertificateSpec Spec(string name, Joktan.Certificates.CertificateKind kind, Joktan.Certificates.StoredCertificate? issuer = null, string names = "") => new()
        {
            Kind = kind, Name = name, CommonName = name, IssuerId = issuer?.Id, AlternativeNames = names, NotAfter = DateTimeOffset.UtcNow.AddYears(1),
        };
        var root = store.Create(Spec("root", Joktan.Certificates.CertificateKind.RootAuthority));
        var server = store.Create(Spec("localhost", Joktan.Certificates.CertificateKind.Server, root, "localhost"));
        var client = store.Create(Spec("device", Joktan.Certificates.CertificateKind.Client, root));
        return (store, root, server, client);
    }

    private static MqttViewModel Mqtt(Joktan.Certificates.CertificateStore store)
        => new(store, new MqttSettings { Clients = [new ClientProfile { Name = "a", Host = "localhost", ClientId = "a" }] }, new FakeCredentialStore());

    [Fact]
    public Task Choosing_certificates_fills_in_the_tls_ports_and_none_empties_them() => Headless.Run(() =>
    {
        var (store, root, server, _) = Certificates();
        var page = Page(Mqtt(store));
        page.AddCommand.Execute(null);
        page.AddCommand.Execute(null);
        var proxy = page.Proxies[1];
        page.Proxies[0].TlsPortText = "8885";

        Assert.Equal(["None", "localhost, signed by root"], proxy.ServerCertificates.Select(c => c.Name));
        Assert.Equal(["None: no mutual TLS", "The CAs that signed the server certificate", "root"], proxy.ClientAuthorities.Select(c => c.Name));
        Assert.True(proxy.SelectedClientAuthority!.IsNone);
        Assert.False(proxy.HasNoServerCertificates);

        // Ports another proxy uses are skipped.
        proxy.SelectedServerCertificate = proxy.ServerCertificates[1];
        Assert.Equal("8886", proxy.TlsPortText);
        proxy.SelectedClientAuthority = proxy.ClientAuthorities[2];
        Assert.Equal("8887", proxy.MutualTlsPortText);
        Assert.Equal("Devices connect to localhost: plain MQTT on 1886, TLS on 8886, mutual TLS on 8887", proxy.AddressText);
        var saved = ProxySettings.Load(Data.At("proxies")).Proxies[1];
        Assert.Equal((server.Id, root.Id, (int?)8886, (int?)8887), (saved.ServerCertificateId, saved.ClientAuthorityId, saved.TlsPort, saved.MutualTlsPort));

        // A port typed is not replaced, and none empties them.
        proxy.SelectedClientAuthority = proxy.ClientAuthorities[1];
        Assert.Equal("8887", proxy.MutualTlsPortText);
        proxy.SelectedServerCertificate = proxy.ServerCertificates[0];
        Assert.Equal(("", ""), (proxy.TlsPortText, proxy.MutualTlsPortText));
        Assert.True(proxy.SelectedClientAuthority!.IsNone);

        // Read again, it shows what was chosen; a certificate made later is offered at once.
        var again = Page(Mqtt(store), ProxySettings.Load(Data.At("proxies"))).Proxies[1];
        Assert.True(again.SelectedServerCertificate!.IsNone);
        store.Create(new Joktan.Certificates.CertificateSpec
        {
            Kind = Joktan.Certificates.CertificateKind.Server, Name = "second", CommonName = "second", IssuerId = root.Id,
            AlternativeNames = "localhost", NotAfter = DateTimeOffset.UtcNow.AddYears(1),
        });
        Assert.Equal(3, proxy.ServerCertificates.Count);
    });

    [Fact]
    public Task Started_with_tls_devices_connect_with_the_proxys_certificate() => Headless.Run(async () =>
    {
        var (store, root, server, client) = Certificates();
        await using var broker = await TestBroker.StartAsync();
        var tls = TestBroker.FreePort();
        var mutual = TestBroker.FreePort();
        var page = Page(Mqtt(store), new ProxySettings
        {
            Proxies =
            [
                new ProxyProfile
                {
                    Name = "P", ListenPort = null, TlsPort = tls, MutualTlsPort = mutual, ServerCertificateId = server.Id,
                    Target = new ClientProfile { Host = "127.0.0.1", Port = broker.Port.ToString(), ClientId = "proxy-tls" },
                },
            ],
        });
        var proxy = page.Proxies[0];
        Assert.Equal("Devices connect to localhost: TLS on " + tls + ", mutual TLS on " + mutual, proxy.AddressText);

        await proxy.StartAsync();
        Assert.True(proxy.IsRunning, proxy.Error);

        var device = new MqttConnection();
        await device.ConnectAsync(new BrokerEndpoint("localhost", mutual, "device", null, null, new TlsEndpoint(root.AuthorityPath, client.CertificatePath, client.KeyPath, null, false)));
        Assert.Equal(ConnectionState.Connected, device.State);
        await device.PublishAsync("over/mutual", Encoding.UTF8.GetBytes("1"), 0, false);
        await Headless.Until(() => broker.PublishedTopics.Contains("over/mutual"));
        await proxy.FlushAsync();
        Assert.Contains(proxy.Entries, e => e.Event == "Device connected" && e.Source == "device (mutual TLS)");

        await device.DisconnectAsync();
        await proxy.StopAsync();
    });

    [Fact]
    public Task Tls_without_a_server_certificate_does_not_start() => Headless.Run(async () =>
    {
        var (store, _, server, _) = Certificates();
        var page = Page(Mqtt(store), new ProxySettings { Proxies = [new ProxyProfile { Name = "P", TlsPort = TestBroker.FreePort() }] });

        await page.Proxies[0].StartAsync();
        Assert.False(page.Proxies[0].IsRunning);
        Assert.Equal("A TLS port needs a server certificate from Dev Tools / Certificates.", page.Proxies[0].Error);

        // Its files gone, it says it cannot be read.
        page.Proxies[0].SelectedServerCertificate = page.Proxies[0].ServerCertificates[1];
        File.Delete(server.KeyPath);
        await page.Proxies[0].StartAsync();
        Assert.Contains("cannot be read", page.Proxies[0].Error);
    });

    [Fact]
    public Task A_target_following_a_broker_with_a_login_keeps_its_password() => Headless.Run(() =>
    {
        var mqtt = Mqtt();
        var locked = new BrokerChoice("b1", "main", BrokerListener.Plain, "main · plain MQTT 1883",
            new ClientProfile { Host = "localhost", Port = "1883", Username = "main" }, "secret");
        mqtt.SyncBrokers([locked], []);
        var account = Joktan.Core.CredentialStore.AccountFor("main", "localhost", "1883");

        var page = Page(mqtt);
        page.AddCommand.Execute(null);

        Assert.Equal("main", page.Proxies[0].TargetLogin);
        Assert.Equal("secret", _credentials.Saved[account]);

        // One saved before the password was kept catches up when the page is made.
        _credentials.Saved.Clear();
        var again = Page(mqtt, ProxySettings.Load(Data.At("proxies")));
        Assert.Equal("secret", _credentials.Saved[account]);
        Assert.Equal(DeviceLogin.Broker, again.Proxies[0].SelectedDeviceLogin);
    });

    [Fact]
    public Task Devices_can_sign_in_with_a_login_of_the_proxys_own() => Headless.Run(async () =>
    {
        var page = Page();
        page.AddCommand.Execute(null);
        var proxy = page.Proxies[0];
        Assert.Equal(["Anyone can connect", "With the proxy's own login", "With a login the broker accepts"], proxy.DeviceLogins.Select(l => ProxyViewModel.DeviceLoginConverter.Convert(l, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.False(proxy.UsesOwnLogin);

        proxy.SelectedDeviceLogin = DeviceLogin.Own;
        Assert.True(proxy.UsesOwnLogin);

        // Without a password it does not start.
        proxy.DeviceUsername = " device ";
        await proxy.StartAsync();
        Assert.Equal("Set the username and password devices sign in to the proxy with.", proxy.Error);

        proxy.DevicePassword = "letmein";
        var account = proxy.Profile.DevicePasswordAccount;
        Assert.Equal("letmein", _credentials.Saved[account]);
        var saved = ProxySettings.Load(Data.At("proxies")).Proxies[0];
        Assert.Equal((DeviceLogin.Own, "device"), (saved.DeviceLogin, saved.DeviceUsername));
        Assert.DoesNotContain("letmein", File.ReadAllText(ProxySettings.PathIn(Data.At("proxies"))));
        Assert.Equal((DeviceLogin.Own, "device", "letmein"), (proxy.Engine.Login, proxy.Engine.Username, proxy.Engine.Password));

        // Read again, the password comes from the credential store; emptied, it goes.
        var again = Page(settings: ProxySettings.Load(Data.At("proxies"))).Proxies[0];
        Assert.Equal("letmein", again.DevicePassword);
        again.DevicePassword = "";
        Assert.False(_credentials.Saved.ContainsKey(account));

        _credentials.FailWrites = true;
        again.DevicePassword = "x";
        Assert.NotNull(again.Error);

        // Removing the proxy forgets its password.
        _credentials.FailWrites = false;
        proxy.DevicePassword = "again";
        await page.RemoveAsync(proxy);
        Assert.False(_credentials.Saved.ContainsKey(account));
    });

    [Fact]
    public Task A_target_following_a_broker_can_sign_in_with_a_login_of_its_own() => Headless.Run(async () =>
    {
        var mqtt = Mqtt();
        var main = new BrokerChoice("b1", "main", BrokerListener.Plain, "main · plain MQTT 1883",
            new ClientProfile { Host = "localhost", Port = "1883", Username = "main" }, "secret");
        mqtt.SyncBrokers([main], []);
        var page = Page(mqtt);
        page.AddCommand.Execute(null);
        var proxy = page.Proxies[0];

        // In the editor the broker's login is filled in; changing it makes it the target's own.
        ClientSettingsViewModel? editor = null;
        page.EditClientSettings = vm =>
        {
            editor = vm;
            Assert.True(vm.FollowsBroker);
            Assert.False(vm.HasOwnLogin);
            vm.Username = "proxy-user";
            vm.Password = "proxy-pass";
            Assert.True(vm.HasOwnLogin);
            return Task.FromResult(true);
        };
        await proxy.EditTargetCommand.ExecuteAsync(null);

        Assert.Equal("proxy-user", proxy.TargetLogin);
        Assert.True(proxy.Profile.Target.OwnLogin);
        Assert.Equal("b1", proxy.Profile.Target.BrokerId);
        Assert.Equal("proxy-pass", _credentials.Saved[Joktan.Core.CredentialStore.AccountFor("proxy-user", "localhost", "1883")]);

        // The broker moving takes the target with it, keeping its login, and its password with it.
        mqtt.SyncBrokers([main with { Connection = new ClientProfile { Host = "localhost", Port = "1999", Username = "main" } }], []);
        Assert.Equal(("localhost:1999", "proxy-user"), (proxy.TargetText, proxy.TargetLogin));
        Assert.Equal("proxy-pass", _credentials.Saved[Joktan.Core.CredentialStore.AccountFor("proxy-user", "localhost", "1999")]);

        // Read again, it keeps its own too.
        var again = Page(mqtt, ProxySettings.Load(Data.At("proxies"))).Proxies[0];
        Assert.Equal("proxy-user", again.TargetLogin);
    });
}
