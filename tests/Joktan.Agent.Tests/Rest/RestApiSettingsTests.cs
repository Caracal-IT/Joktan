using System;
using System.IO;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Rest;
using Joktan.Core;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

[Collection(ProcessStateCollection.Name)]
public class RestApiSettingsTests : DataFolderTest
{
    private static McpTool Tool(string name, ToolRisk risk) => new(name, "", risk, [], _ => Task.FromResult<object?>(null));

    [Fact]
    public void Starts_off_on_this_computer_only()
    {
        var settings = RestApiSettings.Load();

        Assert.False(settings.Enabled);
        Assert.False(settings.FromOtherComputers);
        Assert.Equal(RestApiSettings.DefaultPort, settings.Port);
        Assert.Empty(settings.Keys);
    }

    [Fact]
    public void Calls_that_look_start_on_and_the_rest_start_off()
    {
        var settings = new RestApiSettings();

        Assert.True(settings.IsCallOn(Tool("notes_list", ToolRisk.Read)));
        Assert.False(settings.IsCallOn(Tool("notes_update", ToolRisk.Change)));
        Assert.False(settings.IsCallOn(Tool("notes_delete", ToolRisk.Risky)));
    }

    [Fact]
    public void A_tool_that_hands_out_a_secret_is_never_a_call()
    {
        var settings = new RestApiSettings();
        var reveal = Tool("passwords_reveal", ToolRisk.Secret);

        settings.SetCall(reveal, true);

        Assert.False(RestApiSettings.IsOffered(reveal));
        Assert.False(settings.IsCallOn(reveal));
    }

    [Fact]
    public void Keeps_only_the_calls_switched_away_from_how_they_start()
    {
        var settings = new RestApiSettings();
        var list = Tool("notes_list", ToolRisk.Read);
        var update = Tool("notes_update", ToolRisk.Change);

        settings.SetCall(list, false);
        settings.SetCall(update, true);
        Assert.Equal(2, settings.Calls.Count);
        Assert.False(settings.IsCallOn(list));
        Assert.True(settings.IsCallOn(update));

        settings.SetCall(list, true);
        settings.SetCall(update, false);
        Assert.Empty(settings.Calls);
    }

    [Fact]
    public void Saves_and_reads_back_without_the_secret()
    {
        var settings = new RestApiSettings { Enabled = true, Port = 50123, FromOtherComputers = true };
        var (key, secret) = ApiKey.Create("CI", new DateOnly(2027, 3, 1), DateTimeOffset.Now);
        settings.Keys.Add(key);
        settings.SetCall(Tool("notes_update", ToolRisk.Change), true);
        settings.Save();

        var file = File.ReadAllText(Path.Combine(DataFolder.For("settings"), "rest-api.json"));
        Assert.DoesNotContain(secret, file);
        Assert.Contains("2027-03-01", file);

        var read = RestApiSettings.Load();
        Assert.True(read.Enabled);
        Assert.Equal(50123, read.Port);
        Assert.True(read.FromOtherComputers);
        var readKey = Assert.Single(read.Keys);
        Assert.Equal(key.Hash, readKey.Hash);
        Assert.Equal(new DateOnly(2027, 3, 1), readKey.Expires);
        Assert.True(read.Calls["notes_update"]);
    }

    [Fact]
    public void Unreadable_settings_start_over_with_the_api_off()
    {
        Directory.CreateDirectory(DataFolder.For("settings"));
        File.WriteAllText(Path.Combine(DataFolder.For("settings"), "rest-api.json"), "{ not json");

        Assert.False(RestApiSettings.Load().Enabled);
    }

    [Theory]
    [InlineData(1023, false)]
    [InlineData(1024, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    public void Ports_are_from_1024_to_65535(int port, bool valid) => Assert.Equal(valid, RestApiSettings.IsValidPort(port));
}
