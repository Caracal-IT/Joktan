using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Joktan.Mqtt.Proxy;
using Joktan.Mqtt.ViewModels;
using Joktan.Mqtt.Views;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests.Views;

[Collection(ProcessStateCollection.Name)]
public class ProxiesViewTests : DataFolderTest
{
    private MqttViewModel Mqtt() => new(null, new MqttSettings { Clients = [new ClientProfile { Name = "a", Host = "localhost", ClientId = "a" }] }, new FakeCredentialStore());

    private string Folder()
    {
        Data.Write("messages/plant/status/status.json", """{"mode":"test"}""");
        Data.Write("messages/plant/bad/bad.json", "{ not json");
        return Data.At("messages");
    }

    [Fact]
    public Task With_no_proxies_it_says_how_to_start() => Headless.Run(() =>
    {
        var page = new ProxiesViewModel(Mqtt(), new ProxySettings(), new FakeCredentialStore(), Data.At("proxies"));
        var window = Headless.Show(new ProxiesView(), page);
        Headless.Settle();

        Assert.Contains(Headless.All<TextBlock>(window), t => t.IsEffectivelyVisible && t.Text?.StartsWith("No proxies yet") == true);
        window.Close();
    });

    [Fact]
    public Task A_proxy_shows_where_devices_connect_its_broker_its_messages_and_activity() => Headless.Run(async () =>
    {
        var page = new ProxiesViewModel(Mqtt(), new ProxySettings
        {
            Proxies = [new ProxyProfile { Name = "Lab", Folder = Folder(), Selected = ["plant/status/status.json"] }],
        }, new FakeCredentialStore(), Data.At("proxies"));
        var proxy = page.Proxies[0];
        var window = Headless.Show(new ProxiesView(), page);
        await Headless.Until(() => proxy.Rows.Count == 2);
        proxy.Rows[1].BrokerTopicText = "lab/#";
        proxy.AddMessageCommand.Execute(null);
        proxy.NewMessage!.Error = "A problem";
        proxy.Error = "Cannot listen";
        Headless.Settle();
        window.UpdateLayout();
        window.CaptureRenderedFrame();

        var texts = Headless.All<TextBlock>(window).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        Assert.Contains("Devices connect to localhost: plain MQTT on 1885", texts);
        Assert.Contains("Server certificate", texts);
        Assert.Contains("For TLS, make a server certificate on Dev Tools / Certificates first.", texts);
        Assert.Contains("localhost:1883", texts);
        Assert.Contains("plant/status", texts);
        Assert.Contains("Topic at the broker", texts);
        Assert.Contains("New message", texts);
        Assert.Contains("A problem", texts);
        Assert.Contains("Cannot listen", texts);
        Assert.Contains(proxy.Rows[1].BrokerTopicError, texts);

        // The dialogs have nothing to show over off screen, so they give nothing.
        _ = new ProxiesView { DataContext = page };
        Assert.False(await page.EditSettings!(new MessageSettingsViewModel(proxy.Rows[1].Message)));
        Assert.Null(await page.PickFolder!());
        window.Close();
    });
}
