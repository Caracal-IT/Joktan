using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Joktan.Core;

/// <summary>Where logs are sent.</summary>
public sealed record ElasticTarget(Uri Url, string DataStream, string? Authorization);

/// <summary>
/// Sends logs straight to Elasticsearch in batches, from a thread of its own so that logging never
/// waits for the network. While it cannot be reached the newest logs are kept, up to a limit.
/// </summary>
public sealed class ElasticLogSink : IDisposable
{
    private const int MaxQueued = 5000;
    private const int MaxBatch = 500;

    private readonly Func<ElasticTarget?> _target;
    private readonly HttpClient _http;
    private readonly Queue<string> _queue = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly TimeSpan _interval;

    /// <param name="target">Asked before each send, so that a change on the settings page applies at once.</param>
    public ElasticLogSink(Func<ElasticTarget?> target, HttpMessageHandler? handler = null, TimeSpan? interval = null)
    {
        _target = target;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(10);
        _interval = interval ?? TimeSpan.FromSeconds(2);
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Why the last send failed, or null if it worked.</summary>
    public static string? LastProblem { get; private set; }

    public void Add(string document)
    {
        lock (_queue)
        {
            if (_queue.Count >= MaxQueued)
                _queue.Dequeue();
            _queue.Enqueue(document);
        }
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            await FlushAsync(_stop.Token);
        }
    }

    /// <summary>Sends what is waiting. What could not be delivered stays for the next time.</summary>
    public async Task FlushAsync(CancellationToken cancel = default)
    {
        while (_target() is { } target)
        {
            string[] batch;
            lock (_queue)
                batch = _queue.Take(MaxBatch).ToArray();
            if (batch.Length == 0)
                return;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(target.Url, "_bulk"));
                request.Content = new StringContent(Bulk(target.DataStream, batch), Encoding.UTF8, "application/x-ndjson");
                if (target.Authorization is { } authorization)
                    request.Headers.TryAddWithoutValidation("Authorization", authorization);
                using var response = await _http.SendAsync(request, cancel);
                if (!response.IsSuccessStatusCode)
                {
                    LastProblem = $"Elasticsearch answered {(int)response.StatusCode} {response.ReasonPhrase}";
                    return;
                }

                LastProblem = FirstRefusal(await response.Content.ReadAsStringAsync(cancel));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                if (!cancel.IsCancellationRequested)
                    LastProblem = e.Message;
                return;
            }

            // A document Elasticsearch refuses would be refused for ever, so it goes too.
            lock (_queue)
            {
                for (var i = 0; i < batch.Length && _queue.Count > 0; i++)
                    _queue.Dequeue();
            }
        }
    }

    private static string Bulk(string dataStream, IEnumerable<string> documents)
    {
        var action = JsonSerializer.Serialize(new { create = new { _index = dataStream } });
        var body = new StringBuilder();
        foreach (var document in documents)
            body.Append(action).Append('\n').Append(document).Append('\n');
        return body.ToString();
    }

    private static string? FirstRefusal(string response)
    {
        try
        {
            using var json = JsonDocument.Parse(response);
            if (!json.RootElement.TryGetProperty("errors", out var errors) || !errors.GetBoolean())
                return null;
            foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            {
                if (item.TryGetProperty("create", out var create) && create.TryGetProperty("error", out var error))
                    return error.TryGetProperty("reason", out var reason) ? reason.GetString() : error.ToString();
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return "Elasticsearch gave an answer that could not be read";
        }

        return null;
    }

    /// <summary>Tries to reach Elasticsearch, and says how it went.</summary>
    public static async Task<(bool Reached, string Text)> TestAsync(string url, string? authorization, HttpMessageHandler? handler = null)
    {
        using var http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(10);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url.Trim().TrimEnd('/') + "/");
            if (authorization is not null)
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return (false, "401");
            // A key that may only write logs is not allowed to read this, and that is as it should be.
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return (true, "");
            if (!response.IsSuccessStatusCode)
                return (false, $"{(int)response.StatusCode} {response.ReasonPhrase}");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return (true, json.RootElement.GetProperty("version").GetProperty("number").GetString() ?? "");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or UriFormatException)
        {
            return (false, e.Message);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
            // What was logged last, such as why the app stopped, is worth a short wait.
            FlushAsync().Wait(TimeSpan.FromSeconds(3));
        }
        catch (AggregateException)
        {
            // Closing goes on regardless.
        }

        _http.Dispose();
        _stop.Dispose();
    }
}

/// <summary>Turns logs into Elasticsearch documents, each naming the app and its module.</summary>
public sealed class ElasticLoggerProvider(ElasticLogSink sink, string? version = null) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger(sink, categoryName, version);

    public void Dispose()
    {
    }

    private sealed class Logger(ElasticLogSink sink, string category, string? version) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => AppLog.Settings.LogsToElastic && logLevel >= AppLog.Settings.MinimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            var document = new Dictionary<string, object?>
            {
                ["@timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
                ["app"] = AppLog.AppName,
                ["module"] = AppLog.ModuleOf(category),
                ["message"] = formatter(state, exception),
                ["log"] = new { level = logLevel.ToString().ToLowerInvariant(), logger = category },
                ["service"] = new { name = AppLog.AppName, version },
                ["host"] = new { name = Environment.MachineName },
            };
            if (state is IEnumerable<KeyValuePair<string, object?>> values
                && values.Where(v => v.Key != "{OriginalFormat}").ToDictionary(v => v.Key, v => v.Value?.ToString()) is { Count: > 0 } fields)
                document["fields"] = fields;
            if (exception is not null)
                document["error"] = new { type = exception.GetType().FullName, message = exception.Message, stack_trace = exception.ToString() };

            sink.Add(JsonSerializer.Serialize(document));
        }
    }
}
