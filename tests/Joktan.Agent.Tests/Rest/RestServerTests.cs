using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Joktan.Agent.Rest;
using Xunit;

namespace Joktan.Agent.Tests.Rest;

public sealed class RestServerTests : IDisposable
{
    private readonly List<RestRequest> _requests = [];
    private readonly RestServer _server;
    private readonly HttpClient _http = new();

    public RestServerTests()
    {
        _server = new RestServer(false, 0, request =>
        {
            lock (_requests)
                _requests.Add(request);
            return Task.FromResult(new RestReply(201, new JsonObject { ["echo"] = request.Body, ["path"] = request.Path, ["query"] = request.Query },
                new Dictionary<string, string> { ["X-Test"] = "yes" }));
        });
    }

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private string Url(string path) => $"http://127.0.0.1:{_server.Port}{path}";

    [Fact]
    public async Task Passes_the_request_on_and_writes_the_reply()
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Url("/api/notes_list?folder=a%20b"))
        {
            Content = new StringContent("""{"title":"Ünïcode"}""", Encoding.UTF8, "application/json"),
        };
        message.Headers.Add("X-Api-Key", "jok_test");

        using var response = await _http.SendAsync(message, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("yes", Assert.Single(response.Headers.GetValues("X-Test")));
        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal("""{"title":"Ünïcode"}""", body["echo"]!.GetValue<string>());
        Assert.Equal("/api/notes_list", body["path"]!.GetValue<string>());
        Assert.Equal("folder=a%20b", body["query"]!.GetValue<string>());

        var request = Assert.Single(_requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("jok_test", request.Headers["x-api-key"]);
    }

    [Fact]
    public async Task Reads_a_body_larger_than_the_first_read()
    {
        var text = new string('x', 300_000);
        using var response = await _http.PostAsync(Url("/api/x"), new StringContent(text), TestContext.Current.CancellationToken);

        var body = JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!;
        Assert.Equal(text, body["echo"]!.GetValue<string>());
    }

    [Fact]
    public async Task Turns_away_a_body_over_a_megabyte_without_passing_it_on()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _server.Port, TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("POST /api/x HTTP/1.1\r\nHost: localhost\r\nContent-Length: 2000000\r\n\r\n"), TestContext.Current.CancellationToken);

        var reply = await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("HTTP/1.1 413", reply);
        Assert.Empty(_requests);
    }

    [Fact]
    public async Task Tells_a_caller_that_expects_it_to_go_on()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _server.Port, TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("POST /api/x HTTP/1.1\r\nHost: localhost\r\nExpect: 100-continue\r\nContent-Length: 2\r\n\r\n"), TestContext.Current.CancellationToken);
        var buffer = new byte[64];
        var read = await stream.ReadAsync(buffer, TestContext.Current.CancellationToken);
        Assert.StartsWith("HTTP/1.1 100 Continue", Encoding.ASCII.GetString(buffer, 0, read));

        await stream.WriteAsync("{}"u8.ToArray(), TestContext.Current.CancellationToken);
        var reply = await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Contains("HTTP/1.1 201", reply);
        Assert.Equal("{}", Assert.Single(_requests).Body);
    }

    [Fact]
    public async Task Turns_away_what_is_not_http()
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _server.Port, TestContext.Current.CancellationToken);
        var stream = client.GetStream();
        await stream.WriteAsync("hello there\r\n\r\n"u8.ToArray(), TestContext.Current.CancellationToken);

        var reply = await new StreamReader(stream).ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.StartsWith("HTTP/1.1 400", reply);
        Assert.Empty(_requests);
    }

    [Fact]
    public void Throws_when_the_port_is_in_use()
    {
        Assert.Throws<SocketException>(() => new RestServer(false, _server.Port, _ => throw new InvalidOperationException()));
    }
}
