using System;
using System.Linq;
using System.Threading.Tasks;
using Joktan.Mqtt;
using Joktan.Mqtt.ViewModels;
using Joktan.TopicDashboards;
using Joktan.TopicDashboards.ViewModels;
using Joktan.TopicExplorer.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.TopicExplorer.Tests.ViewModels;

/// <summary>A topic explorer with its own data folder, on a client with no credentials to look up.</summary>
[Collection(ProcessStateCollection.Name)]
public abstract class ExplorerTest : DataFolderTest
{
    protected static MqttViewModel Mqtt(int port = 1883, params string[] names) => new(null, new MqttSettings
    {
        Clients = [.. (names.Length > 0 ? names : ["c"]).Select(n => new ClientProfile { Name = n, Host = "127.0.0.1", Port = port.ToString(), ClientId = n + Guid.NewGuid().ToString("N")[..8] })],
    });

    protected static TopicDashboardsViewModel Dashboards(MqttViewModel mqtt) => new(mqtt, DashboardStore.Load(DashboardStore.DefaultFolder));

    protected static TopicExplorerViewModel Explorer(MqttViewModel mqtt) => new(mqtt, Dashboards(mqtt));

    protected static async Task Connect(MqttClientViewModel client)
    {
        await client.ConnectCommand.ExecuteAsync(null);
        await Headless.Until(() => client.IsConnected);
    }
}
