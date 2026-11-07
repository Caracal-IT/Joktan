using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Rest;
using Joktan.Agent.ViewModels;
using Joktan.Api;
using Joktan.Api.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Agent.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class RestApiSettingsViewModelTests : DataFolderTest
{
    // One client for the class, as HttpClient is meant to be shared.
    private static readonly HttpClient Http = new();

    private static readonly DateOnly Today = new(2026, 11, 7);

    private int _toolsMade;

    private IReadOnlyList<McpTool> Tools()
    {
        _toolsMade++;
        return
        [
            In("Notes", McpTool.Sync("notes_list", "Lists the notes.", ToolRisk.Read, [], _ => "two notes")),
            In("Notes", McpTool.Sync("notes_update", "Changes a note.", ToolRisk.Change, [], _ => "updated")),
            In("Notes", McpTool.Sync("notes_delete", "Deletes a note.", ToolRisk.Risky, [], _ => "deleted")),
            In("MQTT", McpTool.Sync("mqtt_clients", "Lists the MQTT clients.", ToolRisk.Read, [], _ => "none")),
            In("Passwords", McpTool.Sync("passwords_reveal", "Reveals a password.", ToolRisk.Secret, [], _ => "secret")),
        ];
    }

    private static McpTool In(string category, McpTool tool)
    {
        tool.Category = category;
        return tool;
    }

    private RestApiSettingsViewModel Page(RestApiSettings? settings = null) => new(settings ?? new RestApiSettings(), Tools, today: () => Today);

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    [Fact]
    public void Makes_no_tools_until_the_calls_are_shown()
    {
        using var page = Page();
        Assert.Equal(0, _toolsMade);

        Assert.Equal(["notes_list", "notes_update", "notes_delete", "mqtt_clients"], page.Calls.Select(c => c.Name));
        Assert.Equal(1, _toolsMade);
    }

    [Fact]
    public void Adds_a_key_and_shows_its_secret_until_dismissed()
    {
        using var page = Page();
        page.NewKeyName = "  Build server ";
        page.NewKeyExpires = new DateTime(2026, 12, 31);

        page.AddKeyCommand.Execute(null);

        var key = Assert.Single(page.Keys);
        Assert.Equal("Build server", key.Name);
        Assert.Equal(new DateOnly(2026, 12, 31), key.Key.Expires);
        Assert.Contains("2026-12-31", key.ExpiresText);
        Assert.NotNull(page.NewSecret);
        Assert.True(key.Key.Matches(ApiKey.HashOf(page.NewSecret!)));
        Assert.Equal("", page.NewKeyName);
        Assert.Null(page.NewKeyExpires);
        Assert.Single(RestApiSettings.Load().Keys);

        page.DismissSecretCommand.Execute(null);
        Assert.False(page.HasNewSecret);
    }

    [Fact]
    public void A_key_without_a_date_never_expires()
    {
        using var page = Page();
        page.NewKeyName = "CI";

        page.AddKeyCommand.Execute(null);

        var key = Assert.Single(page.Keys);
        Assert.Null(key.Key.Expires);
        Assert.False(key.IsExpired);
    }

    [Fact]
    public void Needs_a_name_not_already_in_use()
    {
        using var page = Page();
        Assert.False(page.AddKeyCommand.CanExecute(null));

        page.NewKeyName = "CI";
        page.AddKeyCommand.Execute(null);
        page.NewKeyName = "ci";

        Assert.False(page.AddKeyCommand.CanExecute(null));
        Assert.Contains("ci", page.NewKeyProblem);
    }

    [Fact]
    public void Will_not_make_a_key_that_has_already_expired()
    {
        using var page = Page();
        page.NewKeyName = "CI";
        page.NewKeyExpires = new DateTime(2026, 11, 6);
        Assert.False(page.AddKeyCommand.CanExecute(null));
        Assert.NotNull(page.NewKeyProblem);

        page.NewKeyExpires = new DateTime(2026, 11, 7);
        Assert.True(page.AddKeyCommand.CanExecute(null));
    }

    [Fact]
    public void Shows_a_key_past_its_date_as_expired()
    {
        var settings = new RestApiSettings();
        var (key, _) = ApiKey.Create("Old", new DateOnly(2026, 1, 31), DateTimeOffset.Now);
        settings.Keys.Add(key);
        using var page = Page(settings);

        var row = Assert.Single(page.Keys);
        Assert.True(row.IsExpired);
        Assert.Contains("2026-01-31", row.ExpiresText);
    }

    [Fact]
    public void Turning_a_key_off_and_removing_it_are_saved()
    {
        using var page = Page();
        page.NewKeyName = "CI";
        page.AddKeyCommand.Execute(null);
        var row = Assert.Single(page.Keys);

        row.IsEnabled = false;
        Assert.False(Assert.Single(RestApiSettings.Load().Keys).Enabled);

        page.RemoveKeyCommand.Execute(row);
        Assert.Empty(page.Keys);
        Assert.True(page.HasNoKeys);
        Assert.Empty(RestApiSettings.Load().Keys);
    }

    [Fact]
    public void Switching_calls_is_saved()
    {
        using var page = Page();
        var update = page.Calls.Single(c => c.Name == "notes_update");
        Assert.False(update.IsOn);
        Assert.True(update.Changes);

        update.IsOn = true;

        Assert.True(RestApiSettings.Load().Calls["notes_update"]);
    }

    [Fact]
    public void Filters_the_calls_and_turns_those_listed_on_or_off()
    {
        using var page = Page();
        page.CallFilter = "DELETES";
        Assert.Equal("notes_delete", Assert.Single(page.Calls).Name);

        page.TurnListedCallsOnCommand.Execute(null);
        page.CallFilter = "";

        Assert.Equal([true, false, true, true], page.Calls.Select(c => c.IsOn));

        page.TurnListedCallsOffCommand.Execute(null);
        Assert.All(page.Calls, c => Assert.False(c.IsOn));
    }

    [Fact]
    public void Gives_a_reason_for_a_port_it_cannot_use()
    {
        using var page = Page();

        page.PortText = "80";
        Assert.NotNull(page.PortError);
        Assert.Equal(RestApiSettings.DefaultPort, RestApiSettings.Load().Port);

        page.PortText = "50000";
        Assert.Null(page.PortError);
        Assert.Equal(50000, RestApiSettings.Load().Port);
        Assert.Contains(":50000/api", page.Url);
    }

    [Fact]
    public void Says_so_when_the_port_is_taken()
    {
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            using var page = Page(new RestApiSettings { Port = ((IPEndPoint)taken.LocalEndpoint).Port });

            page.Enabled = true;

            Assert.False(page.IsListening);
            Assert.NotNull(page.ServerProblem);
        }
        finally
        {
            taken.Stop();
        }
    }

    [Fact]
    public Task Answers_calls_while_on_and_stops_when_turned_off() => Headless.Run(async () =>
    {
        using var page = Page(new RestApiSettings { Port = FreePort() });
        page.NewKeyName = "CI";
        page.AddKeyCommand.Execute(null);
        var secret = page.NewSecret!;
        Assert.False(page.IsListening);

        page.Enabled = true;
        Assert.True(page.IsListening);
        Assert.Null(page.ServerProblem);

        using var message = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{page.ListeningPort}/api/notes_list");
        message.Headers.Authorization = new("Bearer", secret);
        var sending = Http.SendAsync(message);
        await Headless.Until(() => sending.IsCompleted);
        using var response = await sending;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("two notes", await response.Content.ReadAsStringAsync());

        var port = page.ListeningPort;
        page.Enabled = false;
        Assert.False(page.IsListening);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Http.GetAsync($"http://127.0.0.1:{port}/api", TestContext.Current.CancellationToken));
    });

    [Fact]
    public void Starts_with_the_app_when_it_was_left_on()
    {
        using var page = Page(new RestApiSettings { Enabled = true, Port = FreePort() });
        Assert.False(page.IsListening);

        page.Start();

        Assert.True(page.IsListening);
    }

    [Fact]
    public void Groups_the_calls_by_category_in_a_tree()
    {
        using var page = Page();

        Assert.Equal(["Notes", "MQTT"], page.Categories.Select(c => c.Name));
        Assert.Equal(["notes_list", "notes_update", "notes_delete"], page.Categories[0].Calls.Select(c => c.Name));
        Assert.Equal("1 of 3 on", page.Categories[0].Summary);
        Assert.Null(page.Categories[0].IsOn);
        Assert.True(page.Categories[1].IsOn);
    }

    [Fact]
    public void A_category_s_switch_turns_all_its_calls_on_or_off()
    {
        using var page = Page();
        var notes = page.Categories[0];

        notes.IsOn = true;
        Assert.All(notes.Calls, c => Assert.True(c.IsOn));
        Assert.Equal("3 of 3 on", notes.Summary);

        notes.IsOn = false;
        Assert.All(notes.Calls, c => Assert.False(c.IsOn));
        Assert.False(notes.IsOn);
        Assert.True(page.Categories[1].IsOn);
    }

    [Fact]
    public void A_category_follows_its_calls()
    {
        using var page = Page();
        var mqtt = page.Categories[1];
        var changed = 0;
        mqtt.PropertyChanged += (_, e) => changed += e.PropertyName == nameof(RestCategoryViewModel.IsOn) ? 1 : 0;

        mqtt.Calls[0].IsOn = false;

        Assert.False(mqtt.IsOn);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Filtering_hides_empty_categories_and_opens_the_rest()
    {
        using var page = Page();
        Assert.All(page.Categories, c => Assert.False(c.IsExpanded));

        page.CallFilter = "mqtt";
        var mqtt = Assert.Single(page.Categories);
        Assert.Equal("MQTT", mqtt.Name);
        Assert.True(mqtt.IsExpanded);

        page.CallFilter = "nothing like it";
        Assert.True(page.HasNoCallsListed);

        page.CallFilter = "";
        Assert.Equal(2, page.Categories.Count);
        Assert.Equal(3, page.Categories[0].Calls.Count);
    }

    [Fact]
    public void A_category_s_switch_turns_only_the_calls_listed()
    {
        using var page = Page();
        page.CallFilter = "deletes";

        page.Categories.Single().IsOn = true;
        page.CallFilter = "";

        Assert.Equal([true, false, true], page.Categories[0].Calls.Select(c => c.IsOn));
    }

    [Fact]
    public Task Adds_every_call_to_the_api_client_in_folders_by_category() => Headless.Run(() =>
    {
        var workspace = Data.At("requests");
        Directory.CreateDirectory(workspace);
        var client = new ApiClientViewModel(new ApiWorkspace(workspace));
        using var page = new RestApiSettingsViewModel(new RestApiSettings { Port = 50000 }, Tools, client, () => Today);
        Assert.True(page.CanAddToApiClient);

        page.AddToApiClientCommand.Execute(null);

        Assert.True(File.Exists(Path.Combine(workspace, "Joktan REST API", "Notes", "notes_delete" + ApiWorkspace.RequestExtension)));
        Assert.True(File.Exists(Path.Combine(workspace, "Joktan REST API", "MQTT", "mqtt_clients" + ApiWorkspace.RequestExtension)));
        Assert.False(File.Exists(Path.Combine(workspace, "Joktan REST API", "Passwords", "passwords_reveal" + ApiWorkspace.RequestExtension)));
        Assert.Contains("4", page.ApiClientMessage);
        Assert.Equal("http://localhost:50000/api", client.Environments.Single().Variables.Single(v => v.Name == "joktanUrl").Value);

        page.AddToApiClientCommand.Execute(null);
        Assert.Contains("kept 4", page.ApiClientMessage);
    });

    [Fact]
    public void Shows_where_the_docs_are_while_listening_with_them_on()
    {
        using var page = Page(new RestApiSettings { Enabled = true, Port = FreePort() });
        Assert.False(page.ShowsDocs);

        page.Start();
        Assert.True(page.ShowsDocs);
        Assert.EndsWith("/api/docs", page.DocsUrl);

        page.SwaggerEnabled = false;
        Assert.False(page.ShowsDocs);
        Assert.False(RestApiSettings.Load().SwaggerEnabled);
    }
}
