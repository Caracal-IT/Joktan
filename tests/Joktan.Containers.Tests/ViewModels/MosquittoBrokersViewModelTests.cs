using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Certificates;
using Joktan.Containers.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Containers.Tests.ViewModels;

public class MosquittoBrokersViewModelTests : FakeEngineTest
{
    private readonly FakeCredentialStore _credentials = new();
    private TestCertificates? _certificates;

    private TestCertificates Certificates => _certificates ??= new TestCertificates(Folder.At("certificates"));

    private string BrokersFolder => Folder.At("brokers");

    private string BrokersFile => Path.Combine(BrokersFolder, "docker-compose.yml");

    private ContainersViewModel BrokerPage(Action<ComposeSettings>? change = null)
    {
        var settings = new ComposeSettings { ComposeFiles = [], Engine = ContainerEngine.Docker, BrokersFolder = BrokersFolder };
        change?.Invoke(settings);
        settings.Save(DataFolder);
        return new ContainersViewModel(DataFolder, Certificates.Store, _credentials);
    }

    // The stand-in's answers for a brokers' project with one container.
    private void Running(string service, string state = "running")
    {
        Answer("services", $"{service}\n");
        Answer("ps", $$"""{"Service":"{{service}}","Name":"p-{{service}}-1","State":"{{state}}","Status":"Up 1 minute"}""" + "\n");
    }

    private static async Task<bool> Save(BrokerEditorViewModel editor)
    {
        var saved = await editor.SaveBrokerAsync();
        Headless.Settle();
        return saved;
    }

    [Fact]
    public Task Adding_a_broker_writes_its_file_and_puts_it_on_the_page() => Headless.Run(async () =>
    {
        var page = BrokerPage();
        var brokers = page.Brokers;
        Assert.False(brokers.HasBrokers);

        brokers.AddCommand.Execute(null);
        var editor = brokers.Editor!;
        Assert.True(editor.IsNew);
        Assert.Equal("mosquitto", editor.NameText);
        Assert.Equal("1883", editor.PortText);
        Assert.False(brokers.AddCommand.CanExecute(null));

        Assert.True(await Save(editor));

        Assert.Null(brokers.Editor);
        var row = Assert.Single(brokers.Items);
        Assert.Equal(("mosquitto", "1883", "Anyone can connect"), (row.Name, row.PortsText, row.Details));
        Assert.True(File.Exists(BrokersFile));
        Assert.Contains("  mosquitto:", File.ReadAllText(BrokersFile));
        Assert.Equal(BrokersFile, Assert.Single(page.Projects).File);
        Assert.Equal([BrokersFile], ComposeSettings.Load(DataFolder).ComposeFiles);
        Assert.Equal("mosquitto", Assert.Single(ComposeSettings.Load(DataFolder).Brokers).Name);
    });

    [Fact]
    public Task A_second_broker_gets_a_free_name_and_port() => Headless.Run(async () =>
    {
        var page = BrokerPage(s => s.Brokers = [new MosquittoBroker { Name = "mosquitto", Port = 1883 }]);

        page.Brokers.AddCommand.Execute(null);

        Assert.Equal(("mosquitto-2", "1884"), (page.Brokers.Editor!.NameText, page.Brokers.Editor.PortText));
        page.Brokers.Editor.CancelCommand.Execute(null);
        Assert.Null(page.Brokers.Editor);
        await Task.CompletedTask;
    });

    [Fact]
    public Task What_is_wrong_is_shown_and_nothing_is_saved() => Headless.Run(async () =>
    {
        var page = BrokerPage(s => s.Brokers = [new MosquittoBroker { Name = "taken", Port = 1883 }]);
        page.Brokers.AddCommand.Execute(null);
        var editor = page.Brokers.Editor!;

        editor.NameText = "taken";
        Assert.False(await Save(editor));
        Assert.Contains("taken", editor.Error);

        editor.NameText = "new";
        editor.PortText = "1883";
        Assert.False(await Save(editor));
        Assert.Contains("1883", editor.Error);

        editor.PortText = "99999";
        Assert.False(await Save(editor));

        editor.PortText = "1884";
        editor.TlsPortText = "8883";
        Assert.False(await Save(editor));
        Assert.Equal("Choose a server certificate for the TLS ports.", editor.Error);

        editor.TlsPortText = "";
        editor.Username = "alice";
        Assert.False(await Save(editor));
        Assert.Equal("Enter a password for the user.", editor.Error);

        Assert.Single(page.Brokers.Items);
        Assert.Same(editor, page.Brokers.Editor);
        Assert.False(File.Exists(BrokersFile));
    });

