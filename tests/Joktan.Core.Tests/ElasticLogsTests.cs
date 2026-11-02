using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Joktan.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Joktan.Core.Tests;

[Collection(ProcessStateCollection.Name)]
public class ElasticLogsTests : DataFolderTest
{
    private sealed class Answer(HttpStatusCode status, string body = """{"errors":false}""") : HttpMessageHandler
    {
        public HttpStatusCode Status { get; set; } = status;
        public string? LastBody { get; private set; }
        public string? LastAuthorization { get; private set; }
        public Uri? LastUrl { get; private set; }
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastUrl = request.RequestUri;
            LastAuthorization = request.Headers.TryGetValues("Authorization", out var values) ? values.Single() : null;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(body) };
        }
    }

    private static readonly TimeSpan Never = TimeSpan.FromHours(1);
    private static readonly ElasticTarget Target = new(new Uri("https://cloud.example:9243/"), "logs-joktan-default", "ApiKey abc");

    [Fact]
    public async Task Logs_are_sent_in_bulk_to_the_data_stream_with_the_login()
    {
        var answer = new Answer(HttpStatusCode.OK);
        using var sink = new ElasticLogSink(() => Target, answer, Never);

        sink.Add("""{"message":"one"}""");
        sink.Add("""{"message":"two"}""");
        await sink.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://cloud.example:9243/_bulk", answer.LastUrl!.ToString());
        Assert.Equal("ApiKey abc", answer.LastAuthorization);
        var lines = answer.LastBody!.TrimEnd('\n').Split('\n');
        Assert.Equal(4, lines.Length);
        Assert.Contains("logs-joktan-default", lines[0]);
        Assert.Contains("create", lines[0]);
        Assert.Equal("""{"message":"two"}""", lines[3]);
        Assert.Null(ElasticLogSink.LastProblem);

        await sink.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, answer.Calls);
    }

    [Fact]
    public async Task What_could_not_be_delivered_is_tried_again()
    {
        var answer = new Answer(HttpStatusCode.ServiceUnavailable);
        using var sink = new ElasticLogSink(() => Target, answer, Never);

        sink.Add("""{"message":"one"}""");
        await sink.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Contains("503", ElasticLogSink.LastProblem);

        answer.Status = HttpStatusCode.OK;
        await sink.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, answer.Calls);
        Assert.Contains("one", answer.LastBody);
        Assert.Null(ElasticLogSink.LastProblem);
    }

    [Fact]
    public async Task With_nowhere_to_send_nothing_is_sent()
    {
        var answer = new Answer(HttpStatusCode.OK);
        using var sink = new ElasticLogSink(() => null, answer, Never);

        sink.Add("{}");
        await sink.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, answer.Calls);
    }

    [Fact]
    public async Task A_refused_document_is_reported_and_not_tried_for_ever()
    {
        var answer = new Answer(HttpStatusCode.OK, """{"errors":true,"items":[{"create":{"error":{"reason":"bad mapping"}}}]}""");
        using var sink = new ElasticLogSink(() => Target, answer, Never);

        sink.Add("{}");
        await sink.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal("bad mapping", ElasticLogSink.LastProblem);

        await sink.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, answer.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"version":{"number":"9.5.5"}}""", true, "9.5.5")]
    [InlineData(HttpStatusCode.Forbidden, "{}", true, "")]
    [InlineData(HttpStatusCode.Unauthorized, "{}", false, "401")]
    public async Task A_test_says_how_it_went(HttpStatusCode status, string body, bool reached, string text)
        => Assert.Equal((reached, text), await ElasticLogSink.TestAsync("https://cloud.example", "ApiKey abc", new Answer(status, body)));

    [Fact]
    public void An_elastic_document_names_the_app_and_module_and_keeps_the_values()
    {
        AppLog.Configure(new TelemetrySettings { LogsToElastic = true });
        var answer = new Answer(HttpStatusCode.OK);
        using (var sink = new ElasticLogSink(() => Target, answer, Never))
        using (var provider = new ElasticLoggerProvider(sink, "1.2.3"))
        {
            provider.CreateLogger("Joktan.Mqtt").LogWarning("To {Broker}", "localhost:1883");
        }

        var document = JsonDocument.Parse(answer.LastBody!.Split('\n')[1]).RootElement;
        Assert.Equal("joktan", document.GetProperty("app").GetString());
        Assert.Equal("mqtt", document.GetProperty("module").GetString());
        Assert.Equal("To localhost:1883", document.GetProperty("message").GetString());
        Assert.Equal("warning", document.GetProperty("log").GetProperty("level").GetString());
        Assert.Equal("localhost:1883", document.GetProperty("fields").GetProperty("Broker").GetString());
        AppLog.Configure(new TelemetrySettings());
    }

    [Fact]
    public void Each_destination_follows_its_own_switch_and_the_level()
    {
        var folder = Data.At("files");
        AppLog.Configure(new TelemetrySettings { LogsToFile = true, MinimumLevel = LogLevel.Warning });
        var answer = new Answer(HttpStatusCode.OK);
        using var elastic = new ElasticLogSink(() => Target, answer, Never);
        using var file = new FileLogSink(() => (folder, 7), Never);
        var toElastic = new ElasticLoggerProvider(elastic).CreateLogger("Joktan.App");
        var toFile = new FileLoggerProvider(file).CreateLogger("Joktan.App");

        Assert.False(toElastic.IsEnabled(LogLevel.Error));
        Assert.True(toFile.IsEnabled(LogLevel.Warning));
        Assert.False(toFile.IsEnabled(LogLevel.Information));

        AppLog.Configure(new TelemetrySettings());
    }
}
