using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Controls;
using Joktan.Agent.Mcp;
using Joktan.Agent.Rest;
using Joktan.Agent.ViewModels;
using Joktan.Agent.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Agent.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class RestApiSettingsViewTests : DataFolderTest
{
    private static McpTool In(string category, McpTool tool)
    {
        tool.Category = category;
        return tool;
    }

    private static RestApiSettingsViewModel Page() => new(new RestApiSettings(), () =>
    [
        In("Notes", McpTool.Sync("notes_list", "Lists the notes.", ToolRisk.Read, [], _ => "two notes")),
        In("Notes", McpTool.Sync("notes_delete", "Deletes a note.", ToolRisk.Risky, [], _ => "deleted")),
        In("MQTT", McpTool.Sync("mqtt_clients", "Lists the MQTT clients.", ToolRisk.Read, [], _ => "none")),
    ]);

    [Fact]
    public Task Lists_the_calls_and_shows_a_new_key_once() => Headless.Run(() =>
    {
        using var page = Page();
        var window = Headless.Show(new RestApiSettingsView(), page);

        var tree = Headless.Find<TreeView>(window, t => t.Name == "CallTree");
        Assert.Equal(2, tree.ItemCount);
        Assert.DoesNotContain(Headless.All<TextBlock>(tree), t => t.Text == "POST /api/notes_delete");

        page.Categories[0].IsExpanded = true;
        Headless.Settle();
        Assert.Single(Headless.All<TextBlock>(tree), t => t.Text == "POST /api/notes_delete");
        var secretPanel = Headless.Find<Border>(window, b => b.Name == "NewSecretPanel");
        Assert.False(secretPanel.IsVisible);

        page.NewKeyName = "CI";
        page.AddKeyCommand.Execute(null);
        Headless.Settle();

        Assert.Equal(1, Headless.Find<ItemsControl>(window, c => c.Name == "KeyList").ItemCount);
        Assert.True(secretPanel.IsVisible);
        Assert.Contains(Headless.All<SelectableTextBlock>(window), t => t.Text == page.NewSecret);

        page.DismissSecretCommand.Execute(null);
        Headless.Settle();
        Assert.False(secretPanel.IsVisible);
        window.Close();
    });

    [Fact]
    public Task Turning_the_server_on_shows_where_it_listens() => Headless.Run(() =>
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        using var page = Page();
        page.PortText = port.ToString();
        var window = Headless.Show(new RestApiSettingsView(), page);
        bool UrlShown() => Headless.All<TextBlock>(window).Any(t => t.IsEffectivelyVisible && t.Text == page.Url);
        Assert.False(UrlShown());

        Headless.Find<ToggleSwitch>(window, s => s.Name == "EnabledSwitch").IsChecked = true;
        Headless.Settle();

        Assert.True(page.IsListening);
        Assert.True(UrlShown());
        Assert.True(Headless.Find<Button>(window, b => b.Name == "OpenDocsButton").IsEffectivelyVisible);
        window.Close();
    });
}
