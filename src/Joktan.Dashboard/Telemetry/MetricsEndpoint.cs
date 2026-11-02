using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Joktan.Dashboard.Telemetry;

/// <summary>
/// The web address Prometheus scrapes, /metrics. The exporter only answers requests addressed to the
/// name it was given, and a Prometheus in a container calls this computer by another. This listens
/// for it on this computer alone, unless told otherwise, and answers from the exporter.
/// </summary>
public sealed class MetricsEndpoint : IDisposable
{
    private const int MaxRequestBytes = 8192;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpListener _listener;
    private readonly Func<CancellationToken, Task<(string ContentType, byte[] Body)>> _metrics;
    private readonly CancellationTokenSource _stop = new();

    /// <param name="fromOtherComputers">Whether to answer other computers, rather than only this one.</param>
    /// <param name="port">The port; 0 for any that is free.</param>
    /// <param name="metrics">Gives the metrics to answer with.</param>
    /// <exception cref="SocketException">The port is in use.</exception>
    public MetricsEndpoint(bool fromOtherComputers, int port, Func<CancellationToken, Task<(string ContentType, byte[] Body)>> metrics)
    {
        _metrics = metrics;
        _listener = new TcpListener(fromOtherComputers ? IPAddress.Any : IPAddress.Loopback, port);
        _listener.Start();
        _ = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
                _ = AnswerAsync(await _listener.AcceptTcpClientAsync(_stop.Token));
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Stopped.
        }
    }

    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
        {
            timeout.CancelAfter(RequestTimeout);
            try
            {
                var stream = client.GetStream();
                var line = await ReadRequestLineAsync(stream, timeout.Token);
                if (line is null || !line.StartsWith("GET /metrics", StringComparison.Ordinal) || line.Length <= 12 || line[12] is not (' ' or '?'))
                {
                    await WriteAsync(stream, "404 Not Found", "text/plain", "Not found\n"u8.ToArray(), timeout.Token);
                    return;
                }

                var (contentType, body) = await _metrics(timeout.Token);
                await WriteAsync(stream, "200 OK", contentType, body, timeout.Token);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or System.Net.Http.HttpRequestException)
            {
                // The one asking went away, or the answer could not be made, and it will ask again.
            }
        }
    }

    // Reads up to the end of the headers, as answering sooner can reset the connection.
    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancel)
    {
        var buffer = new byte[MaxRequestBytes];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancel);
            if (read == 0)
                return null;
            length += read;
            var text = Encoding.ASCII.GetString(buffer, 0, length);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal))
                return text[..text.IndexOf('\r')];
        }

        return null;
    }

    private static async Task WriteAsync(NetworkStream stream, string status, string contentType, byte[] body, CancellationToken cancel)
    {
        var head = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), cancel);
        await stream.WriteAsync(body, cancel);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
