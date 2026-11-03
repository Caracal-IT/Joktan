using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Logs.Resources;
using Microsoft.Extensions.Logging;

namespace Joktan.Logs;

/// <summary>
/// Searches Elasticsearch for logs. Fields are asked for by their usual names, which both Elastic's own (ECS) logs and
/// those an OpenTelemetry collector writes answer to, the latter through aliases; the first one a log has is used.
/// </summary>
public sealed class ElasticLogReader(string url, string index, string? authorization, HttpMessageHandler? handler = null) : ILogReader
{
    private static readonly string[] TimeFields = ["@timestamp"];
    private static readonly string[] LevelFields = ["log.level", "severity_text", "level"];
    private static readonly string[] AppFields = ["service.name", "app", "attributes.app"];
    private static readonly string[] ModuleFields = ["module", "attributes.module", "log.logger", "scope.name"];
    private static readonly string[] MessageFields = ["message", "body.text", "error.message", "error.exception.message"];
    private static readonly string[] ErrorTypeFields = ["error.type", "error.exception.type", "exception.type"];
    private static readonly string[] StackFields = ["error.stack_trace", "exception.stacktrace"];

    public async Task<LogResult> ReadAsync(LogQuery query, CancellationToken cancel = default)
    {
        if (!Uri.TryCreate(url.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var root) || root.Scheme is not ("http" or "https"))
            return LogResult.Failed(Strings.ElasticLogReader_NotAnAddress);
        if (!LogSource.IsValidIndex(index))
            return LogResult.Failed(Strings.ElasticLogReader_NotAnIndex);

        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = TimeSpan.FromSeconds(30);
        try
        {
            var path = string.Join(',', index.Trim().Split(',').Select(i => Uri.EscapeDataString(i.Trim()))) + "/_search?ignore_unavailable=true&allow_no_indices=true";
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(root, path));
            request.Content = new StringContent(Body(query).ToJsonString(), Encoding.UTF8, "application/json");
            if (authorization is not null)
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
            using var response = await http.SendAsync(request, cancel);
            var text = await response.Content.ReadAsStringAsync(cancel);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return LogResult.Failed(Strings.ElasticLogReader_LoginRefused);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return LogResult.Failed(Strings.ElasticLogReader_MayNotRead);
            if (!response.IsSuccessStatusCode)
                return LogResult.Failed(string.Format(Strings.ElasticLogReader_Answered, (int)response.StatusCode, Reason(text) ?? response.ReasonPhrase));
            return Read(text, query.Limit);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return LogResult.Failed(string.Format(Strings.ElasticLogReader_CouldNotReach, e.Message));
        }
    }

    /// <summary>The search: the newest logs in the window, at the levels asked for, as fields rather than whole documents.</summary>
    public static JsonObject Body(LogQuery query)
    {
        var filters = new JsonArray
        {
            new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["@timestamp"] = new JsonObject { ["gte"] = query.From.UtcDateTime.ToString("O"), ["lte"] = query.To.UtcDateTime.ToString("O") },
                },
            },
            AnyOf(LevelFields, query.Levels.SelectMany(LogLevels.NamesOf)),
        };
        if (!string.IsNullOrWhiteSpace(query.App))
            filters.Add(AnyOf(AppFields, [query.App.Trim()]));

        var search = new JsonObject { ["filter"] = filters };
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            search["must"] = new JsonArray
            {
                new JsonObject
                {
                    ["multi_match"] = new JsonObject
                    {
                        ["query"] = query.Text.Trim(),
                        ["fields"] = new JsonArray("message", "error.message", "error.exception.message", "error.stack_trace"),
                        ["type"] = "phrase",
                        ["lenient"] = true,
                    },
                },
            };
        }

        var fields = new JsonArray { new JsonObject { ["field"] = "@timestamp", ["format"] = "strict_date_optional_time" } };
        foreach (var field in LevelFields.Concat(AppFields).Concat(ModuleFields).Concat(MessageFields).Concat(ErrorTypeFields).Concat(StackFields).Distinct())
            fields.Add(field);

        return new JsonObject
        {
            ["size"] = query.Limit,
            ["track_total_hits"] = query.Limit + 1,
            ["_source"] = false,
            ["fields"] = fields,
            ["sort"] = new JsonArray(new JsonObject { ["@timestamp"] = "desc" }),
            ["query"] = new JsonObject { ["bool"] = search },
        };
    }

    // Levels and app names are written in any case, so each is matched whatever its case.
    private static JsonObject AnyOf(IEnumerable<string> fields, IEnumerable<string> values) => new()
    {
        ["bool"] = new JsonObject
        {
            ["should"] = new JsonArray(fields.SelectMany(field => values.Select(value => (JsonNode)new JsonObject
            {
                ["term"] = new JsonObject { [field] = new JsonObject { ["value"] = value, ["case_insensitive"] = true } },
            })).ToArray()),
            ["minimum_should_match"] = 1,
        },
    };

    /// <summary>The logs in an answer from Elasticsearch.</summary>
    public static LogResult Read(string answer, int limit)
    {
        try
        {
            using var json = JsonDocument.Parse(answer);
            var hits = json.RootElement.GetProperty("hits");
            var total = hits.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Object ? t.GetProperty("value").GetInt64() : 0;
            var entries = new List<LogEntry>();
            foreach (var hit in hits.GetProperty("hits").EnumerateArray())
            {
                if (!hit.TryGetProperty("fields", out var fields) || First(fields, TimeFields) is not { } time
                    || !DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var when))
                    continue;

                entries.Add(new LogEntry(
                    when,
                    LogLevels.Parse(First(fields, LevelFields)) ?? LogLevel.Information,
                    First(fields, AppFields) ?? "",
                    First(fields, ModuleFields) ?? "",
                    First(fields, MessageFields) ?? "",
                    First(fields, ErrorTypeFields),
                    First(fields, StackFields)));
            }

            return new LogResult(entries, total > limit || entries.Count > limit);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return LogResult.Failed(Strings.ElasticLogReader_CouldNotReadTheAnswer);
        }
    }

    private static string? First(JsonElement fields, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (fields.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array && values.GetArrayLength() > 0)
            {
                var value = values[0];
                var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }
        }

        return null;
    }

    private static string? Reason(string answer)
    {
        try
        {
            using var json = JsonDocument.Parse(answer);
            return json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                   && error.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