    [Fact]
    public Task A_login_keeps_its_password_in_the_credential_store() => Headless.Run(async () =>
    {
        var page = BrokerPage();
        page.Brokers.AddCommand.Execute(null);
        var editor = page.Brokers.Editor!;
        editor.Username = "alice";
        editor.Password = "secret";
        Assert.Equal("With a username, clients must sign in with it and this password. Without one, anyone can connect.", editor.PasswordHint);

        Assert.True(await Save(editor));

        var broker = Assert.Single(page.Brokers.Brokers);
        Assert.Equal("secret", _credentials.Saved[broker.Account]);
        Assert.DoesNotContain("secret", File.ReadAllText(ComposeSettings.PathIn(DataFolder)));
        Assert.True(MosquittoPassword.Matches(File.ReadAllText(Path.Combine(BrokersFolder, "mosquitto", "passwd")), "alice", "secret"));
        Assert.Equal("User alice", page.Brokers.Items[0].Details);

        // Editing again keeps the saved password unless another is typed.
        page.Brokers.Items[0].EditCommand.Execute(null);
        editor = page.Brokers.Editor!;
        Assert.True(editor.HasSavedPassword);
        Assert.Equal("", editor.Password);
        Assert.StartsWith("A password is saved", editor.PasswordHint);
        Assert.True(await Save(editor));
        Assert.Equal("secret", _credentials.Saved[broker.Account]);

        // Without a username the password is forgotten.
        page.Brokers.Items[0].EditCommand.Execute(null);
        page.Brokers.Editor!.Username = "";
        Assert.True(await Save(page.Brokers.Editor));
        Assert.Empty(_credentials.Saved);
        Assert.False(File.Exists(Path.Combine(BrokersFolder, "mosquitto", "passwd")));
    });

    [Fact]
    public Task A_password_that_cannot_be_kept_is_not_saved() => Headless.Run(async () =>
    {
        _credentials.FailWrites = true;
        var page = BrokerPage();
        page.Brokers.AddCommand.Execute(null);
        page.Brokers.Editor!.Username = "alice";
        page.Brokers.Editor.Password = "secret";

        Assert.False(await Save(page.Brokers.Editor));
        Assert.Equal("The password could not be kept in the credential store.", page.Brokers.Editor.Error);
        Assert.Empty(page.Brokers.Items);
    });

    [Fact]
    public Task Without_a_credential_store_a_login_cannot_be_given() => Headless.Run(async () =>
    {
        var credentials = new FakeCredentialStore(isAvailable: false);
        new ComposeSettings { ComposeFiles = [], Engine = ContainerEngine.Docker, BrokersFolder = BrokersFolder }.Save(DataFolder);
        var page = new ContainersViewModel(DataFolder, Certificates.Store, credentials);
        page.Brokers.AddCommand.Execute(null);
        page.Brokers.Editor!.Username = "alice";
        page.Brokers.Editor.Password = "secret";

        Assert.False(await Save(page.Brokers.Editor));
        Assert.Equal("This computer has no credential store to keep the password in.", page.Brokers.Editor.Error);
    });

    [Fact]
    public Task Certificates_are_chosen_from_the_certificates_page() => Headless.Run(async () =>
    {
        var page = BrokerPage();
        page.Brokers.AddCommand.Execute(null);
        var editor = page.Brokers.Editor!;

        Assert.Equal(["None", "localhost, signed by root"], editor.ServerCertificates.Select(c => c.Name));
        Assert.Equal(["None: no mutual TLS", "The CAs that signed the server certificate", "root", "other ca"], editor.ClientAuthorities.Select(c => c.Name));
        Assert.True(editor.SelectedClientAuthority!.IsNone);
        Assert.False(editor.HasNoServerCertificates);
        Assert.False(editor.ChooseServerCertificate("root"));
        Assert.True(editor.ChooseServerCertificate("LOCALHOST"));
        Assert.True(editor.ChooseClientAuthority("other ca"));
        editor.PortText = "";
        editor.TlsPortText = "";

        Assert.True(await Save(editor));

        var broker = Assert.Single(page.Brokers.Brokers);
        Assert.Equal((Certificates.Server.Id, Certificates.Other.Id), (broker.ServerCertificateId, broker.ClientAuthorityId));
        Assert.Equal("Anyone can connect · Certificate localhost", page.Brokers.Items[0].Details);
        Assert.Equal(File.ReadAllText(Certificates.Other.AuthorityPath), File.ReadAllText(Path.Combine(BrokersFolder, "mosquitto", "certs", "ca.pem")));

        // A certificate made later is offered straight away, and what was chosen stays chosen.
        page.Brokers.Items[0].EditCommand.Execute(null);
        editor = page.Brokers.Editor!;
        Assert.Equal(Certificates.Server.Id, editor.SelectedServerCertificate!.Id);
        Certificates.Store.Create(new CertificateSpec
        {
            Kind = CertificateKind.Server, Name = "second", CommonName = "second", IssuerId = Certificates.Root.Id,
            AlternativeNames = "localhost", NotAfter = DateTimeOffset.UtcNow.AddYears(1),
        });
        Assert.Equal(3, editor.ServerCertificates.Count);
        Assert.Equal(Certificates.Server.Id, editor.SelectedServerCertificate!.Id);
    });

