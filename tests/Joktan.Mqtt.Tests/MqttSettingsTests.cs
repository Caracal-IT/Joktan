using System.IO;
using System.Linq;
using Joktan.Core;
using Joktan.Mqtt;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

[Collection(ProcessStateCollection.Name)]
public class MqttSettingsTests : DataFolderTest
{
    private string FilePath => Data.At("mqtt.json");

    [Fact]
    public void With_no_file_there_is_one_client_on_this_machine()
    {
        var settings = MqttSettings.Load();

        var client = Assert.Single(settings.Clients);
        Assert.Equal(MqttSettings.FirstClientName, client.Name);
        Assert.Equal("localhost", client.Host);
        Assert.Equal("1883", client.Port);
        Assert.Equal("#", client.ExplorerFilters);
        Assert.StartsWith("joktan-", client.ClientId);
    }

    [Fact]
    public void An_unreadable_file_counts_as_no_settings()
    {
        File.WriteAllText(FilePath, "not json");

        Assert.Equal(MqttSettings.FirstClientName, Assert.Single(MqttSettings.Load().Clients).Name);
    }

    [Fact]
    public void What_is_saved_is_loaded()
    {
        var settings = MqttSettings.Load();
        settings.SelectedClient = "other";
        settings.Clients[0].Host = "broker";
        settings.Clients[0].ExplorerCharts["a/b"] = ["x", "y"];
        settings.Clients.Add(new ClientProfile { Name = "other", ClientId = "id-2", UseTls = true });

        settings.Save();
        var loaded = MqttSettings.Load();

        Assert.Equal("other", loaded.SelectedClient);
        Assert.Equal(2, loaded.Clients.Count);
        Assert.Equal("broker", loaded.Clients[0].Host);
        Assert.Equal(settings.Clients[0].ClientId, loaded.Clients[0].ClientId);
        Assert.Equal(["x", "y"], loaded.Clients[0].ExplorerCharts["a/b"]);
        Assert.True(loaded.Clients[1].UseTls);
    }

    [Fact]
    public void Saving_makes_the_folder()
    {
        var folder = Data.At("deeper", "still");
        System.Environment.SetEnvironmentVariable(DataFolder.Variable, folder);

        MqttSettings.Load().Save();

        Assert.True(File.Exists(Path.Combine(folder, "mqtt.json")));
    }

    [Fact]
    public void The_single_broker_of_earlier_versions_moves_into_the_first_client()
    {
        File.WriteAllText(FilePath, """
            {"Host":"old","Port":"8883","Username":"me","ClientId":"id","Folder":"/f","ManualTopic":"t",
             "ManualBody":"b","ManualQos":2,"ManualRetain":true,"ExplorerFilters":"a/#"}
            """);

        var settings = MqttSettings.Load();

        var client = Assert.Single(settings.Clients);
        Assert.Equal("old", client.Host);
        Assert.Equal("8883", client.Port);
        Assert.Equal("me", client.Username);
        Assert.Equal("id", client.ClientId);
        Assert.Equal("/f", client.Folder);
        Assert.Equal("t", client.ManualTopic);
        Assert.Equal("b", client.ManualBody);
        Assert.Equal(2, client.ManualQos);
        Assert.True(client.ManualRetain);
        Assert.Equal("a/#", client.ExplorerFilters);
        Assert.Null(settings.Host);
        Assert.Null(settings.ManualQos);
    }

    [Fact]
    public void The_earlier_settings_are_not_written_again()
    {
        File.WriteAllText(FilePath, """{"Host":"old"}""");

        MqttSettings.Load().Save();

        var text = File.ReadAllText(FilePath);
        Assert.DoesNotContain("\"Host\": \"old\"", text.Split("\"Clients\"")[0]);
        Assert.DoesNotContain("ManualQos\": null", text);
    }

    [Fact]
    public void Earlier_settings_are_ignored_when_there_are_clients()
    {
        File.WriteAllText(FilePath, """{"Host":"old","Clients":[{"Name":"a","Host":"new","ClientId":"id"}]}""");

        var settings = MqttSettings.Load();

        Assert.Equal("new", Assert.Single(settings.Clients).Host);
    }

    [Fact]
    public void Empty_entries_are_dropped_and_each_client_gets_an_ID()
    {
        File.WriteAllText(FilePath, """{"Clients":[null,{"Name":"a"},{"Name":"b","ClientId":"keep"}]}""");

        var clients = MqttSettings.Load().Clients;

        Assert.Equal(["a", "b"], clients.Select(c => c.Name));
        Assert.StartsWith("joktan-", clients[0].ClientId);
        Assert.Equal("keep", clients[1].ClientId);
    }

    [Fact]
    public void Passwords_are_not_part_of_the_file()
    {
        MqttSettings.Load().Save();

        Assert.DoesNotContain("password", File.ReadAllText(FilePath), System.StringComparison.OrdinalIgnoreCase);
    }
}
