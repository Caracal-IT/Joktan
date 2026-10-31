using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Certificates;
using Joktan.Tests.TestSupport;
using Xunit;

namespace Joktan.Mqtt.Tests;

// Servers that are not brokers at all, as what they do is what these are about.
public class MqttConnectionFailureTests : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly MqttConnection _connection = new();

    private int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private void Serve(Func<NetworkStream, Task> answer)
    {
        _listener.Start();
        _ = Task.Run(async () =>
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    _ = answer(client.GetStream());
                }
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _connection.DisconnectAsync().Wait(TestContext.Current.CancellationToken);
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    private BrokerEndpoint Endpoint(TlsEndpoint? tls = null) => new("127.0.0.1", Port, "x", null, null, tls);

    [Fact]
    public async Task A_server_that_says_nothing_is_described_as_not_answering()
    {
        Serve(_ => Task.Delay(Timeout.Infinite, _stop.Token));

        await _connection.ConnectAsync(Endpoint());

        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.Equal("No answer from the broker within 10 seconds", _connection.FailureReason);
    }

    [Fact]
    public async Task A_server_that_closes_the_connection_is_not_described_as_not_answering()
    {
        Serve(stream =>
        {
            stream.Close();
            return Task.CompletedTask;
        });

        await _connection.ConnectAsync(Endpoint());

        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.StartsWith("Could not connect: ", _connection.FailureReason);
    }

    [Fact]
    public async Task Disconnecting_while_connecting_stops_it_quietly()
    {
        Serve(_ => Task.Delay(Timeout.Infinite, _stop.Token));

        var connecting = _connection.ConnectAsync(Endpoint());
        while (_connection.State != ConnectionState.Connecting)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        await _connection.DisconnectAsync();
        await connecting;

        Assert.Equal(ConnectionState.Disconnected, _connection.State);
    }

    [Fact]
    public async Task A_server_that_does_not_speak_tls_fails_the_handshake()
    {
        Serve(async stream =>
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\nthis is not TLS at all, as anyone can see\r\n"), _stop.Token);
            await stream.FlushAsync(_stop.Token);
        });

        await _connection.ConnectAsync(Endpoint(new TlsEndpoint(null, null, null, null, true)));

        Assert.Equal(ConnectionState.Failed, _connection.State);
        Assert.NotNull(_connection.FailureReason);
    }

    [Fact]
    public async Task With_a_client_certificate_it_says_to_check_that_instead()
    {
        using var folder = new TempFolder();
        var store = CertificateStore.Load(folder.Path);
        Serve(async stream =>
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\nthis is not TLS at all, as anyone can see\r\n"), _stop.Token);
            await stream.FlushAsync(_stop.Token);
        });
        var root = store.Create(new CertificateSpec { Kind = CertificateKind.RootAuthority, Name = "root", CommonName = "root", NotAfter = DateTimeOffset.UtcNow.AddYears(1) });
        var client = store.Create(new CertificateSpec { Kind = CertificateKind.Client, Name = "client", CommonName = "client", IssuerId = root.Id, NotAfter = DateTimeOffset.UtcNow.AddYears(1) });

        await _connection.ConnectAsync(Endpoint(new TlsEndpoint(null, client.CertificatePath, client.KeyPath, null, true)));

        Assert.Contains("Check that the broker accepts the client certificate", _connection.FailureReason);
    }
}
