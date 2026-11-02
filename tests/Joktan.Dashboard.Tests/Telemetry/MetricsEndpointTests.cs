using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Joktan.Dashboard.Telemetry;
using Xunit;

namespace Joktan.Dashboard.Tests.Telemetry;

public class MetricsEndpointTests
{
    private static MetricsEndpoint Endpoint(int port = 0)
        => new(false, port, _ => Task.FromResult(("text/plain; version=0.0.4", Encoding.UTF8.GetBytes("up 1\n"))));

    [Fact]
    public async Task Answers_metrics_whatever_name_it_is_called_by()
    {
        using var endpoint = Endpoint();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{endpoint.Port}/metrics");
        request.Headers.Host = "host.docker.internal";

        using var response = await http.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("up 1\n", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType!.ToString());
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/metricsx")]
    [InlineData("/other")]
    public async Task Answers_nothing_else(string path)
    {
        using var endpoint = Endpoint();
        using var http = new HttpClient();

        using var response = await http.GetAsync($"http://127.0.0.1:{endpoint.Port}{path}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void A_port_in_use_is_reported()
    {
        using var first = Endpoint();

        Assert.Throws<SocketException>(() => Endpoint(first.Port));
    }
}
