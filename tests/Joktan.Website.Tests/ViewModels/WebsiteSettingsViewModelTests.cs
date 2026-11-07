using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Joktan.Website.ViewModels;
using Xunit;

namespace Joktan.Website.Tests.ViewModels;

[Collection(ProcessStateCollection.Name)]
public class WebsiteSettingsViewModelTests : WebsiteTest
{
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Fact]
    public Task Switching_the_website_on_listens_and_serves_the_sign_in_page() => Headless.Run(async () =>
    {
        Settings.Enabled = false;
        var vm = new WebsiteSettingsViewModel(Settings, Site, () => "Nobody can sign in yet.");
        vm.Start();
        Assert.False(vm.IsListening);
        Assert.Null(vm.ListeningUrl);

        vm.PortText = "80";
        Assert.NotNull(vm.PortError);
        var port = FreePort();
        vm.PortText = port.ToString();
        Assert.Null(vm.PortError);
        vm.Enabled = true;
        Assert.True(vm.IsListening);
        Assert.Equal($"http://localhost:{port}", vm.ListeningUrl);
        Assert.Null(vm.NetworkUrl);
        Assert.Equal("Nobody can sign in yet.", vm.SignInProblem);
        Assert.True(WebsiteSettings.Load().Enabled);

        using var http = new HttpClient();
        var html = await Task.Run(() => http.GetStringAsync($"http://localhost:{port}/login"));
        Assert.Contains("/assets/app.js", html);

        vm.SessionHoursText = "0";
        Assert.NotNull(vm.SessionHoursError);
        vm.SessionHoursText = "12";
        Assert.Equal(12, Settings.SessionHours);

        vm.Enabled = false;
        Assert.False(vm.IsListening);
        vm.Dispose();
    });

    [Fact]
    public Task A_port_in_use_says_so() => Headless.Run(() =>
    {
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            Settings.Port = ((IPEndPoint)taken.LocalEndpoint).Port;
            var vm = new WebsiteSettingsViewModel(Settings, Site);
            vm.Start();
            Assert.False(vm.IsListening);
            Assert.Contains(Settings.Port.ToString(), vm.ServerProblem);
            vm.Dispose();
        }
        finally
        {
            taken.Stop();
        }
    });
}
