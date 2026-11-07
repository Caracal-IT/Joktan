using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Joktan.Agent.Mcp;
using Joktan.Agent.Tools;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Agent.Tests.Tools;

// The client has no username or certificate, so nothing reaches the real credential store.
[Collection(ProcessStateCollection.Name)]
public sealed class MqttToolsTests : DataFolderTest
{
    private static MqttViewModel Page() => new(null, new MqttSettings
    {
        Clients = [new ClientProfile { Name = "secondary", Host = "localhost", ClientId = "secondary" }],
    });

    private static async Task<string> Call(MqttViewModel mqtt, string name, string arguments)
    {
        var tool = MqttTools.Create(mqtt).Single(t => t.Name == name);
        using var json = JsonDocument.Parse(arguments);
        return JsonSerializer.Serialize(await tool.RunAsync(new ToolArgs(json.RootElement.Clone())));
    }

    private static string Json(string text) => JsonSerializer.Serialize(text);

    [Fact]
    public Task Writes_a_message_and_its_settings_into_a_new_folder() => Headless.Run(async () =>
    {
        var mqtt = Page();
        var folder = Data.At("demo-messages");

        await Call(mqtt, "mqtt_folder_use", $$"""{"client": "secondary", "path": {{Json(folder)}}, "create": true}""");
        var answer = await Call(mqtt, "mqtt_folder_message_save", """
            {"client": "secondary", "file": "demo/orders/new.json", "body": {"orderId": "A1", "qty": 2},
             "settings": {"group": "Demo", "qos": 1, "fields": {"orderId": {"type": "guid"}}}}
            """);

        Assert.Equal(2, JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "demo", "orders", "new.json"))).RootElement.GetProperty("qty").GetInt32());
        var settings = MessageSettings.Parse(File.ReadAllText(Path.Combine(folder, "demo", "orders", "new.send.json")));
        Assert.Equal("Demo", settings.Group);
        Assert.Equal(1, settings.Qos);
        Assert.Contains("demo/orders", answer);
    });

    [Fact]
    public Task Reads_a_leading_tilde_as_the_home_folder() => Headless.Run(async () =>
    {
        var mqtt = Page();

        // Not created, so nothing is left in the real home folder.
        await Call(mqtt, "mqtt_folder_use", """{"client": "secondary", "path": "~/joktan-test-no-such-folder"}""");

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "joktan-test-no-such-folder"), mqtt.Find("secondary")!.FolderPath);
    });

    [Fact]
    public Task Keeps_messages_inside_the_clients_folder() => Headless.Run(async () =>
    {
        var mqtt = Page();
        await Call(mqtt, "mqtt_folder_use", $$"""{"client": "secondary", "path": {{Json(Data.At("messages"))}}, "create": true}""");

        await Assert.ThrowsAsync<ToolFailure>(() => Call(mqtt, "mqtt_folder_message_save", """{"client": "secondary", "file": "../outside.json", "body": {}}"""));
        await Assert.ThrowsAsync<ToolFailure>(() => Call(mqtt, "mqtt_folder_message_save", """{"client": "secondary", "file": "a.send.json", "body": {}}"""));
        await Assert.ThrowsAsync<ToolFailure>(() => Call(mqtt, "mqtt_folder_message_save", """{"client": "secondary", "file": "a.json", "body": {}, "settings": {"qos": 5}}"""));
        Assert.False(File.Exists(Data.At("outside.json")));
    });
}
