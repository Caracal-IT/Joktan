using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Joktan.Api;
using Joktan.Api.ViewModels;
using Joktan.Mqtt;
using Joktan.Mqtt.Tests;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Joktan.Workflows.Engine;
using Xunit;

namespace Joktan.Workflows.Tests.Engine;

[Collection(ProcessStateCollection.Name)]
public class AppWorkflowServicesTests : DataFolderTest
{
    private static MqttViewModel Mqtt(int port) => new(null, new MqttSettings
    {
        Clients = [new ClientProfile { Name = "Local", Host = "127.0.0.1", Port = port.ToString(), ClientId = "wf" + Guid.NewGuid().ToString("N")[..8] }],
    });

    private ApiClientViewModel Api() => new(new ApiWorkspace(Data.At("requests")));

    [Fact]
    public Task Publishes_and_listens_through_the_real_clients() => Headless.Run(async () =>
    {
        await using var broker = await TestBroker.StartAsync();
        var mqtt = Mqtt(broker.Port);
        var services = new AppWorkflowServices(mqtt, Api());

        Assert.Equal(["Local"], services.MqttClients);
        Assert.Contains("not connected", await services.PublishAsync("Local", "a/b", [1], 0, false));
        Assert.Contains("no client", await services.PublishAsync("Nowhere", "a/b", [1], 0, false));

        await mqtt.Clients[0].ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => mqtt.Clients[0].IsConnected);

        MqttArrival? arrived = null;
        var (listener, error) = await services.ListenAsync("Local", "tanks/+/level", m => arrived = m);
        Assert.Null(error);
        await broker.SendAsync("tanks/2/temperature", "20");
        await broker.SendAsync("tanks/2/level", """{"level": 5}""");
        await Headless.Until(() => arrived is not null);
        Assert.Equal("tanks/2/level", arrived!.Topic);
        Assert.Equal("5", JsonText.Of(arrived.ToJson()["payload"]!["level"]));

        Assert.Null(await services.PublishAsync("Local", "out/x", Encoding.UTF8.GetBytes("hi"), 1, false));
        await Headless.Until(() => broker.PublishedTopics.Contains("out/x"));

        listener!.Dispose();
        arrived = null;
        await broker.SendAsync("tanks/3/level", "1");
        await Task.Delay(200);
        Headless.Settle();
        Assert.Null(arrived);
        await mqtt.Clients[0].DisconnectCommand.ExecuteAsync(null);
    });

    [Fact]
    public Task Reads_a_clients_folder_messages_with_their_settings() => Headless.Run(() =>
    {
        var folder = Data.At("messages");
        Data.Write("messages/plant/line1/reading.json", """{"temp": 21}""");
        Data.Write("messages/plant/line1/reading.send.json", """{"qos": 1, "retain": true}""");
        var mqtt = Mqtt(1883);
        mqtt.Clients[0].UseFolder(folder);
        var services = new AppWorkflowServices(mqtt, Api());

        Assert.Equal(["plant/line1/reading.json"], services.FolderMessages("Local"));
        var message = services.ReadFolderMessage("Local", "plant/line1/reading.json")!;
        Assert.Equal("plant/line1", message.Topic);
        Assert.Equal("""{"temp": 21}""", message.Body);
        Assert.Equal((1, true), (message.Qos, message.Retain));
        Assert.Null(services.ReadFolderMessage("Local", "nothing.json"));
        Assert.Empty(services.FolderMessages("Nowhere"));
    });

    [Fact]
    public Task Lists_and_reads_the_API_Clients_saved_requests() => Headless.Run(() =>
    {
        ApiWorkspace.Write(Data.At("requests", "Orders", "Create order.request.json"), new ApiRequest { Method = "POST", Url = "{{base}}/orders" });
        ApiWorkspace.Write(Data.At("requests", "Ping.request.json"), new ApiRequest { Url = "http://x" });
        ApiWorkspace.Write(Data.At("requests", ".hidden", "Secret.request.json"), new ApiRequest());
        File.WriteAllText(Data.At("outside.request.json"), "{}");
        var services = new AppWorkflowServices(Mqtt(1883), Api());

        Assert.Equal(["Orders/Create order", "Ping"], services.SavedRequests());
        Assert.Equal("POST", services.ReadRequest("Orders/Create order")!.Method);

        // A path cannot lead out of the requests folder.
        Assert.Null(services.ReadRequest("../outside"));
    });
}
