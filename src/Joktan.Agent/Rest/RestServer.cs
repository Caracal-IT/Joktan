using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Core;

namespace Joktan.Agent.Rest;

/// <summary>
/// The REST API's listener: plain HTTP/1.1, one request per connection, on this computer alone unless
/// told otherwise. A TCP listener rather than HttpListener, which on Windows needs an administrator to
/// answer other computers. It reads requests and writes replies; <see cref="RestApi"/> decides them.
/// </summary>
public sealed class RestServer : IDisposable
{
    private const int MaxHeaderBytes = 16 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(30);

    private readonly TcpListener _listener;
    private readonly Func<RestRequest, Task<RestReply>> _answer;
    private readonly CancellationTokenSource _stop = new();

    /// <param name="fromOtherComputers">Whether to answer other computers, rather than only this one.</param>
    /// <param name="port">The port; 0 for any that is free.</param>
    /// <param name="answer">Decides the reply to a request.</param>
    /// <exception cref="SocketException">The port is in use.</exception>
    public RestServer(bool fromOtherComputers, int port, Func<RestRequest, Task<RestReply>> answer)
    {
        _answer = answer;
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
                _ = ServeAsync(await _listener.AcceptTcpClientAsync(_stop.Token));
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
        {
            // Stopped.
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var (request, problem) = await ReadAsync(stream);
                var reply = problem ?? await _answer(request!);
                await WriteAsync(stream, reply);
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
            {
                // The caller went away, or the server is stopping.
            }
        }
    }

    private async Task<(RestRequest? Request, RestReply? Problem)> ReadAsync(NetworkStream stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(ReadTimeout);

        var buffer = new byte[MaxHeaderBytes];
        var length = 0;
        var end = -1;
        while (end < 0)
        {
            if (length == buffer.Length)
                return (null, RestReply.Problem(431, "The request's headers are too long."));
            var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token);
            if (read == 0)
                throw new IOException("The connection closed before the request was whole.");
            length += read;
            end = buffer.AsSpan(0, length).IndexOf("\r\n\r\n"u8);
        }

        var lines = Encoding.ASCII.GetString(buffer, 0, end).Split("\r\n");
        var parts = lines[0].Split(' ');
        if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal))
            return (null, RestReply.Problem(400, "That is not an HTTP request."));

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines[1..])
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (headers.ContainsKey("Transfer-Encoding"))
            return (null, RestReply.Problem(411, "Send the body with a Content-Length."));
        var bodyLength = 0;
        if (headers.TryGetValue("Content-Length", out var given) && (!int.TryParse(given, out bodyLength) || bodyLength < 0))
            return (null, RestReply.Problem(400, "The Content-Length is not a number."));
        if (bodyLength > MaxBodyBytes)
            return (null, RestReply.Problem(413, $"The body may be at most {MaxBodyBytes} bytes."));

        // curl, among others, waits to be told to go on before sending a larger body.
        if (headers.TryGetValue("Expect", out var expect) && expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
            await stream.WriteAsync("HTTP/1.1 100 Continue\r\n\r\n"u8.ToArray(), timeout.Token);

        var body = new byte[bodyLength];
        var have = Math.Min(length - (end + 4), bodyLength);
        Array.Copy(buffer, end + 4, body, 0, have);
        while (have < bodyLength)
        {
            var read = await stream.ReadAsync(body.AsMemory(have), timeout.Token);
            if (read == 0)
                throw new IOException("The connection closed before the body was whole.");
            have += read;
        }

        var target = parts[1];
        var question = target.IndexOf('?');
        var path = question < 0 ? target : target[..question];
        var query = question < 0 ? "" : target[(question + 1)..];
        return (new RestRequest(parts[0].ToUpperInvariant(), path, query, headers, Encoding.UTF8.GetString(body)), null);
    }

    private async Task WriteAsync(NetworkStream stream, RestReply reply)
    {
        var body = reply.Content ?? Encoding.UTF8.GetBytes(reply.Body?.ToJsonString(JsonFormats.Indented) ?? "");
        var head = new StringBuilder()
            .Append($"HTTP/1.1 {reply.Status} {Reason(reply.Status)}\r\n")
            .Append($"Content-Type: {reply.ContentType}\r\n")
            .Append($"Content-Length: {body.Length}\r\n")
            .Append("Cache-Control: no-store\r\n")
            .Append("Connection: close\r\n");
        foreach (var (name, value) in reply.Headers ?? new Dictionary<string, string>())
            head.Append($"{name}: {value}\r\n");
        head.Append("\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), _stop.Token);
        await stream.WriteAsync(body, _stop.Token);
    }

    private static string Reason(int status) => status switch
    {
        200 => "OK",
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        405 => "Method Not Allowed",
        411 => "Length Required",
        413 => "Content Too Large",
        422 => "Unprocessable Content",
        431 => "Request Header Fields Too Large",
        _ => "Internal Server Error",
    };

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}
