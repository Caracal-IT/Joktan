using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Logs.Tests;

public class ElasticLogReaderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 3, 12, 0, 0, TimeSpan.FromHours(2));

    private static readonly LogQuery Hour = new(Noon.AddHours(-1), Noon, LogLevels.Errors);

    internal sealed class Answer(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public string? LastAuthorization { get; private set; }
        public Uri? LastUrl { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri;
            LastAuthorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    // As an OpenTelemetry collector's logs answer, through Elasticsearch's aliases.
    internal const string TwoErrors = """
        {"hits":{"total":{"value":2,"relation":"eq"},"hits":[
          {"fields":{"@timestamp":["2026-11-03T09:59:00.000Z"],"log.level":["Error"],"service.name":["Joktan"],"app":["joktan"],
                     "module":["mqtt"],"message":["Could not connect"],"error.exception.type":["System.InvalidOperationException"],
                     "error.stack_trace":["System.InvalidOperationException: gone\n   at A.B() in /build/src/A.cs:line 3"]}},
          {"fields":{"@timestamp":["2026-11-03T09:30:00.000Z"],"severity_text":["Critical"],"attributes.app":["billing"],"body.text":["Down"]}},
          {"fields":{"message":["no time, so left out"]}}
        ]}}
        """;

    [Fact]
    public async Task Searches_the_indices_with_the_login_and_reads_the_logs()
    {
        var answer = new Answer(HttpStatusCode.OK, TwoErrors);

        var result = await new ElasticLogReader("https://cloud.example:9243/", "logs-joktan*,logs-billing-*", "ApiKey abc", answer).ReadAsync(Hour, TestContext.Current.CancellationToken);

        Assert.Equal("https://cloud.example:9243/logs-joktan%2A,logs-billing-%2A/_search?ignore_unavailable=true&allow_no_indices=true", answer.LastUrl!.AbsoluteUri);
        Assert.Equal("ApiKey abc", answer.LastAuthorization);
        Assert.Null(result.Problem);
        Assert.False(result.Truncated);
        Assert.Equal(2, result.Entries.Count);
        var first = result.Entries[0];
        Assert.Equal((LogLevel.Error, "Joktan", "mqtt", "Could not connect", "System.InvalidOperationException"), (first.Level, first.App, first.Module, first.Message, first.ErrorType));
        Assert.Contains("line 3", first.StackTrace);
        Assert.Equal((LogLevel.Critical, "billing", "Down"), (result.Entries[1].Level, result.Entries[1].App, result.Entries[1].Message));
    }

    [Fact]
    public void Asks_for_the_window_the_levels_in_any_case_and_the_app_and_text()
    {
        var query = new LogQuery(Noon.AddHours(-1), Noon, LogLevels.Errors, "billing", "timeout", 20);

        var body = JsonDocument.Parse(ElasticLogReader.Body(query).ToJsonString()).RootElement;
        var text = body.ToString();

        Assert.Equal(20, body.GetProperty("size").GetInt32());
        Assert.Contains("\"gte\":\"2026-11-03T09:00:00.0000000Z\"", text);
        Assert.Contains("\"lte\":\"2026-11-03T10:00:00.0000000Z\"", text);
        Assert.Contains("\"fatal\"", text);
        Assert.Contains("\"err\"", text);
        Assert.DoesNotContain("\"warn\"", text);
        Assert.Contains("\"service.name\":{\"value\":\"billing\",\"case_insensitive\":true}", text);
        Assert.Contains("\"query\":\"timeout\"", text);
        Assert.Equal("desc", body.GetProperty("sort")[0].GetProperty("@timestamp").GetString());
    }

    [Fact]
    public void Says_when_there_were_more_than_the_limit()
    {
        var result = ElasticLogReader.Read(TwoErrors.Replace("\"value\":2", "\"value\":900"), 2);

        Assert.True(result.Truncated);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "{}", "Elasticsearch refused the login. Check the API key, or the username and password.")]
    [InlineData(HttpStatusCode.Forbidden, "{}", "The key may not read these logs. Give it read access to them, or set a key that may read.")]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"reason":"bad query"}}""", "Elasticsearch answered 400: bad query")]
    [InlineData(HttpStatusCode.OK, "not json", "Elasticsearch gave an answer that could not be read.")]
    public async Task Says_why_nothing_could_be_read(HttpStatusCode status, string body, string problem)
    {
        var result = await new ElasticLogReader("http://localhost:9200", "logs-*", null, new Answer(status, body)).ReadAsync(Hour, TestContext.Current.CancellationToken);

        Assert.Empty(result.Entries);
        Assert.Equal(problem, result.Problem);
    }

    [Theory]
    [InlineData("localhost:9200", "logs-*", "The address of Elasticsearch is not a whole http or https address.")]
    [InlineData("http://localhost:9200", "logs *", "The index pattern is not one Elasticsearch accepts.")]
    public async Task Sends_nothing_with_an_address_or_index_that_is_not_right(string url, string index, string problem)
    {
        var answer = new Answer(HttpStatusCode.OK, TwoErrors);

        var result = await new ElasticLogReader(url, index, null, answer).ReadAsync(Hour, TestContext.Current.CancellationToken);

        Assert.Equal(problem, result.Problem);
        Assert.Null(answer.LastUrl);
    }
}
