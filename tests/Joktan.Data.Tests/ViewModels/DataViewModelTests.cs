using Joktan.Certificates;
using Joktan.Containers;
using Joktan.Containers.Tests.ViewModels;
using Joktan.Containers.ViewModels;
using Joktan.Data.ViewModels;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Data.Tests.ViewModels;

public class DataViewModelTests : FakeEngineTest
{
    [Fact]
    public void It_opens_on_the_mqtt_tab_with_the_containers_pages_brokers()
    {
        new ComposeSettings
        {
            ComposeFiles = [], Engine = ContainerEngine.Docker, BrokersFolder = Folder.At("brokers"),
            Brokers = [new MosquittoBroker { Name = "listed", Port = 1883 }],
        }.Save(DataFolder);
        var containers = new ContainersViewModel(DataFolder, CertificateStore.Load(Folder.At("certificates")), new FakeCredentialStore());
        var page = new DataViewModel(containers);

        Assert.Equal("Data", page.Title);
        Assert.Equal("MQTT brokers and databases", page.Subtitle);
        Assert.Equal(DataViewModel.MqttTab, page.SelectedTab);
        Assert.Same(containers, page.Containers);
        Assert.Same(containers.Brokers, page.Brokers);
        Assert.Equal("listed", Assert.Single(page.Brokers.Items).Name);
        containers.Shutdown();
    }
}