    [Fact]
    public Task A_deleted_certificate_shows_as_missing() => Headless.Run(async () =>
    {
        var page = BrokerPage(s => s.Brokers = [new MosquittoBroker { Name = "tls", TlsPort = 8883, ServerCertificateId = Certificates.Server.Id }]);

        Certificates.Store.Delete(Certificates.Server);

        Assert.Equal("Anyone can connect · Certificate missing", page.Brokers.Items[0].Details);
        Assert.NotNull(page.Brokers.Write());
        await Task.CompletedTask;
    });

    [Fact]
    public Task Saving_a_running_broker_starts_it_again() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "mosquitto", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        Running("mosquitto");
        await page.Projects[0].RefreshStatusAsync();

        page.Brokers.Items[0].EditCommand.Execute(null);
        page.Brokers.Editor!.PortText = "1999";
        Assert.True(await Save(page.Brokers.Editor));

        Assert.Contains(" up -d mosquitto\n", Calls);
        Assert.Contains("\"1999:1883\"", File.ReadAllText(BrokersFile));
    });

    [Fact]
    public Task Renaming_a_broker_removes_its_old_container() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "old", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        Running("old", "exited");
        await page.Projects[0].RefreshStatusAsync();

        page.Brokers.Items[0].EditCommand.Execute(null);
        page.Brokers.Editor!.NameText = "new";
        Assert.True(await Save(page.Brokers.Editor));

        Assert.Contains(" rm --stop --force old\n", Calls);
        Assert.DoesNotContain("up -d", Calls);
        Assert.False(Directory.Exists(Path.Combine(BrokersFolder, "old")));
        Assert.True(File.Exists(Path.Combine(BrokersFolder, "new", "mosquitto.conf")));
    });

    [Fact]
    public Task A_rename_stops_when_the_old_container_cannot_be_removed() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "old", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        Running("old");
        await page.Projects[0].RefreshStatusAsync();
        Answer("command-exit", "3");

        page.Brokers.Items[0].EditCommand.Execute(null);
        page.Brokers.Editor!.NameText = "new";

        Assert.False(await Save(page.Brokers.Editor));
        Assert.Contains("exit code 3", page.Brokers.Editor.Error);
        Assert.Equal("old", page.Brokers.Brokers[0].Name);
    });

    [Fact]
    public Task Removing_a_broker_removes_its_container_password_and_folder() => Headless.Run(async () =>
    {
        var keep = new MosquittoBroker { Name = "keep", Port = 1883 };
        var gone = new MosquittoBroker { Name = "gone", Port = 1884, Username = "alice" };
        _credentials.Saved[gone.Account] = "secret";
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [keep, gone];
        });
        Assert.Null(page.Brokers.Write());
        Running("gone");
        await page.Projects[0].RefreshStatusAsync();
        var row = page.Brokers.Items.Single(r => r.Name == "gone");

        row.RemoveCommand.Execute(null);
        Assert.True(row.IsConfirmingRemove);
        Assert.Contains("gone", row.RemoveText);
        row.CancelRemoveCommand.Execute(null);
        Assert.False(row.IsConfirmingRemove);

        await row.ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Contains(" rm --stop --force gone\n", Calls);
        Assert.Empty(_credentials.Saved);
        Assert.Equal(["keep"], page.Brokers.Items.Select(r => r.Name));
        Assert.Equal(["keep"], ComposeSettings.Load(DataFolder).Brokers.Select(b => b.Name));
        Assert.False(Directory.Exists(Path.Combine(BrokersFolder, "gone")));
        Assert.DoesNotContain("gone", File.ReadAllText(BrokersFile));
    });

    [Fact]
    public Task Removing_the_last_broker_takes_its_file_off_the_page() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "only", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        Answer("services", "only\n");
        Answer("ps");
        await page.Projects[0].RefreshStatusAsync();

        await page.Brokers.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.DoesNotContain("rm --stop", Calls);
        Assert.Empty(page.Projects);
        Assert.False(File.Exists(BrokersFile));
        Assert.Empty(ComposeSettings.Load(DataFolder).ComposeFiles!);
        Assert.Null(page.Brokers.Error);
    });

    [Fact]
    public Task A_container_that_cannot_be_removed_keeps_its_broker() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "only", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        Running("only");
        await page.Projects[0].RefreshStatusAsync();
        Answer("command-exit", "1");

        await page.Brokers.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);

        Assert.Contains("exit code 1", page.Brokers.Error);
        Assert.Single(page.Brokers.Items);
    });

    [Fact]
    public Task Starting_the_brokers_writes_their_files_first() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "b", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        File.Delete(Path.Combine(BrokersFolder, "b", "mosquitto.conf"));

        await page.Projects[0].UpCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(BrokersFolder, "b", "mosquitto.conf")));
        Assert.Contains(" up -d\n", Calls);
    });

    [Fact]
    public Task Brokers_that_cannot_be_written_are_not_started() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "tls", TlsPort = 8883, ServerCertificateId = Certificates.Server.Id }];
        });
        Assert.Null(page.Brokers.Write());
        Certificates.Store.Delete(Certificates.Server);

        await page.Projects[0].UpCommand.ExecuteAsync(null);

        Assert.StartsWith("tls: ", page.Projects[0].Error);
        Assert.DoesNotContain("up -d", Calls);

        // Other files and other commands are not held up.
        await page.Projects[0].StopCommand.ExecuteAsync(null);
        Assert.Contains(" stop\n", Calls);
    });

    [Fact]
    public Task The_image_is_saved_once_valid() => Headless.Run(async () =>
    {
        var page = BrokerPage();

        page.Brokers.ImageText = " ";
        Assert.NotNull(page.Brokers.ImageError);
        Assert.Equal(MosquittoProject.DefaultImage, ComposeSettings.Load(DataFolder).MosquittoImage);

        page.Brokers.ImageText = "docker.io/library/eclipse-mosquitto:2.0.20";
        Assert.Null(page.Brokers.ImageError);
        Assert.Equal("docker.io/library/eclipse-mosquitto:2.0.20", ComposeSettings.Load(DataFolder).MosquittoImage);
        await Task.CompletedTask;
    });

    [Fact]
    public Task Moving_the_brokers_writes_them_to_the_new_folder_and_name() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "b", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        Answer("services", "b\n");
        Answer("ps");
        var brokers = page.Brokers;
        Assert.False(brokers.ApplyLocationCommand.CanExecute(null));

        brokers.ProjectText = "Bad";
        Assert.NotNull(brokers.LocationError);
        brokers.ProjectText = "moved";
        brokers.FolderText = Folder.At("elsewhere");
        Assert.Null(brokers.LocationError);
        Assert.True(brokers.HasLocationChanges);

        await brokers.ApplyLocationCommand.ExecuteAsync(null);

        var moved = Path.Combine(Folder.At("elsewhere"), "docker-compose.yml");
        Assert.Null(brokers.LocationError);
        Assert.False(brokers.HasLocationChanges);
        Assert.Equal(moved, brokers.ComposeFile);
        Assert.Contains("name: moved", File.ReadAllText(moved));
        Assert.False(Directory.Exists(BrokersFolder));
        Assert.Equal([moved], page.Projects.Select(p => p.File));
        var saved = ComposeSettings.Load(DataFolder);
        Assert.Equal(("moved", Folder.At("elsewhere")), (saved.BrokersProject, saved.BrokersFolder));
    });

    [Fact]
    public Task The_brokers_are_not_moved_while_they_have_containers() => Headless.Run(async () =>
    {
        var page = BrokerPage(s =>
        {
            s.ComposeFiles = [BrokersFile];
            s.Brokers = [new MosquittoBroker { Name = "b", Port = 1883 }];
        });
        Assert.Null(page.Brokers.Write());
        Running("b", "exited");

        page.Brokers.ProjectText = "moved";
        await page.Brokers.ApplyLocationCommand.ExecuteAsync(null);

        Assert.StartsWith("Take ", page.Brokers.LocationError);
        Assert.Equal(MosquittoProject.DefaultName, ComposeSettings.Load(DataFolder).BrokersProject);
        Assert.True(File.Exists(BrokersFile));
    });

    [Fact]
    public Task The_folder_can_be_browsed_for() => Headless.Run(async () =>
    {
        var page = BrokerPage();
        await page.Brokers.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal(BrokersFolder, page.Brokers.FolderText);

        page.Brokers.PickFolder = () => Task.FromResult<string?>(Folder.At("picked"));
        await page.Brokers.BrowseFolderCommand.ExecuteAsync(null);
        Assert.Equal(Folder.At("picked"), page.Brokers.FolderText);

        // Moving with no brokers only remembers where they go.
        await page.Brokers.ApplyLocationCommand.ExecuteAsync(null);
        Assert.Equal(Folder.At("picked"), ComposeSettings.Load(DataFolder).BrokersFolder);
        Assert.Empty(page.Projects);
    });

    [Fact]
    public Task Settings_opens_settings() => Headless.Run(() =>
    {
        var page = BrokerPage();
        var opened = false;
        page.ShowSettingsCommand.Execute(null);
        page.OpenSettings = () => opened = true;
        page.ShowSettingsCommand.Execute(null);

        Assert.True(opened);
        Assert.Same(page, new ContainerSettingsViewModel(page).Containers);
        Assert.Same(page.Brokers, new ContainerSettingsViewModel(page).Brokers);
        Assert.Equal("Settings / Container Settings", new ContainerSettingsViewModel(page).Subtitle);
    });

    [Fact]
    public Task Choosing_certificates_fills_in_the_tls_ports_and_none_empties_them() => Headless.Run(() =>
    {
        var page = BrokerPage();
        page.Brokers.AddCommand.Execute(null);
        var editor = page.Brokers.Editor!;
        Assert.Equal(("", ""), (editor.TlsPortText, editor.MutualTlsPortText));

        editor.ChooseServerCertificate("localhost");
        Assert.Equal(("8883", ""), (editor.TlsPortText, editor.MutualTlsPortText));

        editor.SelectedClientAuthority = editor.ClientAuthorities[1];
        Assert.Equal("8884", editor.MutualTlsPortText);

        // A port typed is not replaced.
        editor.MutualTlsPortText = "9999";
        editor.ChooseClientAuthority("other ca");
        Assert.Equal("9999", editor.MutualTlsPortText);

        editor.SelectedClientAuthority = editor.ClientAuthorities[0];
        Assert.Equal("", editor.MutualTlsPortText);

        editor.SelectedClientAuthority = editor.ClientAuthorities[1];
        editor.SelectedServerCertificate = editor.ServerCertificates[0];
        Assert.Equal(("", ""), (editor.TlsPortText, editor.MutualTlsPortText));
        Assert.True(editor.SelectedClientAuthority!.IsNone);
    });

    [Fact]
    public Task Ports_other_brokers_use_are_skipped() => Headless.Run(() =>
    {
        var page = BrokerPage(s => s.Brokers =
        [
            new MosquittoBroker { Name = "first", Port = 1883, TlsPort = 8883, MutualTlsPort = 8884, ServerCertificateId = Certificates.Server.Id },
        ]);
        page.Brokers.AddCommand.Execute(null);
        var editor = page.Brokers.Editor!;

        Assert.Equal("1884", editor.PortText);
        editor.ChooseServerCertificate("localhost");
        Assert.Equal("8885", editor.TlsPortText);
        editor.ChooseClientAuthority("root");
        Assert.Equal("8886", editor.MutualTlsPortText);
        editor.CancelCommand.Execute(null);

        // A broker being changed keeps its own ports.
        page.Brokers.Items[0].EditCommand.Execute(null);
        editor = page.Brokers.Editor!;
        Assert.Equal(("1883", "8883", "8884"), (editor.PortText, editor.TlsPortText, editor.MutualTlsPortText));
        Assert.Equal("The CAs that signed the server certificate", editor.SelectedClientAuthority!.Name);
        editor.TlsPortText = "";
        editor.ChooseServerCertificate("localhost");
        Assert.Equal("", editor.TlsPortText);
        editor.SelectedServerCertificate = editor.ServerCertificates[1];
        Assert.Equal("", editor.TlsPortText);
        editor.SelectedServerCertificate = editor.ServerCertificates[0];
        editor.SelectedServerCertificate = editor.ServerCertificates[1];
        Assert.Equal("8883", editor.TlsPortText);
    });

    [Fact]
    public Task Saving_and_removing_are_announced_for_the_mqtt_page() => Headless.Run(async () =>
    {
        var page = BrokerPage();
        var saved = new System.Collections.Generic.List<(string, string?)>();
        var removed = new System.Collections.Generic.List<string>();
        page.Brokers.Saved += b => saved.Add((b.Name, page.Brokers.PasswordOf(b)));
        page.Brokers.Removed += b => removed.Add(b.Name);

        page.Brokers.AddCommand.Execute(null);
        page.Brokers.Editor!.Username = "alice";
        page.Brokers.Editor.Password = "secret";
        Assert.True(await Save(page.Brokers.Editor));
        Assert.Equal([("mosquitto", "secret")], saved);

        await page.Brokers.Items[0].ConfirmRemoveCommand.ExecuteAsync(null);
        Assert.Equal(["mosquitto"], removed);
    });

    [Fact]
    public Task A_broker_is_started_and_stopped_from_its_row_which_shows_its_state() => Headless.Run(async () =>
    {
        var page = BrokerPage(s => s.Brokers = [new MosquittoBroker { Name = "b", Port = 1883 }]);
        var row = page.Brokers.Items[0];
        Assert.Equal(("Not created", true, false), (row.StatusText, row.CanStart, row.IsRunning));

        // Not on the page yet, its file is written and added first.
        Running("b");
        await row.StartCommand.ExecuteAsync(null);

        Assert.Contains($"-f {BrokersFile} up -d b\n", Calls);
        Assert.Equal(BrokersFile, Assert.Single(page.Projects).File);
        Assert.Equal(("Up 1 minute", false, true), (row.StatusText, row.CanStart, row.IsRunning));

        Running("b", "exited");
        await row.StopCommand.ExecuteAsync(null);

        Assert.Contains(" stop b\n", Calls);
        Assert.True(row.IsStopped);
        Assert.True(row.CanStart);
        Assert.Null(page.Brokers.Error);
    });

    [Fact]
    public Task A_broker_that_will_not_start_says_why() => Headless.Run(async () =>
    {
        var page = BrokerPage(s => s.Brokers = [new MosquittoBroker { Name = "tls", TlsPort = 8883, ServerCertificateId = "gone" }]);

        await page.Brokers.Items[0].StartCommand.ExecuteAsync(null);
        Assert.StartsWith("tls: ", page.Brokers.Error);

        Assert.Empty(page.Projects);

        // Once it can be written, a command that fails says so too.
        Answer("command-exit", "2");
        page.Brokers.Items[0].Broker.TlsPort = null;
        page.Brokers.Items[0].Broker.Port = 1883;
        await page.Brokers.Items[0].StartCommand.ExecuteAsync(null);
        Assert.Contains("exit code 2", page.Brokers.Error);
        await page.Brokers.Items[0].StopCommand.ExecuteAsync(null);
        Assert.Contains("exit code 2", page.Brokers.Error);
    });

    [Fact]
    public Task Each_row_shows_how_its_mqtt_client_is_doing() => Headless.Run(() =>
    {
        var page = BrokerPage(s => s.Brokers = [new MosquittoBroker { Name = "b", Port = 1883 }]);
        var row = page.Brokers.Items[0];
        Assert.Equal(("No MQTT client", false), (row.ConnectionText, row.IsConnected));

        var changed = 0;
        row.PropertyChanged += (_, e) => changed += e.PropertyName == nameof(MosquittoBrokerRow.ConnectionText) ? 1 : 0;
        var connection = new BrokerConnection("Connecting", false, IsPending: true);
        page.Brokers.ConnectionOf = _ => connection;
        page.Brokers.ConnectionsChanged();
        Assert.Equal(("MQTT: Connecting", true, false), (row.ConnectionText, row.IsConnecting, row.IsConnected));

        connection = new BrokerConnection("Connected", true);
        page.Brokers.ConnectionsChanged();
        Assert.True(row.IsConnected);
        connection = new BrokerConnection("Failed", false, IsFailed: true);
        page.Brokers.ConnectionsChanged();
        Assert.True(row.IsConnectionFailed);
        Assert.Equal(3, changed);
    });
}
